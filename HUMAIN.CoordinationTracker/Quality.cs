using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private const string QualityHeader = "SnapshotDateTime,SnapshotDate,TestName,PresentInExport,PreviousPresent,TestMissing,TestAdded,PreviousTotal,CurrentTotal,DropPct,LargeDrop,UnknownStatuses,Disappeared,HasAlert,Reason,AlertId,ReviewStatus,ReviewReason,ReviewedBy,ReviewedAt,RequiresAction";
        private class QualityMetric
        {
            public int PresentInExport, PreviousPresent, TestMissing, TestAdded, PreviousTotal, CurrentTotal;
            public double DropPct;
            public int LargeDrop, UnknownStatuses, Disappeared, HasAlert;
            public string Reason;
        }
        private static QualityMetric BuildQuality(bool previousPresent, bool present, List<ClashRecord> previous, List<ClashRecord> current, bool baseline)
        {
            var m = new QualityMetric {
                PresentInExport = present ? 1 : 0, PreviousPresent = previousPresent ? 1 : 0,
                PreviousTotal = previous.Count, CurrentTotal = current.Count,
                TestMissing = !baseline && previousPresent && !present ? 1 : 0,
                TestAdded = !baseline && !previousPresent && present ? 1 : 0
            };
            var known = new HashSet<string>(new[] { "new", "active", "reviewed", "approved", "resolved" }, StringComparer.OrdinalIgnoreCase);
            m.UnknownStatuses = current.Count(x => !known.Contains((x.Status ?? "").Trim()));
            m.DropPct = baseline || previous.Count == 0 ? 0 : Math.Max(0, (previous.Count - current.Count) / (double)previous.Count);
            // Review signal, not proof of a bad export. Includes legitimate Compact and design changes.
            m.LargeDrop = !baseline && present && previousPresent && previous.Count - current.Count >= 100 && m.DropPct >= 0.30 ? 1 : 0;
            var now = new HashSet<string>(current.Select(x => x.ClashGuid), StringComparer.OrdinalIgnoreCase);
            m.Disappeared = baseline ? 0 : previous.Count(x => !now.Contains(x.ClashGuid));
            var reasons = new List<string>();
            if (m.TestMissing == 1) reasons.Add("Test absent from export; verify scope before treating disappearance as resolution");
            if (m.TestAdded == 1) reasons.Add("Test added or renamed; comparison scope changed");
            if (m.LargeDrop == 1) reasons.Add("Current records fell by at least 30% and 100 rows; verify export or Compact");
            if (m.UnknownStatuses > 0) reasons.Add("Blank or unrecognized Navisworks status; kept actionable");
            m.HasAlert = reasons.Count > 0 ? 1 : 0;
            m.Reason = reasons.Count > 0 ? string.Join(" | ", reasons) : baseline ? "Baseline; no prior snapshot for comparison" : "No rule triggered; export completeness is not guaranteed";
            return m;
        }
        private static void WriteQualityRows(StreamWriter writer, SnapshotData previous, SnapshotData current)
        {
            var tests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase); tests.UnionWith(current.Tests);
            var prevTests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase);
            var currTests = new HashSet<string>(current.Tests, StringComparer.OrdinalIgnoreCase);
            var p = previous.Records.ToLookup(x => x.TestName, StringComparer.OrdinalIgnoreCase);
            var c = current.Records.ToLookup(x => x.TestName, StringComparer.OrdinalIgnoreCase);
            foreach (string test in tests.OrderBy(x => x))
            {
                var m = BuildQuality(prevTests.Contains(test), currTests.Contains(test), p[test].ToList(), c[test].ToList(), previous.Date == DateTime.MinValue);
                string alertId = QualityAlertId(previous, current, test);
                QualityDecision decision = ReadQualityDecision(alertId);
                writer.WriteLine(string.Join(",", new[] {
                    Csv(current.Date.ToString("yyyy-MM-dd HH:mm:ss")), Csv(current.Date.ToString("yyyy-MM-dd")), Csv(test),
                    m.PresentInExport.ToString(), m.PreviousPresent.ToString(), m.TestMissing.ToString(), m.TestAdded.ToString(),
                    m.PreviousTotal.ToString(), m.CurrentTotal.ToString(), Number(m.DropPct), m.LargeDrop.ToString(), m.UnknownStatuses.ToString(),
                    m.Disappeared.ToString(), m.HasAlert.ToString(), Csv(m.Reason), Csv(alertId),
                    Csv(m.HasAlert == 0 ? "Not required" : decision.Status), Csv(decision.Reason), Csv(decision.Reviewer), Csv(decision.ReviewedAt),
                    RequiresQualityAction(m, decision).ToString()
                }));
            }
        }
        private static void RunQualityTests()
        {
            int count = 0; Action<bool, string> check = (v, n) => { count++; if (!v) throw new Exception("FAILED quality: " + n); };
            Func<int, List<ClashRecord>> rows = n => Enumerable.Range(0, n).Select(i => new ClashRecord { ClashGuid = "q-" + i, Status = "active" }).ToList();
            check(BuildQuality(true, false, rows(1), rows(0), false).TestMissing == 1, "missing test");
            check(BuildQuality(true, true, rows(1), rows(0), false).TestMissing == 0, "zero rows is not missing test");
            check(BuildQuality(false, true, rows(0), rows(1), true).HasAlert == 0, "baseline not added scope");
            check(BuildQuality(false, true, rows(0), rows(1), false).TestAdded == 1, "added test");
            check(BuildQuality(true, true, rows(1000), rows(700), false).LargeDrop == 1, "30 percent boundary");
            check(BuildQuality(true, true, rows(1000), rows(701), false).LargeDrop == 0, "under percent threshold");
            check(BuildQuality(true, true, rows(100), rows(1), false).LargeDrop == 0, "under absolute threshold");
            check(BuildQuality(true, true, rows(100), rows(0), false).LargeDrop == 1, "100 row boundary");
            check(BuildQuality(true, true, rows(500), rows(600), false).HasAlert == 0, "growth not drop");
            var unknown = rows(1); unknown[0].Status = "";
            check(BuildQuality(false, true, rows(0), unknown, true).UnknownStatuses == 1, "unknown status even baseline");
            check(BuildQuality(true, true, rows(100), rows(90), false).Disappeared == 10, "disappearance count");
            Console.WriteLine("PASS: " + count + " quality assertions.");
        }
    }
}
