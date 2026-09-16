using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private static Action<string> ManagementFault;
        private static readonly string[] ManagedNames = { "Snapshots", "QualityReviews", "PowerBI", "clash_history.csv" };

        private static void CleanTransaction(string path, string root)
        {
            string prefix = Path.GetFullPath(Path.Combine(root, "Transactions")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid temporary path.");
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static IDisposable AcquireProjectLock()
        {
            Directory.CreateDirectory(AppRoot);
            try { return new FileStream(Path.Combine(AppRoot, ".manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new IOException("This project is busy in another Tracker window. Retry after it finishes."); }
        }

        private static void SetRoot(string root)
        {
            AppRoot = root; SnapshotsRoot = Path.Combine(root, "Snapshots");
            PowerBiRoot = Path.Combine(root, "PowerBI"); HistoryFile = Path.Combine(root, "clash_history.csv");
        }

        private static void CopyTree(string source, string destination)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(destination);
            foreach (string f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(destination, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(source))
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked data folders are not supported: " + d);
                CopyTree(d, Path.Combine(destination, Path.GetFileName(d)));
            }
        }

        private static string BackupProject(string label)
        {
            string backup = Path.Combine(AppRoot, "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            foreach (string name in ManagedNames)
            {
                string source = Path.Combine(AppRoot, name), dest = Path.Combine(backup, name);
                if (Directory.Exists(source)) CopyTree(source, dest);
                else if (File.Exists(source)) File.Copy(source, dest);
            }
            File.WriteAllText(Path.Combine(backup, "backup.txt"), label + Environment.NewLine + DateTime.Now.ToString("O"), Encoding.UTF8);
            return backup;
        }

        private static string SnapshotRevision()
        {
            using (var sha = SHA256.Create())
            {
                var text = new StringBuilder();
                if (Directory.Exists(SnapshotsRoot)) foreach (string f in Directory.GetFiles(SnapshotsRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    text.Append(f.Substring(SnapshotsRoot.Length));
                    using (var stream = File.OpenRead(f)) text.Append(Convert.ToBase64String(sha.ComputeHash(stream)));
                }
                string reviews = Path.Combine(AppRoot, "QualityReviews");
                if (Directory.Exists(reviews)) foreach (string f in Directory.GetFiles(reviews, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                { text.Append(f.Substring(reviews.Length)); using (var stream = File.OpenRead(f)) text.Append(Convert.ToBase64String(sha.ComputeHash(stream))); }
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
            }
        }

        // Directory publication is recoverable, not a cross-file-system atomic transaction.
        // The journal is durable before the first move. Readers must refresh after completion.
        private static void RecoverPending()
        {
            string journal = Path.Combine(AppRoot, "pending-operation.txt");
            if (!File.Exists(journal)) return;
            string operation = File.ReadAllText(journal).Trim();
            string prefix = Path.GetFullPath(Path.Combine(AppRoot, "Transactions")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(operation).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery journal.");
            foreach (string name in ManagedNames.Reverse())
            {
                string live = Path.Combine(AppRoot, name), old = Path.Combine(operation, "old", name);
                string staged = Path.Combine(operation, "stage", name), discard = Path.Combine(operation, "discard", name);
                bool existsOld = Directory.Exists(old) || File.Exists(old);
                bool existsLive = Directory.Exists(live) || File.Exists(live);
                bool hadNone = File.Exists(Path.Combine(operation, "absent-" + name));
                if (existsOld || (hadNone && !Directory.Exists(staged) && !File.Exists(staged)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(discard));
                    if (existsLive) MoveManaged(live, discard + "_" + Guid.NewGuid().ToString("N"));
                    if (existsOld) MoveManaged(old, live);
                }
            }
            File.Delete(journal);
        }

        private static void MoveManaged(string from, string to)
        {
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
        }

        private static DashboardSummary CommitChange(string label, Action change, string expectedRevision = null)
        {
            using (AcquireProjectLock())
            {
                RecoverPending();
                if (expectedRevision != null && expectedRevision != SnapshotRevision()) throw new IOException("Snapshots changed after preview. Preview again before applying.");
                string liveRoot = AppRoot;
                string operation = Path.Combine(liveRoot, "Transactions", Guid.NewGuid().ToString("N"));
                string stage = Path.Combine(operation, "stage");
                Directory.CreateDirectory(stage);
                CopyTree(SnapshotsRoot, Path.Combine(stage, "Snapshots"));
                CopyTree(Path.Combine(AppRoot, "QualityReviews"), Path.Combine(stage, "QualityReviews"));
                DashboardSummary result;
                try
                {
                    SetRoot(stage); EnsureFolders(); change(); ValidateSnapshots(); result = RebuildDerivedFiles();
                    if (ManagementFault != null) ManagementFault("built");
                }
                finally { SetRoot(liveRoot); }
                string backup = BackupProject(label);
                Directory.CreateDirectory(Path.Combine(operation, "old"));
                foreach (string name in ManagedNames)
                    if (!Directory.Exists(Path.Combine(liveRoot, name)) && !File.Exists(Path.Combine(liveRoot, name))) File.WriteAllText(Path.Combine(operation, "absent-" + name), "");
                string journal = Path.Combine(liveRoot, "pending-operation.txt");
                using (var stream = new FileStream(journal, FileMode.Create, FileAccess.Write, FileShare.None))
                { byte[] bytes = Encoding.UTF8.GetBytes(operation); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                try
                {
                    foreach (string name in ManagedNames)
                    {
                        string live = Path.Combine(liveRoot, name);
                        if (Directory.Exists(live) || File.Exists(live)) MoveManaged(live, Path.Combine(operation, "old", name));
                        MoveManaged(Path.Combine(stage, name), live);
                        if (ManagementFault != null) ManagementFault("published-" + name);
                    }
                    File.AppendAllText(Path.Combine(liveRoot, "operations.log"), DateTime.Now.ToString("O") + " | " + label.Replace('\r', ' ').Replace('\n', ' ') + " | backup=" + backup + Environment.NewLine);
                    File.Delete(journal);
                }
                catch { RecoverPending(); throw; }
                CleanTransaction(operation, liveRoot);
                return result;
            }
        }

        private static void ValidateSnapshots()
        {
            var refs = GetSnapshotRefs();
            if (refs.GroupBy(x => x.Timestamp).Any(g => g.Count() > 1)) throw new InvalidDataException("Two active snapshots have the same timestamp.");
            foreach (var s in refs)
                if (!File.Exists(Path.Combine(s.FolderPath, "snapshot.csv")) || !File.Exists(Path.Combine(s.FolderPath, "tests.csv")))
                    throw new InvalidDataException("Incomplete saved snapshot: " + Path.GetFileName(s.FolderPath));
        }

        private static void ChangeExclusion(string folderName, bool exclude, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a reason for this change.");
            var item = GetSnapshotRefs(true).Single(x => Path.GetFileName(x.FolderPath) == folderName);
            string marker = Path.Combine(item.FolderPath, "excluded.txt");
            if (exclude) File.WriteAllText(marker, DateTime.Now.ToString("O") + Environment.NewLine + reason, Encoding.UTF8);
            else if (File.Exists(marker)) File.Delete(marker);
        }

        private static string MessagePreview(SnapshotData imported, DateTime timestamp, string replacing)
        {
            var refs = GetSnapshotRefs().Where(x => Path.GetFileName(x.FolderPath) != replacing).ToList();
            var prior = refs.LastOrDefault(x => x.Timestamp < timestamp);
            var previous = prior == null ? SnapshotData.Empty() : LoadSnapshot(prior);
            var result = new StringBuilder();
            result.AppendLine("Snapshot: " + timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            result.AppendLine("Tests: " + imported.Tests.Count + " | Records: " + imported.Records.Count);
            result.AppendLine(prior == null ? "BASELINE: this becomes the first active snapshot." : "Compared with: " + prior.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            var known = new HashSet<string>(imported.Records.Select(x => x.ClashGuid), StringComparer.OrdinalIgnoreCase);
            result.AppendLine("Previously present GUIDs now absent: " + previous.Records.Count(x => !known.Contains(x.ClashGuid)));
            var prevTests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase);
            var nowTests = new HashSet<string>(imported.Tests, StringComparer.OrdinalIgnoreCase);
            foreach (string test in prevTests.Union(nowTests).OrderBy(x => x))
            {
                var q = BuildQuality(prevTests.Contains(test), nowTests.Contains(test), previous.Records.Where(x => x.TestName == test).ToList(), imported.Records.Where(x => x.TestName == test).ToList(), prior == null);
                if (q.HasAlert > 0) result.AppendLine("REVIEW " + test + ": " + q.Reason);
            }
            result.AppendLine("All later intervals, first-seen ages and forecasts will be recalculated.");
            result.AppendLine("Approve only a complete export with the intended test scope. A backup is automatic.");
            return result.ToString();
        }

        private static void RunManagementTests()
        {
            string original = AppRoot;
            string root = Path.Combine(Path.GetTempPath(), "HUMAIN-management-tests-" + Guid.NewGuid().ToString("N"));
            int count = 0;
            Action<bool, string> check = (ok, name) => { if (!ok) throw new Exception("FAILED management: " + name); count++; };
            try
            {
                SetRoot(root); EnsureFolders();
                var first = new DateTime(2026, 1, 1, 9, 0, 0);
                Action seed = delegate {
                    for (int i = 0; i < 3; i++)
                    {
                        var date = first.AddDays(i);
                        SaveSnapshot(new SnapshotChoice { Timestamp = date, FolderPath = Path.Combine(SnapshotsRoot, SnapshotFolderName(date)) }, new SnapshotData {
                            Date = date, Tests = new List<string> { "01_Critical_AR vs STR" },
                            Records = new List<ClashRecord> { new ClashRecord { SnapshotDate = date, ClashGuid = "test-guid", TestName = "01_Critical_AR vs STR", Status = i == 1 ? "resolved" : "active", Severity = "Critical", DisciplineA = "AR", DisciplineB = "STR", DisciplinePair = "AR vs STR" } }
                        });
                    }
                };
                CommitChange("seed", seed); check(GetSnapshotRefs().Count == 3, "seed");
                string middle = SnapshotFolderName(first.AddDays(1));
                string initialRevision = SnapshotRevision();
                string preview = PreviewChange(() => ChangeExclusion(middle, true, "preview test"));
                check(preview.Contains("BEFORE") && preview.Contains("AFTER"), "reviewable before and after");
                check(initialRevision == SnapshotRevision() && GetSnapshotRefs().Count == 3, "preview does not change live snapshots");
                CommitChange("exclude", () => ChangeExclusion(middle, true, "bad export"));
                check(GetSnapshotRefs().Count == 2 && GetSnapshotRefs(true).Count == 3, "exclude retains original");
                check(ProjectSummary().Contains("All-history ResolvedFromActionable: 0"), "excluded resolution no longer counted");
                CommitChange("restore", () => ChangeExclusion(middle, false, "corrected decision"));
                check(GetSnapshotRefs().Count == 3, "restore");
                string before = File.ReadAllText(Path.Combine(PowerBiRoot, "OperationalProgress.csv"));
                ManagementFault = point => { if (point == "published-Snapshots") throw new IOException("injected failure"); };
                try { CommitChange("failure", () => ChangeExclusion(middle, true, "test")); check(false, "must fail"); } catch (IOException) { }
                ManagementFault = null;
                check(GetSnapshotRefs().Count == 3, "publication rollback snapshots");
                check(File.ReadAllText(Path.Combine(PowerBiRoot, "OperationalProgress.csv")) == before, "publication rollback outputs");
                ManagementFault = point => { if (point == "published-PowerBI") throw new IOException("late failure"); };
                try { CommitChange("late failure", () => ChangeExclusion(middle, true, "test")); } catch (IOException) { }
                ManagementFault = null;
                check(GetSnapshotRefs().Count == 3 && File.ReadAllText(Path.Combine(PowerBiRoot, "OperationalProgress.csv")) == before, "late publication rollback");
                string interrupted = Path.Combine(root, "Transactions", "interrupted-test");
                Directory.CreateDirectory(Path.Combine(interrupted, "old"));
                File.WriteAllText(Path.Combine(root, "pending-operation.txt"), interrupted);
                Directory.Move(SnapshotsRoot, Path.Combine(interrupted, "old", "Snapshots"));
                Directory.CreateDirectory(SnapshotsRoot);
                RecoverPending();
                check(GetSnapshotRefs().Count == 3 && !File.Exists(Path.Combine(root, "pending-operation.txt")), "startup recovery from interrupted publication");
                RecoverPending(); check(GetSnapshotRefs().Count == 3, "recovery is repeatable");
                ManagementFault = point => { if (point == "built") throw new IOException("build failure"); };
                try { CommitChange("build failure", () => ChangeExclusion(middle, true, "test")); } catch (IOException) { }
                ManagementFault = null; check(GetSnapshotRefs().Count == 3 && !File.Exists(Path.Combine(root, "pending-operation.txt")), "build failure leaves live unchanged");
                try { CommitChange("stale", delegate { }, "stale"); check(false, "must reject stale"); } catch (IOException) { count++; }
                using (AcquireProjectLock()) { try { using (AcquireProjectLock()) { check(false, "must lock"); } } catch (IOException) { count++; } }
                CommitChange("exclude baseline", () => ChangeExclusion(SnapshotFolderName(first), true, "test"));
                check(GetSnapshotRefs().First().Timestamp == first.AddDays(1), "baseline changes");
                CommitChange("exclude latest", () => ChangeExclusion(SnapshotFolderName(first.AddDays(2)), true, "test"));
                check(GetSnapshotRefs().Last().Timestamp == first.AddDays(1), "latest falls back");
                CommitChange("exclude final", () => ChangeExclusion(middle, true, "test"));
                check(GetSnapshotRefs().Count == 0, "empty project rebuild");
                check(Directory.GetDirectories(Path.Combine(root, "Backups")).Length >= 6, "automatic backups");
                string xml = Path.Combine(root, "test-xml"); Directory.CreateDirectory(xml);
                File.WriteAllText(Path.Combine(xml, "bad.xml"), "<broken");
                try { ImportSnapshot(xml, first); check(false, "reject malformed XML"); } catch (InvalidDataException) { count++; }
                File.WriteAllText(Path.Combine(xml, "bad.xml"), "<exchange><clashtest name=\"01_Critical_AR vs STR\"><clashresult guid=\"same\" status=\"active\"/><clashresult guid=\"same\" status=\"active\"/></clashtest></exchange>");
                try { ImportSnapshot(xml, first); check(false, "reject duplicate GUID"); } catch (InvalidDataException) { count++; }
                File.WriteAllText(Path.Combine(xml, "bad.xml"), "<exchange><clashtest name=\"empty test\"/></exchange>");
                var empty = ImportSnapshot(xml, first); check(empty.Tests.Count == 1 && empty.Records.Count == 0, "zero-clash test preserved");
                File.WriteAllText(Path.Combine(xml, "bad.xml"), "<unrelated/>");
                try { ImportSnapshot(xml, first); check(false, "reject unrelated XML"); } catch (InvalidDataException) { count++; }
                Console.WriteLine("PASS: " + count + " management assertions. Fixtures: " + root);
            }
            finally { ManagementFault = null; SetRoot(original); }
        }
    }
}
