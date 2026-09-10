using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace HUMAIN.CoordinationTracker
{
    internal class Program
    {
        private static readonly string AppRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUMAIN.CoordinationTracker");

        private static readonly string SnapshotsRoot = Path.Combine(AppRoot, "Snapshots");
        private static readonly string PowerBiRoot = Path.Combine(AppRoot, "PowerBI");
        private static readonly string HistoryFile = Path.Combine(AppRoot, "clash_history.csv");

        static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                EnsureFolders();

                Console.WriteLine("HUMAIN Coordination Tracker");
                Console.WriteLine("===========================");
                Console.WriteLine();
                Console.WriteLine("Navisworks XML Historical Clash Tracker");
                Console.WriteLine();

                string inputFolder = AskForInputFolder();
                DateTime snapshotDate = AskForSnapshotDate();
                SnapshotData imported = ImportSnapshot(inputFolder, snapshotDate);

                if (imported.Records.Count == 0 && imported.Tests.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No clash tests or clash results were found in the selected XML files.");
                    Console.ResetColor();
                    Pause();
                    return;
                }

                SnapshotData previous = LoadPreviousSnapshot(snapshotDate);
                HashSet<string> everSeenBefore = LoadEverSeenBefore(snapshotDate);
                ComparisonResult comparison = CompareSnapshots(previous.Records, imported.Records, everSeenBefore);

                SaveSnapshot(snapshotDate, imported);
                DashboardSummary dashboard = RebuildDerivedFiles();
                PrintSummary(snapshotDate, imported, previous, comparison, dashboard);

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
                Console.WriteLine("Invalid date. Example: 2026-09-09");
                Console.ResetColor();
            }
        }

        private static SnapshotData ImportSnapshot(string folder, DateTime snapshotDate)
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
                    string testName = GetTestName(doc, file);
                    tests.Add(testName);

                    foreach (XElement clash in doc.Descendants().Where(x => LocalName(x) == "clashresult"))
                    {
                        string guid = Attr(clash, "guid");
                        if (string.IsNullOrWhiteSpace(guid)) continue;

                        TestMetadata metadata = ParseTestMetadata(testName);
                        var record = new ClashRecord
                        {
                            SnapshotDate = snapshotDate,
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
                Console.WriteLine("Warning: " + failedFiles + " XML file(s) could not be parsed.");
                Console.ResetColor();
            }

            return new SnapshotData
            {
                Date = snapshotDate,
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

        private static SnapshotData LoadPreviousSnapshot(DateTime currentDate)
        {
            var dates = GetSnapshotDates().Where(x => x < currentDate).OrderByDescending(x => x).ToList();
            return dates.Count == 0 ? SnapshotData.Empty() : LoadSnapshot(dates[0]);
        }

        private static HashSet<string> LoadEverSeenBefore(DateTime currentDate)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DateTime date in GetSnapshotDates().Where(x => x < currentDate).OrderBy(x => x))
                foreach (ClashRecord record in LoadSnapshot(date).Records) set.Add(record.ClashGuid);
            return set;
        }

        private static ComparisonResult CompareSnapshots(List<ClashRecord> previous, List<ClashRecord> current, HashSet<string> everSeenBefore)
        {
            var previousMap = previous.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var currentMap = current.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var result = new ComparisonResult();

            foreach (ClashRecord c in current)
            {
                if (previousMap.ContainsKey(c.ClashGuid)) result.StateByGuid[c.ClashGuid] = "Existing";
                else if (everSeenBefore.Contains(c.ClashGuid)) result.StateByGuid[c.ClashGuid] = "Reopened";
                else result.StateByGuid[c.ClashGuid] = "New";
            }

            foreach (ClashRecord p in previous)
                if (!currentMap.ContainsKey(p.ClashGuid)) result.Resolved.Add(p);
            return result;
        }

        private static void SaveSnapshot(DateTime date, SnapshotData snapshot)
        {
            string folder = Path.Combine(SnapshotsRoot, date.ToString("yyyy-MM-dd"));
            bool replacing = Directory.Exists(folder);
            Directory.CreateDirectory(folder);
            WriteClashCsv(Path.Combine(folder, "snapshot.csv"), snapshot.Records, null);

            using (var writer = new StreamWriter(Path.Combine(folder, "tests.csv"), false, new UTF8Encoding(true)))
            {
                writer.WriteLine("TestName");
                foreach (string test in snapshot.Tests.OrderBy(x => x)) writer.WriteLine(Csv(test));
            }

            if (replacing)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Existing snapshot for " + date.ToString("yyyy-MM-dd") + " was replaced.");
                Console.ResetColor();
            }
        }

        private static List<DateTime> GetSnapshotDates()
        {
            if (!Directory.Exists(SnapshotsRoot)) return new List<DateTime>();
            return Directory.GetDirectories(SnapshotsRoot).Select(Path.GetFileName).Select(x =>
            {
                DateTime date;
                bool ok = DateTime.TryParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                return new { Date = date, Valid = ok };
            }).Where(x => x.Valid).Select(x => x.Date).OrderBy(x => x).ToList();
        }

        private static SnapshotData LoadSnapshot(DateTime date)
        {
            string folder = Path.Combine(SnapshotsRoot, date.ToString("yyyy-MM-dd"));
            string snapshotFile = Path.Combine(folder, "snapshot.csv");
            string testsFile = Path.Combine(folder, "tests.csv");
            var data = new SnapshotData
            {
                Date = date,
                Records = File.Exists(snapshotFile) ? ReadSnapshotCsv(snapshotFile) : new List<ClashRecord>(),
                Tests = new List<string>()
            };

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

            var dates = GetSnapshotDates();
            var dailyRows = new List<DailyMetric>();
            var lifecycle = new Dictionary<string, LifecycleRecord>(StringComparer.OrdinalIgnoreCase);
            SnapshotData previous = SnapshotData.Empty();
            var everSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SnapshotData latest = SnapshotData.Empty();
            ComparisonResult latestComparison = new ComparisonResult();

            using (var historyWriter = new StreamWriter(HistoryFile, false, new UTF8Encoding(true)))
            using (var testWriter = new StreamWriter(testFile, false, new UTF8Encoding(true)))
            {
                historyWriter.WriteLine(HistoryHeader());
                testWriter.WriteLine("SnapshotDate,TestName,Severity,DisciplineA,DisciplineB,DisciplinePair,Previous,Current,New,Reopened,Existing,Resolved,NetChange,ResolutionRatePct");

                foreach (DateTime date in dates)
                {
                    SnapshotData current = LoadSnapshot(date);
                    ComparisonResult comparison = CompareSnapshots(previous.Records, current.Records, everSeen);
                    UpdateLifecycle(lifecycle, date, current, comparison);
                    WriteHistoryRows(historyWriter, date, current.Records, comparison, lifecycle);
                    dailyRows.Add(BuildDailyMetric(date, previous, current, comparison));
                    WriteTestPerformanceRows(testWriter, date, previous, current, comparison);

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
                life.IsOpen = true;
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
            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int reopened = comparison.StateByGuid.Count(x => x.Value == "Reopened");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int resolved = comparison.Resolved.Count;
            int inflow = newCount + reopened;
            int netBurn = resolved - inflow;
            double resolutionRate = previous.Records.Count > 0 ? (resolved * 100.0 / previous.Records.Count) : 0;

            return new DailyMetric
            {
                Date = date,
                Previous = previous.Records.Count,
                Current = current.Records.Count,
                New = newCount,
                Reopened = reopened,
                Existing = existing,
                Resolved = resolved,
                NetChange = current.Records.Count - previous.Records.Count,
                Inflow = inflow,
                NetBurn = netBurn,
                ResolutionRatePct = resolutionRate
            };
        }

        private static void AddRollingMetricsAndForecast(List<DailyMetric> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                DateTime from = rows[i].Date.AddDays(-6);
                List<DailyMetric> window = rows.Where(x => x.Date >= from && x.Date <= rows[i].Date).ToList();
                int spanDays = Math.Max(1, (rows[i].Date - window.Min(x => x.Date)).Days + 1);
                double resolvedPerDay = window.Sum(x => x.Resolved) / (double)spanDays;
                double newPerDay = window.Sum(x => x.Inflow) / (double)spanDays;
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
                writer.WriteLine("SnapshotDate,Previous,Current,New,Reopened,Existing,Resolved,Inflow,NetChange,NetBurn,ResolutionRatePct,Rolling7ResolvedPerDay,Rolling7NewPerDay,Rolling7NetBurnPerDay,ForecastDaysToZero,ForecastFinishDate");
                foreach (DailyMetric r in rows)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.Date.ToString("yyyy-MM-dd")), r.Previous.ToString(), r.Current.ToString(), r.New.ToString(),
                        r.Reopened.ToString(), r.Existing.ToString(), r.Resolved.ToString(), r.Inflow.ToString(),
                        r.NetChange.ToString(), r.NetBurn.ToString(), Number(r.ResolutionRatePct),
                        Number(r.Rolling7ResolvedPerDay), Number(r.Rolling7NewPerDay), Number(r.Rolling7NetBurnPerDay),
                        Number(r.ForecastDaysToZero), Csv(r.ForecastFinishDate.HasValue ? r.ForecastFinishDate.Value.ToString("yyyy-MM-dd") : string.Empty)
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
                    int age = Math.Max(0, (latestDate - r.FirstSeen).Days);
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.ClashGuid), Csv(r.TestName), Csv(r.Severity), Csv(r.DisciplineA), Csv(r.DisciplineB), Csv(r.DisciplinePair),
                        Csv(r.FirstSeen.ToString("yyyy-MM-dd")), Csv(r.LastSeen.ToString("yyyy-MM-dd")),
                        Csv(r.LastResolved.HasValue ? r.LastResolved.Value.ToString("yyyy-MM-dd") : string.Empty),
                        Csv(r.IsOpen ? "Open" : "Resolved"), age.ToString(), Csv(AgeBucket(age)), r.ReopenCount.ToString(),
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
                    int age = Math.Max(0, (latest.Date - life.FirstSeen).Days);
                    string state = StateOf(comparison, r.ClashGuid);
                    writer.WriteLine(ToCurrentCsvLine(r, state, life, age));
                }
            }
        }

        private static DashboardSummary WriteDashboardKpi(string file, List<DailyMetric> dailyRows, IEnumerable<LifecycleRecord> lifecycle, SnapshotData latest)
        {
            DailyMetric latestDay = dailyRows.LastOrDefault() ?? new DailyMetric();
            List<LifecycleRecord> open = lifecycle.Where(x => x.IsOpen).ToList();
            double avgAge = open.Count > 0 ? open.Average(x => Math.Max(0, (latest.Date - x.FirstSeen).Days)) : 0;
            int stale14 = open.Count(x => (latest.Date - x.FirstSeen).Days >= 14);
            int critical = open.Count(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase));

            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("SnapshotDate,OpenClashes,New,Reopened,Resolved,NetChange,NetBurn,Rolling7ResolvedPerDay,Rolling7NewPerDay,Rolling7NetBurnPerDay,ForecastDaysToZero,ForecastFinishDate,AverageOpenAgeDays,Stale14Plus,CriticalOpen,ClashTests");
                writer.WriteLine(string.Join(",", new[]
                {
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
                int age = Math.Max(0, (date - life.FirstSeen).Days);
                writer.WriteLine(ToHistoryCsvLine(r, state, life.FirstSeen, age, life.ReopenCount));
            }

            foreach (ClashRecord r in comparison.Resolved)
            {
                LifecycleRecord life;
                DateTime firstSeen = date;
                int reopenCount = 0;
                if (lifecycle.TryGetValue(r.ClashGuid, out life)) { firstSeen = life.FirstSeen; reopenCount = life.ReopenCount; }
                int age = Math.Max(0, (date - firstSeen).Days);
                writer.WriteLine(ToHistoryCsvLine(r.CloneForDate(date), "Resolved", firstSeen, age, reopenCount));
            }
        }

        private static void WriteTestPerformanceRows(StreamWriter writer, DateTime date, SnapshotData previous,
            SnapshotData current, ComparisonResult comparison)
        {
            var allTests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase); allTests.UnionWith(current.Tests);
            var prevByTest = previous.Records.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var currByTest = current.Records.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var resolvedByTest = comparison.Resolved.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

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
                    Csv(date.ToString("yyyy-MM-dd")), Csv(test), Csv(meta.Severity), Csv(meta.DisciplineA), Csv(meta.DisciplineB), Csv(meta.DisciplinePair),
                    previousCount.ToString(), currentCount.ToString(), newCount.ToString(), reopened.ToString(), existing.ToString(),
                    resolved.ToString(), (currentCount - previousCount).ToString(), Number(rate)
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
            return "SnapshotDate,TestName,ClashGuid,TrackerState,NavisworksStatus,Distance,DateFound,GridLocation,X,Y,Z,ItemAElementId,ItemAName,ItemALayer,ItemAPath,ItemBElementId,ItemBName,ItemBLayer,ItemBPath,Description,Comments,Severity,DisciplineA,DisciplineB,DisciplinePair";
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
                List<string> v = ParseCsvLine(line); if (v.Count < 21) continue;
                string testName = v[1]; TestMetadata meta = ParseTestMetadata(testName);
                records.Add(new ClashRecord
                {
                    SnapshotDate = ParseDate(v[0]) ?? DateTime.MinValue, TestName = testName, ClashGuid = v[2], Status = v[4],
                    Distance = ParseNullableDouble(v[5]), DateFound = ParseDate(v[6]), GridLocation = v[7],
                    X = ParseNullableDouble(v[8]), Y = ParseNullableDouble(v[9]), Z = ParseNullableDouble(v[10]),
                    ItemAElementId = v[11], ItemAName = v[12], ItemALayer = v[13], ItemAPath = v[14],
                    ItemBElementId = v[15], ItemBName = v[16], ItemBLayer = v[17], ItemBPath = v[18], Description = v[19], Comments = v[20],
                    Severity = v.Count > 21 && !string.IsNullOrWhiteSpace(v[21]) ? v[21] : meta.Severity,
                    DisciplineA = v.Count > 22 && !string.IsNullOrWhiteSpace(v[22]) ? v[22] : meta.DisciplineA,
                    DisciplineB = v.Count > 23 && !string.IsNullOrWhiteSpace(v[23]) ? v[23] : meta.DisciplineB,
                    DisciplinePair = v.Count > 24 && !string.IsNullOrWhiteSpace(v[24]) ? v[24] : meta.DisciplinePair
                });
            }
            return records;
        }

        private static string ToSnapshotCsvLine(ClashRecord r, string trackerState)
        {
            return string.Join(",", new[]
            {
                Csv(r.SnapshotDate.ToString("yyyy-MM-dd")), Csv(r.TestName), Csv(r.ClashGuid), Csv(trackerState), Csv(r.Status), Number(r.Distance),
                Csv(r.DateFound.HasValue ? r.DateFound.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty), Csv(r.GridLocation), Number(r.X), Number(r.Y), Number(r.Z),
                Csv(r.ItemAElementId), Csv(r.ItemAName), Csv(r.ItemALayer), Csv(r.ItemAPath), Csv(r.ItemBElementId), Csv(r.ItemBName), Csv(r.ItemBLayer), Csv(r.ItemBPath),
                Csv(r.Description), Csv(r.Comments), Csv(r.Severity), Csv(r.DisciplineA), Csv(r.DisciplineB), Csv(r.DisciplinePair)
            });
        }

        private static string ToHistoryCsvLine(ClashRecord r, string trackerState, DateTime firstSeen, int age, int reopenCount)
        {
            return ToSnapshotCsvLine(r, trackerState) + "," + string.Join(",", new[]
            {
                Csv(firstSeen.ToString("yyyy-MM-dd")), age.ToString(), Csv(AgeBucket(age)), reopenCount.ToString()
            });
        }

        private static string ToCurrentCsvLine(ClashRecord r, string trackerState, LifecycleRecord life, int age)
        {
            return ToSnapshotCsvLine(r, trackerState) + "," + string.Join(",", new[]
            {
                Csv(life.FirstSeen.ToString("yyyy-MM-dd")), age.ToString(), Csv(AgeBucket(age)), life.ReopenCount.ToString(), Csv(age >= 14 ? "Yes" : "No")
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
            Console.WriteLine("Date        : " + date.ToString("yyyy-MM-dd"));
            Console.WriteLine("Clash Tests : " + current.Tests.Count);
            Console.WriteLine("Previous    : " + previous.Records.Count);
            Console.WriteLine("Current     : " + current.Records.Count);
            Console.WriteLine("New         : " + newCount);
            Console.WriteLine("Reopened    : " + reopened);
            Console.WriteLine("Existing    : " + existing);
            Console.WriteLine("Resolved    : " + resolved);
            Console.WriteLine("Net Change  : " + (current.Records.Count - previous.Records.Count));
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

        private class SnapshotData
        {
            public DateTime Date { get; set; }
            public List<ClashRecord> Records { get; set; }
            public List<string> Tests { get; set; }
            public static SnapshotData Empty() { return new SnapshotData { Records = new List<ClashRecord>(), Tests = new List<string>() }; }
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
