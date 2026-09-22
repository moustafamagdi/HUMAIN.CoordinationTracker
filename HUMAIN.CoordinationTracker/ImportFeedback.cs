using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private static string ExportFileCheck(string folder)
        {
            var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories).Select(x => new FileInfo(x)).OrderBy(x => x.LastWriteTimeUtc).ToList();
            if (files.Count == 0) return "No XML files found.";
            var text = new StringBuilder("EXPORT FILE CHECK (includes subfolders)\r\n");
            var span = files.Last().LastWriteTimeUtc - files.First().LastWriteTimeUtc;
            text.AppendLine("XML files: " + files.Count + " | File modification times (local): " + files.First().LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + " to " + files.Last().LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            if (span > TimeSpan.FromMinutes(5))
            {
                text.AppendLine("WARNING: file times span more than 5 minutes. This folder may contain files from different exports. Check older files or use a new empty export folder.");
                foreach (var file in files) text.AppendLine(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + " | " + file.FullName.Substring(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            text.AppendLine("File times are a hint, not proof of export age. All XML files are included; none are automatically excluded.");
            return text.ToString();
        }

        private static string TestChanges(SnapshotData before, SnapshotData after)
        {
            var text = new StringBuilder();
            foreach (string test in before.Tests.Union(after.Tests).OrderBy(x => x))
            {
                string change = !before.Tests.Contains(test) ? "ADDED" : !after.Tests.Contains(test) ? "REMOVED" : "RETAINED";
                text.AppendLine(change + " | " + test + " | " + before.Records.Count(x => x.TestName == test) + " -> " + after.Records.Count(x => x.TestName == test) + " records");
            }
            return text.ToString();
        }

        private static string QualityBlockerText(Dictionary<string, string> alert)
        {
            if (alert["RequiresAction"] != "1") return "No";
            if (alert["TestAdded"] == "1" || alert["TestMissing"] == "1")
                return alert["ReviewStatus"] == "Accepted" ? "Scope change - accepted review" : "Scope change";
            return "Review / correction required";
        }

        private static string QualityChangeSummary(List<Dictionary<string, string>> before, List<Dictionary<string, string>> after)
        {
            Func<Dictionary<string, string>, string> key = a => a["SnapshotDateTime"] + " | " + a["TestName"];
            var text = new StringBuilder("QUALITY RESULTS (all active intervals)\r\n");
            foreach (var a in before.Where(a => !after.Any(b => key(a) == key(b)))) text.AppendLine("CLEARED | " + key(a));
            foreach (var a in after) text.AppendLine((before.Any(b => key(a) == key(b)) ? "STILL PRESENT" : "NEW") + " | " + key(a) + " | " + a["Reason"] + " | Forecast blocker: " + QualityBlockerText(a));
            if (after.Count == 0) text.AppendLine("No remaining quality alerts. Forecast may still require sufficient history and a positive burn rate.");
            return text.ToString();
        }

        private static void RunImportFeedbackTests()
        {
            int count = 0;
            Action<bool, string> check = (ok, message) => { count++; if (!ok) throw new Exception(message); };
            var a = SnapshotData.Empty(); a.Tests.Add("Old"); a.Tests.Add("Keep");
            var b = SnapshotData.Empty(); b.Tests.Add("New"); b.Tests.Add("Keep");
            string changes = TestChanges(a, b);
            check(changes.Contains("REMOVED | Old") && changes.Contains("ADDED | New") && changes.Contains("RETAINED | Keep"), "test scope comparison");
            var alert = new Dictionary<string,string> { {"SnapshotDateTime","today"}, {"TestName","New"}, {"Reason","Added"}, {"RequiresAction","1"}, {"TestAdded","1"}, {"TestMissing","0"}, {"ReviewStatus","Accepted"} };
            check(QualityBlockerText(alert) == "Scope change - accepted review", "accepted scope explanation");
            var list = new List<Dictionary<string,string>> { alert };
            check(QualityChangeSummary(list, new List<Dictionary<string,string>>()).Contains("CLEARED | today | New"), "cleared alert feedback");
            check(QualityChangeSummary(list, list).Contains("STILL PRESENT"), "remaining alert feedback");
            string dir = Path.Combine(Path.GetTempPath(), "humain-export-check-" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,"a.xml"), "test"); File.WriteAllText(Path.Combine(dir,"b.xml"), "test");
                File.SetLastWriteTimeUtc(Path.Combine(dir,"a.xml"), DateTime.UtcNow.AddHours(-1));
                check(ExportFileCheck(dir).Contains("WARNING"), "mixed export times warned");
                check(Directory.GetFiles(dir).Length == 2, "warning preserves files");
                File.SetLastWriteTimeUtc(Path.Combine(dir,"a.xml"), DateTime.UtcNow);
                check(!ExportFileCheck(dir).Contains("WARNING"), "close export times not warned");
            } finally { Directory.Delete(dir, true); }
            Console.WriteLine("Import feedback tests passed: " + count);
        }
    }
}
