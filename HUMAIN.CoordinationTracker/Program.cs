using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private static string AppRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUMAIN.CoordinationTracker");

        private static string SnapshotsRoot = Path.Combine(AppRoot, "Snapshots");
        private static string PowerBiRoot = Path.Combine(AppRoot, "PowerBI");
        private static string HistoryFile = Path.Combine(AppRoot, "clash_history.csv");

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                if (args.Contains("--self-test")) { RunWorkflowTests(); RunQualityTests(); RunManagementTests(); return; }
                int rootArg = Array.IndexOf(args, "--data-root");
                if (rootArg >= 0)
                {
                    if (rootArg + 1 >= args.Length) throw new ArgumentException("--data-root requires a directory");
                    AppRoot = Path.GetFullPath(args[rootArg + 1]);
                    SnapshotsRoot = Path.Combine(AppRoot, "Snapshots");
                    PowerBiRoot = Path.Combine(AppRoot, "PowerBI");
                    HistoryFile = Path.Combine(AppRoot, "clash_history.csv");
                }
                EnsureFolders();
                using (AcquireProjectLock()) RecoverPending();
                if (args.Length == 0 || args.Contains("--gui")) { System.Windows.Forms.Application.EnableVisualStyles(); System.Windows.Forms.Application.Run(new ManagerForm()); return; }
                if (args.Contains("--rebuild")) { CommitChange("Rebuild", delegate { }); Console.WriteLine("Rebuild completed: " + PowerBiRoot); return; }

                Console.WriteLine("HUMAIN Coordination Tracker");
                Console.WriteLine("===========================");
                Console.WriteLine();
                Console.WriteLine("Navisworks XML Historical Clash Tracker");
                Console.WriteLine();

                string inputFolder = AskForInputFolder();
                DateTime requestedDate = AskForSnapshotDate();
                SnapshotChoice choice = ResolveSnapshotChoice(requestedDate);
                if (choice.Cancelled)
                {
                    Console.WriteLine("Import cancelled.");
                    Pause();
                    return;
                }

                DateTime snapshotDateTime = choice.Timestamp;
                SnapshotData imported = ImportSnapshot(inputFolder, snapshotDateTime);

                if (imported.Records.Count == 0 && imported.Tests.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No clash tests or clash results were found in the selected XML files.");
                    Console.ResetColor();
                    Pause();
                    return;
                }

                SnapshotData previous = LoadPreviousSnapshot(snapshotDateTime, choice.FolderPath);
                HashSet<string> everSeenBefore = LoadEverSeenBefore(snapshotDateTime, choice.FolderPath);
                ComparisonResult comparison = CompareSnapshots(previous.Records, imported.Records, everSeenBefore, previous.Date == DateTime.MinValue);

                string expectedRevision = SnapshotRevision();
                Console.WriteLine(MessagePreview(imported, choice.Timestamp, choice.IsOverwrite ? Path.GetFileName(choice.FolderPath) : null));
                Console.Write("Commit this import? [Y/N]: ");
                if (!string.Equals(Console.ReadLine(), "Y", StringComparison.OrdinalIgnoreCase)) return;
                string snapshotName = Path.GetFileName(choice.FolderPath);
                DashboardSummary dashboard = CommitChange("Console import " + snapshotName, delegate { choice.FolderPath = Path.Combine(SnapshotsRoot, snapshotName); SaveSnapshot(choice, imported); }, expectedRevision);
                PrintSummary(snapshotDateTime, imported, previous, comparison, dashboard);

                Console.WriteLine();
                Console.WriteLine("Power BI ready files:");
                Console.WriteLine(PowerBiRoot);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Import completed successfully.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("ERROR:");
                Console.WriteLine(ex.Message);
                Environment.ExitCode = 1;
                if (args.Length > 0) return;
                Console.ResetColor();
            }

            Pause();
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(SnapshotsRoot);
            Directory.CreateDirectory(PowerBiRoot);
        }

        private static string AskForInputFolder()
        {
            while (true)
            {
                Console.WriteLine("Enter the folder containing Navisworks XML clash reports:");
                string value = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    value = value.Trim().Trim('"');
                    if (Directory.Exists(value)) return value;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Folder not found. Try again.");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        private static DateTime AskForSnapshotDate()
        {
            while (true)
            {
                Console.Write("Snapshot date [yyyy-MM-dd] (Enter = today): ");
                string value = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(value)) return DateTime.Today;

                DateTime date;
                if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date)) return date.Date;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Invalid date. Example: 2026-09-10");
                Console.ResetColor();
            }
        }

        private static SnapshotChoice ResolveSnapshotChoice(DateTime requestedDate)
        {
            List<SnapshotRef> sameDay = GetSnapshotRefs()
                .Where(x => x.Timestamp.Date == requestedDate.Date)
                .OrderBy(x => x.Timestamp)
                .ToList();

            if (sameDay.Count == 0)
            {
                DateTime timestamp = BuildNewSnapshotTimestamp(requestedDate);
                return new SnapshotChoice
                {
                    Timestamp = timestamp,
                    FolderPath = Path.Combine(SnapshotsRoot, SnapshotFolderName(timestamp)),
                    IsOverwrite = false
                };
            }

            SnapshotRef latest = sameDay.Last();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Snapshot(s) already exist for " + requestedDate.ToString("yyyy-MM-dd") + ".");
            foreach (SnapshotRef item in sameDay)
                Console.WriteLine("  - " + item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Choose what to do:");
            Console.WriteLine("  O = Overwrite latest snapshot (" + latest.Timestamp.ToString("HH:mm:ss") + ")");
            Console.WriteLine("  N = Save as a NEW snapshot/version for the same day");
            Console.WriteLine("  C = Cancel");

            while (true)
            {
                Console.Write("Your choice [O/N/C]: ");
                string answer = (Console.ReadLine() ?? string.Empty).Trim().ToUpperInvariant();

                if (answer == "O")
                {
                    return new SnapshotChoice
                    {
                        Timestamp = latest.Timestamp,
                        FolderPath = latest.FolderPath,
                        IsOverwrite = true
                    };
                }

                if (answer == "N")
                {
                    DateTime timestamp = BuildNewSnapshotTimestamp(requestedDate);
                    while (GetSnapshotRefs().Any(x => x.Timestamp == timestamp))
                        timestamp = timestamp.AddSeconds(1);

                    return new SnapshotChoice
                    {
                        Timestamp = timestamp,
                        FolderPath = Path.Combine(SnapshotsRoot, SnapshotFolderName(timestamp)),
                        IsOverwrite = false
                    };
                }

                if (answer == "C") return new SnapshotChoice { Cancelled = true };
                Console.WriteLine("Please enter O, N, or C.");
            }
        }

        private static DateTime BuildNewSnapshotTimestamp(DateTime requestedDate)
        {
            DateTime now = DateTime.Now;
            return new DateTime(requestedDate.Year, requestedDate.Month, requestedDate.Day,
                now.Hour, now.Minute, now.Second);
        }

        private static string SnapshotFolderName(DateTime timestamp)
        {
            return timestamp.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        }

        private static SnapshotData ImportSnapshot(string folder, DateTime snapshotDateTime)
        {
            string[] files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories);
            Console.WriteLine();
            Console.WriteLine("XML files found: " + files.Length);
            Console.WriteLine("Importing...");

            var results = new List<ClashRecord>();
            var tests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int failedFiles = 0;

            foreach (string file in files)
            {
                try
                {
                    XDocument doc = XDocument.Load(file, LoadOptions.None);
                    if (doc.Descendants().Count(x => LocalName(x) == "clashtest") != 1) throw new InvalidDataException("Expected one Clash Test per XML. Export All tests (separate).");
                    string testName = GetTestName(doc, file);
                    tests.Add(testName);

                    foreach (XElement clash in doc.Descendants().Where(x => LocalName(x) == "clashresult"))
                    {
                        string guid = Attr(clash, "guid");
                        if (string.IsNullOrWhiteSpace(guid)) throw new InvalidDataException("A clash result is missing its GUID.");

                        TestMetadata metadata = ParseTestMetadata(testName);
                        var record = new ClashRecord
                        {
                            SnapshotDate = snapshotDateTime,
                            TestName = testName,
                            ClashGuid = guid,
                            ClashName = Attr(clash, "name"),
                            Status = FirstNonEmpty(Attr(clash, "status"), ElementValue(clash, "resultstatus")),
                            Distance = ParseDouble(Attr(clash, "distance")),
                            DateFound = ParseNavisworksDate(clash, "createddate"),
                            GridLocation = ElementValue(clash, "gridlocation"),
                            Description = ElementValue(clash, "description"),
                            Comments = ElementValue(clash, "comments"),
                            Severity = metadata.Severity,
                            DisciplineA = metadata.DisciplineA,
                            DisciplineB = metadata.DisciplineB,
                            DisciplinePair = metadata.DisciplinePair
                        };

                        XElement point = clash.Descendants().FirstOrDefault(x => LocalName(x) == "clashpoint");
                        XElement pos = point == null ? null : point.Descendants().FirstOrDefault(x => LocalName(x) == "pos3f");
                        if (pos != null)
                        {
                            record.X = ParseDouble(Attr(pos, "x"));
                            record.Y = ParseDouble(Attr(pos, "y"));
                            record.Z = ParseDouble(Attr(pos, "z"));
                        }

                        var clashObjects = clash.Descendants().Where(x => LocalName(x) == "clashobject").Take(2).ToList();
                        if (clashObjects.Count > 0) FillObject(record, clashObjects[0], true);
                        if (clashObjects.Count > 1) FillObject(record, clashObjects[1], false);
                        results.Add(record);
                    }
                }
                catch (Exception ex)
                {
                    failedFiles++;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Could not parse: " + Path.GetFileName(file));
                    Console.WriteLine("  " + ex.Message);
                    Console.ResetColor();
                }
            }

            if (failedFiles > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                throw new InvalidDataException(failedFiles + " XML file(s) could not be parsed. Nothing was imported. Fix the export and retry.");
            }

            if (results.GroupBy(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate clash GUIDs found. Remove duplicate XML reports before importing.");
            return new SnapshotData
            {
                Date = snapshotDateTime,
                Records = results.GroupBy(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList(),
                Tests = tests.OrderBy(x => x).ToList()
            };
        }

        private static string GetTestName(XDocument doc, string file)
        {
            XElement clashTest = doc.Descendants().FirstOrDefault(x => LocalName(x) == "clashtest");
            string name = clashTest == null ? null : Attr(clashTest, "name");
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();

            XElement batchTest = doc.Descendants().FirstOrDefault(x => LocalName(x) == "batchtest");
            name = batchTest == null ? null : Attr(batchTest, "name");
            return string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name.Trim();
        }

        private static TestMetadata ParseTestMetadata(string testName)
        {
            string text = testName ?? string.Empty;
            string severity = "Unclassified";
            if (text.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) >= 0) severity = "Critical";
            else if (text.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0) severity = "Medium";
            else if (text.IndexOf("Low", StringComparison.OrdinalIgnoreCase) >= 0) severity = "Low";

            string cleaned = text;
            int firstUnderscore = cleaned.IndexOf('_');
            if (firstUnderscore >= 0) cleaned = cleaned.Substring(firstUnderscore + 1);
            firstUnderscore = cleaned.IndexOf('_');
            if (firstUnderscore >= 0 && (cleaned.StartsWith("Critical", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("Medium", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("Low", StringComparison.OrdinalIgnoreCase)))
                cleaned = cleaned.Substring(firstUnderscore + 1);

            string[] parts = cleaned.Split(new[] { " vs " }, StringSplitOptions.None);
            string a = parts.Length > 0 ? NormalizeDiscipline(parts[0]) : "Unknown";
            string b = parts.Length > 1 ? NormalizeDiscipline(parts[1]) : "Unknown";

            return new TestMetadata
            {
                Severity = severity,
                DisciplineA = a,
                DisciplineB = b,
                DisciplinePair = a + " vs " + b
            };
        }

        private static string NormalizeDiscipline(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return "Unknown";
            string[] known = { "AR", "STR", "EL", "FF", "PL", "HV", "ME", "LC", "ICT", "SEC", "FA" };
            foreach (string code in known)
                if (text.StartsWith(code, StringComparison.OrdinalIgnoreCase)) return code.ToUpperInvariant();
            int underscore = text.IndexOf('_');
            if (underscore > 0) return text.Substring(0, underscore).Trim();
            int space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space).Trim() : text;
        }

        private static void FillObject(ClashRecord record, XElement clashObject, bool first)
        {
            string objectName = FirstNonEmpty(ElementValue(clashObject, "objectname"), FindSmartTag(clashObject, "Item Name"));
            string itemId = FirstNonEmpty(FindObjectAttribute(clashObject, "Element ID"), FindSmartTag(clashObject, "Item ID"));
            string layer = FindSmartTag(clashObject, "Layer");
            string itemPath = FindSmartTag(clashObject, "Item Path");

            if (first)
            {
                record.ItemAName = objectName; record.ItemAElementId = itemId;
                record.ItemALayer = layer; record.ItemAPath = itemPath;
            }
            else
            {
                record.ItemBName = objectName; record.ItemBElementId = itemId;
                record.ItemBLayer = layer; record.ItemBPath = itemPath;
            }
        }

        private static string FindObjectAttribute(XElement clashObject, string attributeName)
        {
            foreach (XElement oa in clashObject.Descendants().Where(x => LocalName(x) == "objectattribute"))
            {
                if (string.Equals(ElementValue(oa, "name"), attributeName, StringComparison.OrdinalIgnoreCase))
                    return ElementValue(oa, "value");
            }
            return string.Empty;
        }

        private static string FindSmartTag(XElement clashObject, string tagName)
        {
            foreach (XElement smartTag in clashObject.Descendants().Where(x => LocalName(x) == "smarttag"))
            {
                if (string.Equals(ElementValue(smartTag, "name"), tagName, StringComparison.OrdinalIgnoreCase))
                    return ElementValue(smartTag, "value");
            }
            return string.Empty;
        }

        private static SnapshotData LoadPreviousSnapshot(DateTime currentTimestamp, string currentFolder)
        {
            SnapshotRef previous = GetSnapshotRefs()
                .Where(x => x.Timestamp < currentTimestamp && !SamePath(x.FolderPath, currentFolder))
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefault();
            return previous == null ? SnapshotData.Empty() : LoadSnapshot(previous);
        }

        private static HashSet<string> LoadEverSeenBefore(DateTime currentTimestamp, string currentFolder)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SnapshotRef snapshot in GetSnapshotRefs()
                .Where(x => x.Timestamp < currentTimestamp && !SamePath(x.FolderPath, currentFolder))
                .OrderBy(x => x.Timestamp))
            {
                foreach (ClashRecord record in LoadSnapshot(snapshot).Records) set.Add(record.ClashGuid);
            }
            return set;
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a ?? string.Empty).TrimEnd('\\'),
                Path.GetFullPath(b ?? string.Empty).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static ComparisonResult CompareSnapshots(List<ClashRecord> previous, List<ClashRecord> current, HashSet<string> everSeenBefore, bool isBaseline)
        {
            var previousMap = previous.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var currentMap = current.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var result = new ComparisonResult();
            foreach (ClashRecord c in current)
            {
                ClashRecord p;
                bool wasPresent = previousMap.TryGetValue(c.ClashGuid, out p);
                if (IsResolved(c)) result.StateByGuid[c.ClashGuid] = "Resolved";
                else if (isBaseline) result.StateByGuid[c.ClashGuid] = "Baseline";
                else if (wasPresent && !IsResolved(p)) result.StateByGuid[c.ClashGuid] = "Existing";
                else if (everSeenBefore.Contains(c.ClashGuid)) result.StateByGuid[c.ClashGuid] = "Reopened";
                else result.StateByGuid[c.ClashGuid] = "New";
            }
            // Count a closure only on the transition out of unresolved; later Compact is not a second closure.
            if (!isBaseline) foreach (ClashRecord p in previous.Where(x => !IsResolved(x)))
            {
                ClashRecord c;
                if (!currentMap.TryGetValue(p.ClashGuid, out c) || IsResolved(c)) result.Resolved.Add(c ?? p);
            }
            return result;
        }

        private static void SaveSnapshot(SnapshotChoice choice, SnapshotData snapshot)
        {
            Directory.CreateDirectory(choice.FolderPath);
            WriteClashCsv(Path.Combine(choice.FolderPath, "snapshot.csv"), snapshot.Records, null);

            using (var writer = new StreamWriter(Path.Combine(choice.FolderPath, "tests.csv"), false, new UTF8Encoding(true)))
            {
                writer.WriteLine("TestName");
                foreach (string test in snapshot.Tests.OrderBy(x => x)) writer.WriteLine(Csv(test));
            }

            using (var writer = new StreamWriter(Path.Combine(choice.FolderPath, "metadata.csv"), false, new UTF8Encoding(true)))
            {
                writer.WriteLine("SnapshotDateTime");
                writer.WriteLine(Csv(choice.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")));
            }

            Console.ForegroundColor = choice.IsOverwrite ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine(choice.IsOverwrite
                ? "Existing snapshot was overwritten: " + choice.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
                : "New snapshot saved: " + choice.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.ResetColor();
        }

        private static List<SnapshotRef> GetSnapshotRefs(bool includeExcluded = false)
        {
            var result = new List<SnapshotRef>();
            if (!Directory.Exists(SnapshotsRoot)) return result;

            foreach (string folder in Directory.GetDirectories(SnapshotsRoot))
            {
                if (!includeExcluded && File.Exists(Path.Combine(folder, "excluded.txt"))) continue;
                string name = Path.GetFileName(folder);
                DateTime timestamp;
                bool valid = DateTime.TryParseExact(name, "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out timestamp);

                if (!valid)
                    valid = DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out timestamp);

                string metadata = Path.Combine(folder, "metadata.csv");
                if (File.Exists(metadata))
                {
                    try
                    {
                        string line = File.ReadLines(metadata).Skip(1).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            List<string> v = ParseCsvLine(line);
                            DateTime fromMetadata;
                            if (v.Count > 0 && DateTime.TryParse(v[0], CultureInfo.InvariantCulture,
                                DateTimeStyles.AllowWhiteSpaces, out fromMetadata))
                            {
                                timestamp = fromMetadata;
                                valid = true;
                            }
                        }
                    }
                    catch { }
                }

                if (valid)
                    result.Add(new SnapshotRef { Timestamp = timestamp, FolderPath = folder });
            }

            return result.OrderBy(x => x.Timestamp).ThenBy(x => x.FolderPath).ToList();
        }

        private static SnapshotData LoadSnapshot(SnapshotRef snapshot)
        {
            string snapshotFile = Path.Combine(snapshot.FolderPath, "snapshot.csv");
            string testsFile = Path.Combine(snapshot.FolderPath, "tests.csv");
            var data = new SnapshotData
            {
                Date = snapshot.Timestamp,
                Records = File.Exists(snapshotFile) ? ReadSnapshotCsv(snapshotFile) : new List<ClashRecord>(),
                Tests = new List<string>()
            };

            foreach (ClashRecord record in data.Records) record.SnapshotDate = snapshot.Timestamp;

            if (File.Exists(testsFile))
            {
                data.Tests = File.ReadLines(testsFile).Skip(1).Select(ParseCsvLine)
                    .Where(x => x.Count > 0 && !string.IsNullOrWhiteSpace(x[0])).Select(x => x[0])
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            }
            else
            {
                data.Tests = data.Records.Select(x => x.TestName).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            }
            return data;
        }

        private static DashboardSummary RebuildDerivedFiles()
        {
            Directory.CreateDirectory(PowerBiRoot);
            string dailyFile = Path.Combine(PowerBiRoot, "DailyProgress.csv");
            string testFile = Path.Combine(PowerBiRoot, "TestPerformance.csv");
            string currentFile = Path.Combine(PowerBiRoot, "CurrentClashes.csv");
            string lifecycleFile = Path.Combine(PowerBiRoot, "ClashLifecycle.csv");
            string kpiFile = Path.Combine(PowerBiRoot, "DashboardKPI.csv");
            string powerBiHistory = Path.Combine(PowerBiRoot, "ClashHistory.csv");

            List<SnapshotRef> snapshots = GetSnapshotRefs();
            var dailyRows = new List<DailyMetric>();
            var lifecycle = new Dictionary<string, LifecycleRecord>(StringComparer.OrdinalIgnoreCase);
            SnapshotData previous = SnapshotData.Empty();
            var everSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SnapshotData latest = SnapshotData.Empty();
            ComparisonResult latestComparison = new ComparisonResult();

            using (var historyWriter = new StreamWriter(HistoryFile, false, new UTF8Encoding(true)))
            using (var testWriter = new StreamWriter(testFile, false, new UTF8Encoding(true)))
            using (var operationalWriter = new StreamWriter(Path.Combine(PowerBiRoot, "OperationalProgress.csv"), false, new UTF8Encoding(true)))
            using (var qualityWriter = new StreamWriter(Path.Combine(PowerBiRoot, "SnapshotQuality.csv"), false, new UTF8Encoding(true)))
            {
                historyWriter.WriteLine(HistoryHeader());
                operationalWriter.WriteLine(OperationalHeader);
                qualityWriter.WriteLine(QualityHeader);
                testWriter.WriteLine("SnapshotDateTime,SnapshotDate,TestName,Severity,DisciplineA,DisciplineB,DisciplinePair,Previous,Current,New,Reopened,Existing,Resolved,NetChange,ResolutionRatePct");

                foreach (SnapshotRef snapshotRef in snapshots)
                {
                    SnapshotData current = LoadSnapshot(snapshotRef);
                    ComparisonResult comparison = CompareSnapshots(previous.Records, current.Records, everSeen, previous.Date == DateTime.MinValue);
                    UpdateLifecycle(lifecycle, current.Date, current, comparison);
                    WriteHistoryRows(historyWriter, current.Date, current.Records, comparison, lifecycle);
                    dailyRows.Add(BuildDailyMetric(current.Date, previous, current, comparison));
                    WriteTestPerformanceRows(testWriter, current.Date, previous, current, comparison);
                    WriteOperationalRows(operationalWriter, previous, current, everSeen);
                    WriteQualityRows(qualityWriter, previous, current);

                    foreach (ClashRecord record in current.Records) everSeen.Add(record.ClashGuid);
                    previous = current; latest = current; latestComparison = comparison;
                }
            }

            AddRollingMetricsAndForecast(dailyRows);
            WriteDailyProgress(dailyFile, dailyRows);
            WriteLifecycle(lifecycleFile, lifecycle.Values, latest.Date);
            WriteCurrentClashes(currentFile, latest, latestComparison, lifecycle);
            DashboardSummary summary = WriteDashboardKpi(kpiFile, dailyRows, lifecycle.Values, latest);
            File.Copy(HistoryFile, powerBiHistory, true);
            return summary;
        }

        private static void UpdateLifecycle(Dictionary<string, LifecycleRecord> lifecycle, DateTime date, SnapshotData current, ComparisonResult comparison)
        {
            foreach (ClashRecord record in current.Records)
            {
                LifecycleRecord life;
                if (!lifecycle.TryGetValue(record.ClashGuid, out life))
                {
                    life = new LifecycleRecord
                    {
                        ClashGuid = record.ClashGuid,
                        FirstSeen = date,
                        TestName = record.TestName,
                        Severity = record.Severity,
                        DisciplineA = record.DisciplineA,
                        DisciplineB = record.DisciplineB,
                        DisciplinePair = record.DisciplinePair
                    };
                    lifecycle[record.ClashGuid] = life;
                }

                life.LastSeen = date;
                life.LastStatus = record.Status;
                life.LastDistance = record.Distance;
                life.TestName = record.TestName;
                life.Severity = record.Severity;
                life.DisciplineA = record.DisciplineA;
                life.DisciplineB = record.DisciplineB;
                life.DisciplinePair = record.DisciplinePair;
                life.IsOpen = !IsResolved(record);
                if (IsResolved(record) && !life.LastResolved.HasValue) life.LastResolved = date;
                if (StateOf(comparison, record.ClashGuid) == "Reopened") life.ReopenCount++;
            }

            foreach (ClashRecord resolved in comparison.Resolved)
            {
                LifecycleRecord life;
                if (lifecycle.TryGetValue(resolved.ClashGuid, out life))
                {
                    life.IsOpen = false;
                    life.LastResolved = date;
                    life.ResolutionCount++;
                }
            }
        }

        private static DailyMetric BuildDailyMetric(DateTime date, SnapshotData previous, SnapshotData current, ComparisonResult comparison)
        {
            bool isBaseline = previous.Date == DateTime.MinValue;
            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int reopened = comparison.StateByGuid.Count(x => x.Value == "Reopened");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int resolved = comparison.Resolved.Count;
            int inflow = newCount + reopened;
            int netBurn = resolved - inflow;
            int previousOpen = previous.Records.Count(x => !IsResolved(x));
            int currentOpen = current.Records.Count(x => !IsResolved(x));
            double resolutionRate = previousOpen > 0 ? (resolved * 100.0 / previousOpen) : 0;
            double intervalDays = previous.Date != DateTime.MinValue ? Math.Max(0, (date - previous.Date).TotalDays) : 0;

            return new DailyMetric
            {
                Date = date,
                Previous = previousOpen,
                Current = currentOpen,
                New = newCount,
                Reopened = reopened,
                Existing = existing,
                Resolved = resolved,
                NetChange = isBaseline ? 0 : currentOpen - previousOpen,
                Inflow = isBaseline ? 0 : inflow,
                NetBurn = isBaseline ? 0 : netBurn,
                ResolutionRatePct = isBaseline ? 0 : resolutionRate,
                IsBaseline = isBaseline,
                IntervalDays = isBaseline ? 0 : intervalDays
            };
        }

        private static void AddRollingMetricsAndForecast(List<DailyMetric> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                DateTime from = rows[i].Date.AddDays(-7);
                List<DailyMetric> window = rows
                    .Where(x => x.Date >= from && x.Date <= rows[i].Date && !x.IsBaseline)
                    .ToList();

                double elapsedDays = window.Sum(x => x.IntervalDays);
                if (elapsedDays <= 0)
                {
                    rows[i].Rolling7ResolvedPerDay = 0;
                    rows[i].Rolling7NewPerDay = 0;
                    rows[i].Rolling7NetBurnPerDay = 0;
                    rows[i].ForecastDaysToZero = null;
                    rows[i].ForecastFinishDate = null;
                    continue;
                }

                double resolvedPerDay = window.Sum(x => x.Resolved) / elapsedDays;
                double newPerDay = window.Sum(x => x.Inflow) / elapsedDays;
                double netBurnPerDay = resolvedPerDay - newPerDay;

                rows[i].Rolling7ResolvedPerDay = resolvedPerDay;
                rows[i].Rolling7NewPerDay = newPerDay;
                rows[i].Rolling7NetBurnPerDay = netBurnPerDay;
                rows[i].ForecastDaysToZero = netBurnPerDay > 0 ? (double?)Math.Ceiling(rows[i].Current / netBurnPerDay) : null;
                rows[i].ForecastFinishDate = rows[i].ForecastDaysToZero.HasValue
                    ? (DateTime?)rows[i].Date.AddDays(rows[i].ForecastDaysToZero.Value)
                    : null;
            }
        }

        private static void WriteDailyProgress(string file, List<DailyMetric> rows)
        {
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("SnapshotDateTime,SnapshotDate,IsBaseline,IntervalDays,Previous,Current,New,Reopened,Existing,Resolved,Inflow,NetChange,NetBurn,ResolutionRatePct,Rolling7ResolvedPerDay,Rolling7NewPerDay,Rolling7NetBurnPerDay,ForecastDaysToZero,ForecastFinishDate");
                foreach (DailyMetric r in rows)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.Date.ToString("yyyy-MM-dd HH:mm:ss")), Csv(r.Date.ToString("yyyy-MM-dd")),
                        Csv(r.IsBaseline ? "Yes" : "No"), Number(r.IntervalDays), r.Previous.ToString(), r.Current.ToString(),
                        r.New.ToString(), r.Reopened.ToString(), r.Existing.ToString(), r.Resolved.ToString(), r.Inflow.ToString(),
                        r.NetChange.ToString(), r.NetBurn.ToString(), Number(r.ResolutionRatePct), Number(r.Rolling7ResolvedPerDay),
                        Number(r.Rolling7NewPerDay), Number(r.Rolling7NetBurnPerDay), Number(r.ForecastDaysToZero),
                        Csv(r.ForecastFinishDate.HasValue ? r.ForecastFinishDate.Value.ToString("yyyy-MM-dd") : string.Empty)
                    }));
                }
            }
        }

        private static void WriteLifecycle(string file, IEnumerable<LifecycleRecord> lifecycle, DateTime latestDate)
        {
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("ClashGuid,TestName,Severity,DisciplineA,DisciplineB,DisciplinePair,FirstSeen,LastSeen,LastResolved,IsOpen,AgeDays,AgeBucket,ReopenCount,ResolutionCount,LastNavisworksStatus,LastDistance");
                foreach (LifecycleRecord r in lifecycle.OrderBy(x => x.TestName).ThenBy(x => x.ClashGuid))
                {
                    int age = Math.Max(0, (int)Math.Floor((latestDate - r.FirstSeen).TotalDays));
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.ClashGuid), Csv(r.TestName), Csv(r.Severity), Csv(r.DisciplineA), Csv(r.DisciplineB), Csv(r.DisciplinePair),
                        Csv(r.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss")), Csv(r.LastSeen.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(r.LastResolved.HasValue ? r.LastResolved.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty),
                        Csv(!r.IsOpen ? "Resolved" : string.Equals(r.LastStatus, "approved", StringComparison.OrdinalIgnoreCase) ? "Approved" : "Open"), age.ToString(), Csv(AgeBucket(age)), r.ReopenCount.ToString(),
                        r.ResolutionCount.ToString(), Csv(r.LastStatus), Number(r.LastDistance)
                    }));
                }
            }
        }

        private static void WriteCurrentClashes(string file, SnapshotData latest, ComparisonResult comparison, Dictionary<string, LifecycleRecord> lifecycle)
        {
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(CurrentHeader());
                foreach (ClashRecord r in latest.Records)
                {
                    LifecycleRecord life = lifecycle[r.ClashGuid];
                    int age = Math.Max(0, (int)Math.Floor((latest.Date - life.FirstSeen).TotalDays));
                    string state = StateOf(comparison, r.ClashGuid);
                    writer.WriteLine(ToCurrentCsvLine(r, state, life, age));
                }
            }
        }

        private static DashboardSummary WriteDashboardKpi(string file, List<DailyMetric> dailyRows, IEnumerable<LifecycleRecord> lifecycle, SnapshotData latest)
        {
            DailyMetric latestDay = dailyRows.LastOrDefault() ?? new DailyMetric();
            List<LifecycleRecord> open = lifecycle.Where(x => x.IsOpen && !string.Equals(x.LastStatus, "approved", StringComparison.OrdinalIgnoreCase)).ToList();
            double avgAge = open.Count > 0 ? open.Average(x => Math.Max(0, (latest.Date - x.FirstSeen).TotalDays)) : 0;
            int stale14 = open.Count(x => (latest.Date - x.FirstSeen).TotalDays >= 14);
            int critical = open.Count(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase));

            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("SnapshotDateTime,SnapshotDate,OpenClashes,New,Reopened,Resolved,NetChange,NetBurn,Rolling7ResolvedPerDay,Rolling7NewPerDay,Rolling7NetBurnPerDay,ForecastDaysToZero,ForecastFinishDate,AverageOpenAgeDays,Stale14Plus,CriticalOpen,ClashTests");
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(latest.Date == DateTime.MinValue ? string.Empty : latest.Date.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(latest.Date == DateTime.MinValue ? string.Empty : latest.Date.ToString("yyyy-MM-dd")),
                    latestDay.Current.ToString(), latestDay.New.ToString(), latestDay.Reopened.ToString(), latestDay.Resolved.ToString(),
                    latestDay.NetChange.ToString(), latestDay.NetBurn.ToString(), Number(latestDay.Rolling7ResolvedPerDay),
                    Number(latestDay.Rolling7NewPerDay), Number(latestDay.Rolling7NetBurnPerDay), Number(latestDay.ForecastDaysToZero),
                    Csv(latestDay.ForecastFinishDate.HasValue ? latestDay.ForecastFinishDate.Value.ToString("yyyy-MM-dd") : string.Empty),
                    Number(avgAge), stale14.ToString(), critical.ToString(), latest.Tests.Count.ToString()
                }));
            }

            return new DashboardSummary
            {
                RollingNetBurnPerDay = latestDay.Rolling7NetBurnPerDay,
                ForecastFinishDate = latestDay.ForecastFinishDate,
                AverageOpenAge = avgAge
            };
        }

        private static void WriteHistoryRows(StreamWriter writer, DateTime date, List<ClashRecord> current,
            ComparisonResult comparison, Dictionary<string, LifecycleRecord> lifecycle)
        {
            foreach (ClashRecord r in current)
            {
                string state = StateOf(comparison, r.ClashGuid);
                if (string.IsNullOrWhiteSpace(state)) state = "Existing";
                LifecycleRecord life = lifecycle[r.ClashGuid];
                int age = Math.Max(0, (int)Math.Floor((date - life.FirstSeen).TotalDays));
                writer.WriteLine(ToHistoryCsvLine(r, state, life.FirstSeen, age, life.ReopenCount));
            }

            var presentGuids = new HashSet<string>(current.Select(x => x.ClashGuid), StringComparer.OrdinalIgnoreCase);
            foreach (ClashRecord r in comparison.Resolved.Where(x => !presentGuids.Contains(x.ClashGuid)))
            {
                LifecycleRecord life;
                DateTime firstSeen = date;
                int reopenCount = 0;
                if (lifecycle.TryGetValue(r.ClashGuid, out life)) { firstSeen = life.FirstSeen; reopenCount = life.ReopenCount; }
                int age = Math.Max(0, (int)Math.Floor((date - firstSeen).TotalDays));
                writer.WriteLine(ToHistoryCsvLine(r.CloneForDate(date), "Resolved", firstSeen, age, reopenCount));
            }
        }

        private static void WriteTestPerformanceRows(StreamWriter writer, DateTime date, SnapshotData previous,
            SnapshotData current, ComparisonResult comparison)
        {
            var allTests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase); allTests.UnionWith(current.Tests);
            var prevByTest = previous.Records.Where(x => !IsResolved(x)).GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var currByTest = current.Records.Where(x => !IsResolved(x)).GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var resolvedByTest = comparison.Resolved.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            bool isBaseline = previous.Date == DateTime.MinValue;

            foreach (string test in allTests.OrderBy(x => x))
            {
                List<ClashRecord> currentRows; if (!currByTest.TryGetValue(test, out currentRows)) currentRows = new List<ClashRecord>();
                int previousCount = prevByTest.ContainsKey(test) ? prevByTest[test] : 0;
                int currentCount = currentRows.Count;
                int newCount = currentRows.Count(x => StateOf(comparison, x.ClashGuid) == "New");
                int reopened = currentRows.Count(x => StateOf(comparison, x.ClashGuid) == "Reopened");
                int existing = currentRows.Count(x => StateOf(comparison, x.ClashGuid) == "Existing");
                int resolved = resolvedByTest.ContainsKey(test) ? resolvedByTest[test] : 0;
                double rate = previousCount > 0 ? resolved * 100.0 / previousCount : 0;
                TestMetadata meta = ParseTestMetadata(test);

                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(date.ToString("yyyy-MM-dd HH:mm:ss")), Csv(date.ToString("yyyy-MM-dd")),
                    Csv(test), Csv(meta.Severity), Csv(meta.DisciplineA), Csv(meta.DisciplineB), Csv(meta.DisciplinePair),
                    previousCount.ToString(), currentCount.ToString(), newCount.ToString(), reopened.ToString(), existing.ToString(),
                    resolved.ToString(), (isBaseline ? 0 : currentCount - previousCount).ToString(), Number(isBaseline ? 0 : rate)
                }));
            }
        }

        private static string AgeBucket(int age)
        {
            if (age <= 3) return "0-3 Days";
            if (age <= 7) return "4-7 Days";
            if (age <= 14) return "8-14 Days";
            if (age <= 30) return "15-30 Days";
            return "30+ Days";
        }

        private static string StateOf(ComparisonResult comparison, string guid)
        {
            string state; return comparison.StateByGuid.TryGetValue(guid, out state) ? state : string.Empty;
        }

        private static void WriteClashCsv(string file, List<ClashRecord> records, Dictionary<string, string> states)
        {
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(SnapshotHeader());
                foreach (ClashRecord r in records)
                {
                    string state = string.Empty; if (states != null) states.TryGetValue(r.ClashGuid, out state);
                    writer.WriteLine(ToSnapshotCsvLine(r, state));
                }
            }
        }

        private static string SnapshotHeader()
        {
            return "SnapshotDateTime,SnapshotDate,TestName,ClashGuid,TrackerState,NavisworksStatus,Distance,DateFound,GridLocation,X,Y,Z,ItemAElementId,ItemAName,ItemALayer,ItemAPath,ItemBElementId,ItemBName,ItemBLayer,ItemBPath,Description,Comments,Severity,DisciplineA,DisciplineB,DisciplinePair";
        }

        private static string HistoryHeader()
        {
            return SnapshotHeader() + ",FirstSeen,AgeDays,AgeBucket,ReopenCount";
        }

        private static string CurrentHeader()
        {
            return SnapshotHeader() + ",FirstSeen,AgeDays,AgeBucket,ReopenCount,IsStale14Plus";
        }

        private static List<ClashRecord> ReadSnapshotCsv(string file)
        {
            var records = new List<ClashRecord>(); bool first = true;
            foreach (string line in File.ReadLines(file))
            {
                if (first) { first = false; continue; }
                List<string> v = ParseCsvLine(line);
                if (v.Count < 21) continue;

                bool newFormat = v.Count >= 26;
                int o = newFormat ? 1 : 0;
                string testName = v[1 + o];
                TestMetadata meta = ParseTestMetadata(testName);

                records.Add(new ClashRecord
                {
                    SnapshotDate = ParseDate(v[0]) ?? DateTime.MinValue,
                    TestName = testName,
                    ClashGuid = v[2 + o],
                    Status = v[4 + o],
                    Distance = ParseNullableDouble(v[5 + o]),
                    DateFound = ParseDate(v[6 + o]),
                    GridLocation = v[7 + o],
                    X = ParseNullableDouble(v[8 + o]),
                    Y = ParseNullableDouble(v[9 + o]),
                    Z = ParseNullableDouble(v[10 + o]),
                    ItemAElementId = v[11 + o], ItemAName = v[12 + o], ItemALayer = v[13 + o], ItemAPath = v[14 + o],
                    ItemBElementId = v[15 + o], ItemBName = v[16 + o], ItemBLayer = v[17 + o], ItemBPath = v[18 + o],
                    Description = v[19 + o], Comments = v[20 + o],
                    Severity = v.Count > 21 + o && !string.IsNullOrWhiteSpace(v[21 + o]) ? v[21 + o] : meta.Severity,
                    DisciplineA = v.Count > 22 + o && !string.IsNullOrWhiteSpace(v[22 + o]) ? v[22 + o] : meta.DisciplineA,
                    DisciplineB = v.Count > 23 + o && !string.IsNullOrWhiteSpace(v[23 + o]) ? v[23 + o] : meta.DisciplineB,
                    DisciplinePair = v.Count > 24 + o && !string.IsNullOrWhiteSpace(v[24 + o]) ? v[24 + o] : meta.DisciplinePair
                });
            }
            return records;
        }

        private static string ToSnapshotCsvLine(ClashRecord r, string trackerState)
        {
            return string.Join(",", new[]
            {
                Csv(r.SnapshotDate.ToString("yyyy-MM-dd HH:mm:ss")), Csv(r.SnapshotDate.ToString("yyyy-MM-dd")),
                Csv(r.TestName), Csv(r.ClashGuid), Csv(trackerState), Csv(r.Status), Number(r.Distance),
                Csv(r.DateFound.HasValue ? r.DateFound.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty), Csv(r.GridLocation),
                Number(r.X), Number(r.Y), Number(r.Z), Csv(r.ItemAElementId), Csv(r.ItemAName), Csv(r.ItemALayer), Csv(r.ItemAPath),
                Csv(r.ItemBElementId), Csv(r.ItemBName), Csv(r.ItemBLayer), Csv(r.ItemBPath), Csv(r.Description), Csv(r.Comments),
                Csv(r.Severity), Csv(r.DisciplineA), Csv(r.DisciplineB), Csv(r.DisciplinePair)
            });
        }

        private static string ToHistoryCsvLine(ClashRecord r, string trackerState, DateTime firstSeen, int age, int reopenCount)
        {
            return ToSnapshotCsvLine(r, trackerState) + "," + string.Join(",", new[]
            {
                Csv(firstSeen.ToString("yyyy-MM-dd HH:mm:ss")), age.ToString(), Csv(AgeBucket(age)), reopenCount.ToString()
            });
        }

        private static string ToCurrentCsvLine(ClashRecord r, string trackerState, LifecycleRecord life, int age)
        {
            return ToSnapshotCsvLine(r, trackerState) + "," + string.Join(",", new[]
            {
                Csv(life.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss")), age.ToString(), Csv(AgeBucket(age)),
                life.ReopenCount.ToString(), Csv(age >= 14 ? "Yes" : "No")
            });
        }

        private static void PrintSummary(DateTime date, SnapshotData current, SnapshotData previous,
            ComparisonResult comparison, DashboardSummary dashboard)
        {
            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int reopened = comparison.StateByGuid.Count(x => x.Value == "Reopened");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int resolved = comparison.Resolved.Count;

            Console.WriteLine(); Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("SNAPSHOT SUMMARY"); Console.WriteLine("================"); Console.ResetColor();
            Console.WriteLine("Date/Time   : " + date.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("Clash Tests : " + current.Tests.Count);
            Console.WriteLine("Previous unresolved: " + previous.Records.Count(x => !IsResolved(x)));
            Console.WriteLine("Unresolved  : " + current.Records.Count(x => !IsResolved(x)));
            Console.WriteLine("Actionable  : " + current.Records.Count(IsActionable));
            Console.WriteLine("Approved    : " + current.Records.Count(IsApproved));
            Console.WriteLine("Resolved still present: " + current.Records.Count(IsResolved));
            Console.WriteLine("New         : " + newCount);
            Console.WriteLine("Reopened    : " + reopened);
            Console.WriteLine("Existing    : " + existing);
            Console.WriteLine("Resolved    : " + resolved);
            Console.WriteLine("Net Change  : " + (current.Records.Count(x => !IsResolved(x)) - previous.Records.Count(x => !IsResolved(x))));
            Console.WriteLine("7D Net Burn : " + dashboard.RollingNetBurnPerDay.ToString("0.0", CultureInfo.InvariantCulture) + " clashes/day");
            Console.WriteLine("Avg Age     : " + dashboard.AverageOpenAge.ToString("0.0", CultureInfo.InvariantCulture) + " days");
            Console.WriteLine("Forecast    : " + (dashboard.ForecastFinishDate.HasValue ? dashboard.ForecastFinishDate.Value.ToString("yyyy-MM-dd") : "No valid forecast yet"));
        }

        private static DateTime? ParseNavisworksDate(XElement parent, string elementName)
        {
            XElement container = parent.Descendants().FirstOrDefault(x => LocalName(x) == elementName);
            if (container == null) return null;
            XElement date = container.Descendants().FirstOrDefault(x => LocalName(x) == "date");
            if (date == null) return ParseDate(container.Value);
            int year, month, day, hour, minute, second;
            if (!int.TryParse(Attr(date, "year"), out year) || !int.TryParse(Attr(date, "month"), out month) || !int.TryParse(Attr(date, "day"), out day)) return null;
            int.TryParse(Attr(date, "hour"), out hour); int.TryParse(Attr(date, "minute"), out minute); int.TryParse(Attr(date, "second"), out second);
            try { return new DateTime(year, month, day, hour, minute, second); } catch { return null; }
        }

        private static string Attr(XElement element, string name)
        {
            XAttribute attr = element.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attr == null ? string.Empty : attr.Value;
        }

        private static string ElementValue(XElement parent, string name)
        {
            XElement element = parent.Descendants().FirstOrDefault(x => string.Equals(LocalName(x), name, StringComparison.OrdinalIgnoreCase));
            return element == null ? string.Empty : (element.Value ?? string.Empty).Trim();
        }

        private static string LocalName(XElement element) { return element.Name.LocalName; }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            DateTime date;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return date;
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return date;
            return null;
        }

        private static double? ParseDouble(string value)
        {
            double number;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out number)) return number;
            return null;
        }

        private static double? ParseNullableDouble(string value) { return ParseDouble(value); }
        private static string Number(double? value) { return value.HasValue ? value.Value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty; }
        private static string Number(double value) { return value.ToString("0.############", CultureInfo.InvariantCulture); }
        private static string Csv(string value) { value = value ?? string.Empty; return "\"" + value.Replace("\"", "\"\"") + "\""; }
        private static string FirstNonEmpty(params string[] values) { return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty; }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>(); var sb = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { values.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            values.Add(sb.ToString()); return values;
        }

        private static void Pause() { Console.WriteLine(); Console.WriteLine("Press any key to exit..."); Console.ReadKey(); }

        private class SnapshotRef
        {
            public DateTime Timestamp { get; set; }
            public string FolderPath { get; set; }
        }

        private class SnapshotChoice
        {
            public DateTime Timestamp { get; set; }
            public string FolderPath { get; set; }
            public bool IsOverwrite { get; set; }
            public bool Cancelled { get; set; }
        }

        private class SnapshotData
        {
            public DateTime Date { get; set; }
            public List<ClashRecord> Records { get; set; }
            public List<string> Tests { get; set; }
            public static SnapshotData Empty() { return new SnapshotData { Date = DateTime.MinValue, Records = new List<ClashRecord>(), Tests = new List<string>() }; }
        }

        private class ComparisonResult
        {
            public Dictionary<string, string> StateByGuid { get; private set; }
            public List<ClashRecord> Resolved { get; private set; }
            public ComparisonResult() { StateByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); Resolved = new List<ClashRecord>(); }
        }

        private class TestMetadata
        {
            public string Severity { get; set; }
            public string DisciplineA { get; set; }
            public string DisciplineB { get; set; }
            public string DisciplinePair { get; set; }
        }

        private class DailyMetric
        {
            public DateTime Date { get; set; }
            public int Previous { get; set; }
            public int Current { get; set; }
            public int New { get; set; }
            public int Reopened { get; set; }
            public int Existing { get; set; }
            public int Resolved { get; set; }
            public int Inflow { get; set; }
            public int NetChange { get; set; }
            public int NetBurn { get; set; }
            public double ResolutionRatePct { get; set; }
            public bool IsBaseline { get; set; }
            public double IntervalDays { get; set; }
            public double Rolling7ResolvedPerDay { get; set; }
            public double Rolling7NewPerDay { get; set; }
            public double Rolling7NetBurnPerDay { get; set; }
            public double? ForecastDaysToZero { get; set; }
            public DateTime? ForecastFinishDate { get; set; }
        }

        private class LifecycleRecord
        {
            public string ClashGuid { get; set; }
            public string TestName { get; set; }
            public string Severity { get; set; }
            public string DisciplineA { get; set; }
            public string DisciplineB { get; set; }
            public string DisciplinePair { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public DateTime? LastResolved { get; set; }
            public bool IsOpen { get; set; }
            public int ReopenCount { get; set; }
            public int ResolutionCount { get; set; }
            public string LastStatus { get; set; }
            public double? LastDistance { get; set; }
        }

        private class DashboardSummary
        {
            public double RollingNetBurnPerDay { get; set; }
            public DateTime? ForecastFinishDate { get; set; }
            public double AverageOpenAge { get; set; }
        }

        private class ClashRecord
        {
            public DateTime SnapshotDate { get; set; }
            public string TestName { get; set; }
            public string ClashGuid { get; set; }
            public string ClashName { get; set; }
            public string Status { get; set; }
            public double? Distance { get; set; }
            public DateTime? DateFound { get; set; }
            public string GridLocation { get; set; }
            public double? X { get; set; }
            public double? Y { get; set; }
            public double? Z { get; set; }
            public string ItemAElementId { get; set; }
            public string ItemAName { get; set; }
            public string ItemALayer { get; set; }
            public string ItemAPath { get; set; }
            public string ItemBElementId { get; set; }
            public string ItemBName { get; set; }
            public string ItemBLayer { get; set; }
            public string ItemBPath { get; set; }
            public string Description { get; set; }
            public string Comments { get; set; }
            public string Severity { get; set; }
            public string DisciplineA { get; set; }
            public string DisciplineB { get; set; }
            public string DisciplinePair { get; set; }

            public ClashRecord CloneForDate(DateTime date)
            {
                return new ClashRecord
                {
                    SnapshotDate = date, TestName = TestName, ClashGuid = ClashGuid, ClashName = ClashName, Status = Status,
                    Distance = Distance, DateFound = DateFound, GridLocation = GridLocation, X = X, Y = Y, Z = Z,
                    ItemAElementId = ItemAElementId, ItemAName = ItemAName, ItemALayer = ItemALayer, ItemAPath = ItemAPath,
                    ItemBElementId = ItemBElementId, ItemBName = ItemBName, ItemBLayer = ItemBLayer, ItemBPath = ItemBPath,
                    Description = Description, Comments = Comments, Severity = Severity, DisciplineA = DisciplineA,
                    DisciplineB = DisciplineB, DisciplinePair = DisciplinePair
                };
            }
        }
    }
}
