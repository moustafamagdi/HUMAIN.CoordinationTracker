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
                RebuildDerivedFiles();

                PrintSummary(snapshotDate, imported, previous, comparison);

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
                    if (Directory.Exists(value))
                        return value;
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

                if (string.IsNullOrWhiteSpace(value))
                    return DateTime.Today;

                DateTime date;
                if (DateTime.TryParseExact(
                    value.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
                {
                    return date.Date;
                }

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
                        if (string.IsNullOrWhiteSpace(guid))
                            continue;

                        var record = new ClashRecord
                        {
                            SnapshotDate = snapshotDate,
                            TestName = testName,
                            ClashGuid = guid,
                            ClashName = Attr(clash, "name"),
                            Status = Attr(clash, "status"),
                            Distance = ParseDouble(Attr(clash, "distance")),
                            DateFound = ParseDate(Attr(clash, "date")),
                            GridLocation = ElementValue(clash, "gridlocation"),
                            Description = ElementValue(clash, "description"),
                            Comments = ElementValue(clash, "comments")
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
                        if (clashObjects.Count > 0)
                            FillObject(record, clashObjects[0], true);
                        if (clashObjects.Count > 1)
                            FillObject(record, clashObjects[1], false);

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
                Records = results
                    .GroupBy(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList(),
                Tests = tests.OrderBy(x => x).ToList()
            };
        }

        private static string GetTestName(XDocument doc, string file)
        {
            XElement clashTest = doc.Descendants().FirstOrDefault(x => LocalName(x) == "clashtest");
            string name = clashTest == null ? null : Attr(clashTest, "name");

            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            XElement batchTest = doc.Descendants().FirstOrDefault(x => LocalName(x) == "batchtest");
            name = batchTest == null ? null : Attr(batchTest, "name");

            return string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(file)
                : name.Trim();
        }

        private static void FillObject(ClashRecord record, XElement clashObject, bool first)
        {
            string objectName = FirstNonEmpty(
                ElementValue(clashObject, "objectname"),
                FindSmartTag(clashObject, "Item Name"));

            string itemId = FindSmartTag(clashObject, "Item ID");
            string layer = FindSmartTag(clashObject, "Layer");
            string itemPath = FindSmartTag(clashObject, "Item Path");

            if (first)
            {
                record.ItemAName = objectName;
                record.ItemAElementId = itemId;
                record.ItemALayer = layer;
                record.ItemAPath = itemPath;
            }
            else
            {
                record.ItemBName = objectName;
                record.ItemBElementId = itemId;
                record.ItemBLayer = layer;
                record.ItemBPath = itemPath;
            }
        }

        private static string FindSmartTag(XElement clashObject, string tagName)
        {
            foreach (XElement smartTag in clashObject.Descendants().Where(x => LocalName(x) == "smarttag"))
            {
                string name = ElementValue(smartTag, "name");
                if (string.Equals(name, tagName, StringComparison.OrdinalIgnoreCase))
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
            {
                foreach (ClashRecord record in LoadSnapshot(date).Records)
                    set.Add(record.ClashGuid);
            }
            return set;
        }

        private static ComparisonResult CompareSnapshots(
            List<ClashRecord> previous,
            List<ClashRecord> current,
            HashSet<string> everSeenBefore)
        {
            var previousMap = previous.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var currentMap = current.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var result = new ComparisonResult();

            foreach (ClashRecord c in current)
            {
                if (previousMap.ContainsKey(c.ClashGuid))
                    result.StateByGuid[c.ClashGuid] = "Existing";
                else if (everSeenBefore.Contains(c.ClashGuid))
                    result.StateByGuid[c.ClashGuid] = "Reopened";
                else
                    result.StateByGuid[c.ClashGuid] = "New";
            }

            foreach (ClashRecord p in previous)
            {
                if (!currentMap.ContainsKey(p.ClashGuid))
                    result.Resolved.Add(p);
            }

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
                foreach (string test in snapshot.Tests.OrderBy(x => x))
                    writer.WriteLine(Csv(test));
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
            if (!Directory.Exists(SnapshotsRoot))
                return new List<DateTime>();

            return Directory.GetDirectories(SnapshotsRoot)
                .Select(Path.GetFileName)
                .Select(x =>
                {
                    DateTime date;
                    bool ok = DateTime.TryParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                    return new { Date = date, Valid = ok };
                })
                .Where(x => x.Valid)
                .Select(x => x.Date)
                .OrderBy(x => x)
                .ToList();
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
                data.Tests = File.ReadLines(testsFile)
                    .Skip(1)
                    .Select(ParseCsvLine)
                    .Where(x => x.Count > 0 && !string.IsNullOrWhiteSpace(x[0]))
                    .Select(x => x[0])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }
            else
            {
                data.Tests = data.Records.Select(x => x.TestName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            return data;
        }

        private static void RebuildDerivedFiles()
        {
            Directory.CreateDirectory(PowerBiRoot);

            string dailyFile = Path.Combine(PowerBiRoot, "DailyProgress.csv");
            string testFile = Path.Combine(PowerBiRoot, "TestPerformance.csv");
            string currentFile = Path.Combine(PowerBiRoot, "CurrentClashes.csv");
            string powerBiHistory = Path.Combine(PowerBiRoot, "ClashHistory.csv");

            using (var historyWriter = new StreamWriter(HistoryFile, false, new UTF8Encoding(true)))
            using (var dailyWriter = new StreamWriter(dailyFile, false, new UTF8Encoding(true)))
            using (var testWriter = new StreamWriter(testFile, false, new UTF8Encoding(true)))
            {
                historyWriter.WriteLine(HistoryHeader());
                dailyWriter.WriteLine("SnapshotDate,Previous,Current,New,Reopened,Existing,Resolved,NetChange");
                testWriter.WriteLine("SnapshotDate,TestName,Previous,Current,New,Reopened,Existing,Resolved,NetChange");

                SnapshotData previous = SnapshotData.Empty();
                var everSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                SnapshotData latest = SnapshotData.Empty();
                ComparisonResult latestComparison = new ComparisonResult();

                foreach (DateTime date in GetSnapshotDates())
                {
                    SnapshotData current = LoadSnapshot(date);
                    ComparisonResult comparison = CompareSnapshots(previous.Records, current.Records, everSeen);

                    WriteHistoryRows(historyWriter, date, current.Records, comparison);
                    WriteDailyProgressRow(dailyWriter, date, previous, current, comparison);
                    WriteTestPerformanceRows(testWriter, date, previous, current, comparison);

                    foreach (ClashRecord record in current.Records)
                        everSeen.Add(record.ClashGuid);

                    previous = current;
                    latest = current;
                    latestComparison = comparison;
                }

                WriteClashCsv(currentFile, latest.Records, latestComparison.StateByGuid);
            }

            File.Copy(HistoryFile, powerBiHistory, true);
        }

        private static void WriteHistoryRows(
            StreamWriter writer,
            DateTime date,
            List<ClashRecord> current,
            ComparisonResult comparison)
        {
            foreach (ClashRecord r in current)
            {
                string state;
                if (!comparison.StateByGuid.TryGetValue(r.ClashGuid, out state))
                    state = "Existing";
                writer.WriteLine(ToHistoryCsvLine(r, state));
            }

            foreach (ClashRecord r in comparison.Resolved)
            {
                ClashRecord resolved = r.CloneForDate(date);
                writer.WriteLine(ToHistoryCsvLine(resolved, "Resolved"));
            }
        }

        private static void WriteDailyProgressRow(
            StreamWriter writer,
            DateTime date,
            SnapshotData previous,
            SnapshotData current,
            ComparisonResult comparison)
        {
            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int reopened = comparison.StateByGuid.Count(x => x.Value == "Reopened");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int resolved = comparison.Resolved.Count;

            writer.WriteLine(string.Join(",", new[]
            {
                Csv(date.ToString("yyyy-MM-dd")),
                previous.Records.Count.ToString(CultureInfo.InvariantCulture),
                current.Records.Count.ToString(CultureInfo.InvariantCulture),
                newCount.ToString(CultureInfo.InvariantCulture),
                reopened.ToString(CultureInfo.InvariantCulture),
                existing.ToString(CultureInfo.InvariantCulture),
                resolved.ToString(CultureInfo.InvariantCulture),
                (current.Records.Count - previous.Records.Count).ToString(CultureInfo.InvariantCulture)
            }));
        }

        private static void WriteTestPerformanceRows(
            StreamWriter writer,
            DateTime date,
            SnapshotData previous,
            SnapshotData current,
            ComparisonResult comparison)
        {
            var allTests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase);
            allTests.UnionWith(current.Tests);

            var prevByTest = previous.Records.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var currByTest = current.Records.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var resolvedByTest = comparison.Resolved.GroupBy(x => x.TestName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (string test in allTests.OrderBy(x => x))
            {
                List<ClashRecord> currentRows;
                if (!currByTest.TryGetValue(test, out currentRows))
                    currentRows = new List<ClashRecord>();

                int previousCount = prevByTest.ContainsKey(test) ? prevByTest[test] : 0;
                int currentCount = currentRows.Count;
                int newCount = currentRows.Count(x => StateOf(comparison, x.ClashGuid) == "New");
                int reopened = currentRows.Count(x => StateOf(comparison, x.ClashGuid) == "Reopened");
                int existing = currentRows.Count(x => StateOf(comparison, x.ClashGuid) == "Existing");
                int resolved = resolvedByTest.ContainsKey(test) ? resolvedByTest[test] : 0;

                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(date.ToString("yyyy-MM-dd")),
                    Csv(test),
                    previousCount.ToString(CultureInfo.InvariantCulture),
                    currentCount.ToString(CultureInfo.InvariantCulture),
                    newCount.ToString(CultureInfo.InvariantCulture),
                    reopened.ToString(CultureInfo.InvariantCulture),
                    existing.ToString(CultureInfo.InvariantCulture),
                    resolved.ToString(CultureInfo.InvariantCulture),
                    (currentCount - previousCount).ToString(CultureInfo.InvariantCulture)
                }));
            }
        }

        private static string StateOf(ComparisonResult comparison, string guid)
        {
            string state;
            return comparison.StateByGuid.TryGetValue(guid, out state) ? state : string.Empty;
        }

        private static void WriteClashCsv(string file, List<ClashRecord> records, Dictionary<string, string> states)
        {
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(HistoryHeader());

                foreach (ClashRecord r in records)
                {
                    string state = string.Empty;
                    if (states != null)
                        states.TryGetValue(r.ClashGuid, out state);

                    writer.WriteLine(ToHistoryCsvLine(r, state));
                }
            }
        }

        private static string HistoryHeader()
        {
            return "SnapshotDate,TestName,ClashGuid,TrackerState,NavisworksStatus,Distance,DateFound,GridLocation,X,Y,Z,ItemAElementId,ItemAName,ItemALayer,ItemAPath,ItemBElementId,ItemBName,ItemBLayer,ItemBPath,Description,Comments";
        }

        private static List<ClashRecord> ReadSnapshotCsv(string file)
        {
            var records = new List<ClashRecord>();
            bool first = true;

            foreach (string line in File.ReadLines(file))
            {
                if (first)
                {
                    first = false;
                    continue;
                }

                List<string> values = ParseCsvLine(line);
                if (values.Count < 21)
                    continue;

                records.Add(new ClashRecord
                {
                    SnapshotDate = ParseDate(values[0]) ?? DateTime.MinValue,
                    TestName = values[1],
                    ClashGuid = values[2],
                    Status = values[4],
                    Distance = ParseNullableDouble(values[5]),
                    DateFound = ParseDate(values[6]),
                    GridLocation = values[7],
                    X = ParseNullableDouble(values[8]),
                    Y = ParseNullableDouble(values[9]),
                    Z = ParseNullableDouble(values[10]),
                    ItemAElementId = values[11],
                    ItemAName = values[12],
                    ItemALayer = values[13],
                    ItemAPath = values[14],
                    ItemBElementId = values[15],
                    ItemBName = values[16],
                    ItemBLayer = values[17],
                    ItemBPath = values[18],
                    Description = values[19],
                    Comments = values[20]
                });
            }

            return records;
        }

        private static string ToHistoryCsvLine(ClashRecord r, string trackerState)
        {
            return string.Join(",", new[]
            {
                Csv(r.SnapshotDate.ToString("yyyy-MM-dd")),
                Csv(r.TestName),
                Csv(r.ClashGuid),
                Csv(trackerState),
                Csv(r.Status),
                Number(r.Distance),
                Csv(r.DateFound.HasValue ? r.DateFound.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty),
                Csv(r.GridLocation),
                Number(r.X),
                Number(r.Y),
                Number(r.Z),
                Csv(r.ItemAElementId),
                Csv(r.ItemAName),
                Csv(r.ItemALayer),
                Csv(r.ItemAPath),
                Csv(r.ItemBElementId),
                Csv(r.ItemBName),
                Csv(r.ItemBLayer),
                Csv(r.ItemBPath),
                Csv(r.Description),
                Csv(r.Comments)
            });
        }

        private static void PrintSummary(
            DateTime date,
            SnapshotData current,
            SnapshotData previous,
            ComparisonResult comparison)
        {
            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int reopened = comparison.StateByGuid.Count(x => x.Value == "Reopened");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int resolved = comparison.Resolved.Count;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("SNAPSHOT SUMMARY");
            Console.WriteLine("================");
            Console.ResetColor();
            Console.WriteLine("Date        : " + date.ToString("yyyy-MM-dd"));
            Console.WriteLine("Clash Tests : " + current.Tests.Count);
            Console.WriteLine("Previous    : " + previous.Records.Count);
            Console.WriteLine("Current     : " + current.Records.Count);
            Console.WriteLine("New         : " + newCount);
            Console.WriteLine("Reopened    : " + reopened);
            Console.WriteLine("Existing    : " + existing);
            Console.WriteLine("Resolved    : " + resolved);
            Console.WriteLine("Net Change  : " + (current.Records.Count - previous.Records.Count));
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

        private static string LocalName(XElement element)
        {
            return element.Name.LocalName;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            DateTime date;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
                return date;
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date))
                return date;

            return null;
        }

        private static double? ParseDouble(string value)
        {
            double number;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                return number;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out number))
                return number;
            return null;
        }

        private static double? ParseNullableDouble(string value)
        {
            return ParseDouble(value);
        }

        private static string Number(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    values.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            values.Add(sb.ToString());
            return values;
        }

        private static void Pause()
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private class SnapshotData
        {
            public DateTime Date { get; set; }
            public List<ClashRecord> Records { get; set; }
            public List<string> Tests { get; set; }

            public static SnapshotData Empty()
            {
                return new SnapshotData
                {
                    Records = new List<ClashRecord>(),
                    Tests = new List<string>()
                };
            }
        }

        private class ComparisonResult
        {
            public Dictionary<string, string> StateByGuid { get; private set; }
            public List<ClashRecord> Resolved { get; private set; }

            public ComparisonResult()
            {
                StateByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Resolved = new List<ClashRecord>();
            }
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

            public ClashRecord CloneForDate(DateTime date)
            {
                return new ClashRecord
                {
                    SnapshotDate = date,
                    TestName = TestName,
                    ClashGuid = ClashGuid,
                    ClashName = ClashName,
                    Status = Status,
                    Distance = Distance,
                    DateFound = DateFound,
                    GridLocation = GridLocation,
                    X = X,
                    Y = Y,
                    Z = Z,
                    ItemAElementId = ItemAElementId,
                    ItemAName = ItemAName,
                    ItemALayer = ItemALayer,
                    ItemAPath = ItemAPath,
                    ItemBElementId = ItemBElementId,
                    ItemBName = ItemBName,
                    ItemBLayer = ItemBLayer,
                    ItemBPath = ItemBPath,
                    Description = Description,
                    Comments = Comments
                };
            }
        }
    }
}
