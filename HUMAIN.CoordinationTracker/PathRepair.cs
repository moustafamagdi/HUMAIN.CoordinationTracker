using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private static string WithoutPaths(ClashRecord record)
        {
            var copy = record.CloneForDate(record.SnapshotDate);
            copy.ItemAPath = copy.ItemBPath = "";
            return ToSnapshotCsvLine(copy, "");
        }

        // Metadata-only migration. Validate the entire target before changing even one row.
        // Extra XML tests/results cannot add records, change statuses, or change test scope.
        private static int FillMissingPaths(SnapshotData target, SnapshotData xml)
        {
            var source = xml.Records.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            foreach (var row in target.Records)
            {
                ClashRecord match;
                if (!source.TryGetValue(row.ClashGuid, out match) || WithoutPaths(row) != WithoutPaths(match))
                    throw new InvalidDataException("Path repair stopped: the XML does not exactly match the latest snapshot (excluding paths). Clash: " + row.ClashGuid);
                if ((!string.IsNullOrWhiteSpace(row.ItemAPath) && row.ItemAPath != match.ItemAPath) ||
                    (!string.IsNullOrWhiteSpace(row.ItemBPath) && row.ItemBPath != match.ItemBPath))
                    throw new InvalidDataException("Path repair stopped: an existing source path conflicts with the XML. Clash: " + row.ClashGuid);
            }
            int filled = 0;
            foreach (var row in target.Records)
            {
                var match = source[row.ClashGuid];
                if (string.IsNullOrWhiteSpace(row.ItemAPath) && !string.IsNullOrWhiteSpace(match.ItemAPath)) { row.ItemAPath = match.ItemAPath; filled++; }
                if (string.IsNullOrWhiteSpace(row.ItemBPath) && !string.IsNullOrWhiteSpace(match.ItemBPath)) { row.ItemBPath = match.ItemBPath; filled++; }
            }
            return filled;
        }

        private static void RepairLatestPaths(string xmlFolder)
        {
            string revision = SnapshotRevision();
            var latestRef = GetSnapshotRefs().LastOrDefault();
            if (latestRef == null) throw new InvalidOperationException("No active snapshot to repair.");
            string snapshotName = Path.GetFileName(latestRef.FolderPath);
            var imported = ImportSnapshot(xmlFolder, latestRef.Timestamp);
            int preflight = FillMissingPaths(LoadSnapshot(latestRef), imported);
            if (preflight == 0) { Console.WriteLine("No missing paths to repair."); return; }
            CommitChange("Repair item paths " + snapshotName, delegate {
                var refs = GetSnapshotRefs();
                var latest = LoadSnapshot(refs.Last());
                var previous = refs.Count > 1 ? LoadSnapshot(refs[refs.Count - 2]) : new SnapshotData { Date = DateTime.MinValue };
                var tests = previous.Tests.Union(latest.Tests).ToList();
                var oldIds = tests.ToDictionary(t => t, t => QualityAlertId(previous, latest, t));
                int changed = FillMissingPaths(latest, imported);
                WriteClashCsv(Path.Combine(refs.Last().FolderPath, "snapshot.csv"), latest.Records, null);
                // Exact non-path equality was checked above. Preserve existing decisions without
                // approving new alerts, changing authors/reasons/timestamps, or weakening hashes.
                foreach (string test in tests)
                {
                    string oldId = oldIds[test], newId = QualityAlertId(previous, latest, test);
                    string reviewRoot = Path.Combine(AppRoot, "QualityReviews");
                    string oldFile = Path.Combine(reviewRoot, oldId + ".json"), newFile = Path.Combine(reviewRoot, newId + ".json");
                    if (oldId != newId && File.Exists(oldFile))
                    {
                        if (File.Exists(newFile) && File.ReadAllText(newFile) != File.ReadAllText(oldFile)) throw new InvalidDataException("Conflicting review during path repair.");
                        File.Copy(oldFile, newFile, true);
                        File.AppendAllText(Path.Combine(reviewRoot, "history.jsonl"), new JavaScriptSerializer().Serialize(new {
                            Action = "ItemPathMetadataRepair", PreviousAlertId = oldId, AlertId = newId,
                            Snapshot = snapshotName, At = DateTimeOffset.Now.ToString("O") }) + Environment.NewLine, Encoding.UTF8);
                    }
                }
                Console.WriteLine("Filled " + changed + " item paths; retained snapshot time, tests, records and statuses.");
            }, revision);
        }

        private static void RunPathRepairTests()
        {
            int count = 0;
            Action<bool, string> check = (ok, name) => { count++; if (!ok) throw new Exception("FAILED: " + name); };
            var row = new ClashRecord { SnapshotDate = new DateTime(2026, 9, 22), ClashGuid = "one", TestName = "test", Status = "active", ItemAElementId = "42", ItemBElementId = "43" };
            var source = row.CloneForDate(row.SnapshotDate); source.ItemAPath = "File > A.nwc"; source.ItemBPath = "File > B.nwc";
            var target = new SnapshotData { Records = new List<ClashRecord> { row }, Tests = new List<string> { "test" } };
            var xml = new SnapshotData { Records = new List<ClashRecord> { source }, Tests = new List<string> { "test", "extra" } };
            check(FillMissingPaths(target, xml) == 2, "repair both sides");
            check(row.ItemAPath == source.ItemAPath && row.ItemBPath == source.ItemBPath, "repair exact paths");
            check(target.Tests.Count == 1 && target.Records.Count == 1 && row.Status == "active", "repair does not change scope or status");
            check(FillMissingPaths(target, xml) == 0, "repair is idempotent");
            row.ItemAPath = row.ItemBPath = ""; source.Status = "resolved";
            bool rejected = false; try { FillMissingPaths(target, xml); } catch (InvalidDataException) { rejected = true; }
            check(rejected && row.ItemAPath == "", "reject changed status before mutation");
            source.Status = "active"; source.ItemAElementId = "99"; rejected = false;
            try { FillMissingPaths(target, xml); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "reject changed element identity");
            source.ItemAElementId = "42"; row.ItemAPath = "File > Different.nwc"; rejected = false;
            try { FillMissingPaths(target, xml); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "preserve conflicting existing paths");
            xml.Records.Clear(); rejected = false;
            try { FillMissingPaths(target, xml); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "reject incomplete XML export");
            Console.WriteLine("Path repair tests passed: " + count);
        }
    }
}
