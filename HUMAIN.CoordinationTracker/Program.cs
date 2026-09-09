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

                var imported = ImportSnapshot(inputFolder, snapshotDate);

                if (imported.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No clash results were found in the selected XML files.");
                    Console.ResetColor();
                    Pause();
                    return;
                }

                var previousSnapshot = LoadPreviousSnapshot(snapshotDate);
                var comparison = CompareSnapshots(previousSnapshot, imported);

                SaveSnapshot(snapshotDate, imported);
                AppendHistory(snapshotDate, imported, comparison);
                ExportPowerBiFiles(snapshotDate, imported, comparison);

                PrintSummary(snapshotDate, imported, previousSnapshot, comparison);

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

        private static List<ClashRecord> ImportSnapshot(string folder, DateTime snapshotDate)
        {
            string[] files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories);

            Console.WriteLine();
            Console.WriteLine("XML files found: " + files.Length);
            Console.WriteLine("Importing...");

            var results = new List<ClashRecord>();
            int failedFiles = 0;

            foreach (string file in files)
            {
                try
                {
                    XDocument doc = XDocument.Load(file, LoadOptions.None);
                    string testName = GetTestName(doc, file);

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
                catch
                {
                    failedFiles++;
                }
            }

            if (failedFiles > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: " + failedFiles + " XML file(s) could not be parsed.");
                Console.ResetColor();
            }

            return results
                .GroupBy(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
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
                ElementValue(clashObject, "smarttags"));

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

        private static List<ClashRecord> LoadPreviousSnapshot(DateTime currentDate)
        {
            var dirs = Directory.GetDirectories(SnapshotsRoot)
                .Select(Path.GetFileName)
                .Select(x =>
                {
                    DateTime date;
                    bool ok = DateTime.TryParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                    return new { Name = x, Date = date, Valid = ok };
                })
                .Where(x => x.Valid && x.Date < currentDate)
                .OrderByDescending(x => x.Date)
                .ToList();

            if (dirs.Count == 0)
                return new List<ClashRecord>();

            string file = Path.Combine(SnapshotsRoot, dirs[0].Name, "snapshot.csv");
            return File.Exists(file) ? ReadSnapshotCsv(file) : new List<ClashRecord>();
        }

        private static ComparisonResult CompareSnapshots(List<ClashRecord> previous, List<ClashRecord> current)
        {
            var previousMap = previous.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var currentMap = current.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);

            var result = new ComparisonResult();

            foreach (ClashRecord c in current)
            {
                if (previousMap.ContainsKey(c.ClashGuid))
                    result.StateByGuid[c.ClashGuid] = "Existing";
                else
                    result.StateByGuid[c.ClashGuid] = "New";
            }

            foreach (ClashRecord p in previous)
            {
                if (!currentMap.ContainsKey(p.ClashGuid))
                    result.Disappeared.Add(p);
            }

            return result;
        }

        private static void SaveSnapshot(DateTime date, List<ClashRecord> records)
        {
            string folder = Path.Combine(SnapshotsRoot, date.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(folder);

            string file = Path.Combine(folder, "snapshot.csv");
            WriteClashCsv(file, records, null);
        }

        private static void AppendHistory(DateTime date, List<ClashRecord> current, ComparisonResult comparison)
        {
            bool writeHeader = !File.Exists(HistoryFile);

            using (var writer = new StreamWriter(HistoryFile, true, new UTF8Encoding(true)))
            {
                if (writeHeader)
                {
                    writer.WriteLine("SnapshotDate,TestName,ClashGuid,TrackerState,NavisworksStatus,Distance,DateFound,GridLocation,X,Y,Z,ItemAElementId,ItemAName,ItemALayer,ItemAPath,ItemBElementId,ItemBName,ItemBLayer,ItemBPath,Description,Comments");
                }

                foreach (ClashRecord r in current)
                {
                    string state;
                    if (!comparison.StateByGuid.TryGetValue(r.ClashGuid, out state))
                        state = "Existing";

                    writer.WriteLine(ToHistoryCsvLine(r, state));
                }

                foreach (ClashRecord r in comparison.Disappeared)
                {
                    var disappeared = r.CloneForDate(date);
                    writer.WriteLine(ToHistoryCsvLine(disappeared, "Disappeared"));
                }
            }
        }

        private static void ExportPowerBiFiles(DateTime date, List<ClashRecord> current, ComparisonResult comparison)
        {
            WriteClashCsv(Path.Combine(PowerBiRoot, "CurrentClashes.csv"), current, comparison.StateByGuid);
            ExportDailyProgress(date, current, comparison);
            ExportTestPerformance(date, current, comparison);
            File.Copy(HistoryFile, Path.Combine(PowerBiRoot, "ClashHistory.csv"), true);
        }

        private static void ExportDailyProgress(DateTime date, List<ClashRecord> current, ComparisonResult comparison)
        {
            string file = Path.Combine(PowerBiRoot, "DailyProgress.csv");
            bool writeHeader = !File.Exists(file);

            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int disappeared = comparison.Disappeared.Count;
            int previous = existing + disappeared;
            int currentCount = current.Count;
            int netChange = currentCount - previous;

            using (var writer = new StreamWriter(file, true, new UTF8Encoding(true)))
            {
                if (writeHeader)
                    writer.WriteLine("SnapshotDate,Previous,Current,New,Existing,Disappeared,NetChange");

                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(date.ToString("yyyy-MM-dd")),
                    previous.ToString(CultureInfo.InvariantCulture),
                    currentCount.ToString(CultureInfo.InvariantCulture),
                    newCount.ToString(CultureInfo.InvariantCulture),
                    existing.ToString(CultureInfo.InvariantCulture),
                    disappeared.ToString(CultureInfo.InvariantCulture),
                    netChange.ToString(CultureInfo.InvariantCulture)
                }));
            }
        }

        private static void ExportTestPerformance(DateTime date, List<ClashRecord> current, ComparisonResult comparison)
        {
            string file = Path.Combine(PowerBiRoot, "TestPerformance.csv");
            bool writeHeader = !File.Exists(file);

            var currentByTest = current.GroupBy(x => x.TestName).ToDictionary(g => g.Key, g => g.ToList());
            var disappearedByTest = comparison.Disappeared.GroupBy(x => x.TestName).ToDictionary(g => g.Key, g => g.Count());

            using (var writer = new StreamWriter(file, true, new UTF8Encoding(true)))
            {
                if (writeHeader)
                    writer.WriteLine("SnapshotDate,TestName,Current,New,Existing,Disappeared,Previous,NetChange");

                foreach (var kvp in currentByTest.OrderBy(x => x.Key))
                {
                    int currentCount = kvp.Value.Count;
                    int newCount = kvp.Value.Count(x => comparison.StateByGuid.ContainsKey(x.ClashGuid) && comparison.StateByGuid[x.ClashGuid] == "New");
                    int existing = currentCount - newCount;
                    int disappeared = disappearedByTest.ContainsKey(kvp.Key) ? disappearedByTest[kvp.Key] : 0;
                    int previous = existing + disappeared;
                    int net = currentCount - previous;

                    writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(date.ToString("yyyy-MM-dd")),
                        Csv(kvp.Key),
                        currentCount.ToString(CultureInfo.InvariantCulture),
                        newCount.ToString(CultureInfo.InvariantCulture),
                        existing.ToString(CultureInfo.InvariantCulture),
                        disappeared.ToString(CultureInfo.InvariantCulture),
                        previous.ToString(CultureInfo.InvariantCulture),
                        net.ToString(CultureInfo.InvariantCulture)
                    }));
                }
            }
        }

        private static void WriteClashCsv(string file, List<ClashRecord> records, Dictionary<string, string> states)
        {
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("SnapshotDate,TestName,ClashGuid,TrackerState,NavisworksStatus,Distance,DateFound,GridLocation,X,Y,Z,ItemAElementId,ItemAName,ItemALayer,ItemAPath,ItemBElementId,ItemBName,ItemBLayer,ItemBPath,Description,Comments");

                foreach (ClashRecord r in records)
                {
                    string state = string.Empty;
                    if (states != null)
                        states.TryGetValue(r.ClashGuid, out state);

                    writer.WriteLine(ToHistoryCsvLine(r, state));
                }
            }
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

        private static void PrintSummary(DateTime date, List<ClashRecord> current, List<ClashRecord> previous, ComparisonResult comparison)
        {
            int newCount = comparison.StateByGuid.Count(x => x.Value == "New");
            int existing = comparison.StateByGuid.Count(x => x.Value == "Existing");
            int disappeared = comparison.Disappeared.Count;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("SNAPSHOT SUMMARY");
            Console.WriteLine("================");
            Console.ResetColor();
            Console.WriteLine("Date        : " + date.ToString("yyyy-MM-dd"));
            Console.WriteLine("Clash Tests : " + current.Select(x => x.TestName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Console.WriteLine("Previous    : " + previous.Count);
            Console.WriteLine("Current     : " + current.Count);
            Console.WriteLine("New         : " + newCount);
            Console.WriteLine("Existing    : " + existing);
            Console.WriteLine("Disappeared : " + disappeared);
            Console.WriteLine("Net Change  : " + (current.Count - previous.Count));
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

        private class ComparisonResult
        {
            public Dictionary<string, string> StateByGuid { get; private set; }
            public List<ClashRecord> Disappeared { get; private set; }

            public ComparisonResult()
            {
                StateByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Disappeared = new List<ClashRecord>();
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
