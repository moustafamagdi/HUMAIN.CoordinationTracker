using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private static bool HasStatus(ClashRecord r, string status)
        { return r != null && string.Equals((r.Status ?? "").Trim(), status, StringComparison.OrdinalIgnoreCase); }
        private static bool IsResolved(ClashRecord r) { return HasStatus(r, "resolved"); }
        private static bool IsApproved(ClashRecord r) { return HasStatus(r, "approved"); }
        // Unknown or blank statuses remain actionable, so an unrecognized value cannot silently close a clash.
        private static bool IsActionable(ClashRecord r) { return r != null && !IsResolved(r) && !IsApproved(r); }

        private const string OperationalHeader = "SnapshotDateTime,SnapshotDate,TestName,Severity,DisciplineA,DisciplineB,DisciplinePair,TotalCurrent,Unresolved,Actionable,Approved,Reviewed,ResolvedPresent,PreviousActionable,NewActionable,ReturnedActionable,ResolvedFromActionable,ApprovedFromActionable,NewlyApproved,ApprovalRevoked,Resolved,ResolvedByStatus,ResolvedByDisappearance,NetActionableReduction";
        private class OperationalMetric
        {
            public int TotalCurrent, Unresolved, Actionable, Approved, Reviewed, ResolvedPresent;
            public int PreviousActionable, NewActionable, ReturnedActionable, ResolvedFromActionable, ApprovedFromActionable;
            public int NewlyApproved, ApprovalRevoked, Resolved, ResolvedByStatus, ResolvedByDisappearance, NetActionableReduction;
        }

        private static OperationalMetric BuildOperational(List<ClashRecord> previous, List<ClashRecord> current, HashSet<string> everSeen, bool baseline)
        {
            var pmap = previous.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var cmap = current.ToDictionary(x => x.ClashGuid, StringComparer.OrdinalIgnoreCase);
            var m = new OperationalMetric {
                TotalCurrent = current.Count, Unresolved = current.Count(x => !IsResolved(x)),
                Actionable = current.Count(IsActionable), Approved = current.Count(IsApproved),
                Reviewed = current.Count(x => HasStatus(x, "reviewed")), ResolvedPresent = current.Count(IsResolved),
                PreviousActionable = previous.Count(IsActionable)
            };
            if (baseline) return m;
            foreach (ClashRecord c in current)
            {
                ClashRecord p; pmap.TryGetValue(c.ClashGuid, out p);
                if (IsActionable(c) && !IsActionable(p))
                {
                    if (everSeen.Contains(c.ClashGuid)) m.ReturnedActionable++;
                    else m.NewActionable++;
                    if (IsApproved(p)) m.ApprovalRevoked++;
                }
                // A first observation already approved is a stock, not work completed during the observed interval.
                if (IsApproved(c) && p != null && !IsApproved(p)) m.NewlyApproved++;
                if (IsApproved(c) && IsActionable(p)) m.ApprovedFromActionable++;
            }
            foreach (ClashRecord p in previous)
            {
                ClashRecord c; cmap.TryGetValue(p.ClashGuid, out c);
                if (!IsResolved(p) && (c == null || IsResolved(c)))
                {
                    m.Resolved++;
                    if (c == null) m.ResolvedByDisappearance++; else m.ResolvedByStatus++;
                    if (IsActionable(p)) m.ResolvedFromActionable++;
                }
            }
            m.NetActionableReduction = m.PreviousActionable - m.Actionable;
            int reconciled = m.PreviousActionable + m.NewActionable + m.ReturnedActionable - m.ResolvedFromActionable - m.ApprovedFromActionable;
            if (reconciled != m.Actionable) throw new InvalidDataException("Actionable balance did not reconcile.");
            return m;
        }

        private static void WriteOperationalRows(StreamWriter writer, SnapshotData previous, SnapshotData current, HashSet<string> everSeen)
        {
            var tests = new HashSet<string>(previous.Tests, StringComparer.OrdinalIgnoreCase); tests.UnionWith(current.Tests);
            var p = previous.Records.ToLookup(x => x.TestName, StringComparer.OrdinalIgnoreCase);
            var c = current.Records.ToLookup(x => x.TestName, StringComparer.OrdinalIgnoreCase);
            foreach (string test in tests.OrderBy(x => x))
            {
                OperationalMetric m = BuildOperational(p[test].ToList(), c[test].ToList(), everSeen, previous.Date == DateTime.MinValue);
                TestMetadata meta = ParseTestMetadata(test);
                writer.WriteLine(string.Join(",", new[] {
                    Csv(current.Date.ToString("yyyy-MM-dd HH:mm:ss")), Csv(current.Date.ToString("yyyy-MM-dd")), Csv(test),
                    Csv(meta.Severity), Csv(meta.DisciplineA), Csv(meta.DisciplineB), Csv(meta.DisciplinePair),
                    m.TotalCurrent.ToString(), m.Unresolved.ToString(), m.Actionable.ToString(), m.Approved.ToString(), m.Reviewed.ToString(), m.ResolvedPresent.ToString(),
                    m.PreviousActionable.ToString(), m.NewActionable.ToString(), m.ReturnedActionable.ToString(), m.ResolvedFromActionable.ToString(), m.ApprovedFromActionable.ToString(),
                    m.NewlyApproved.ToString(), m.ApprovalRevoked.ToString(), m.Resolved.ToString(), m.ResolvedByStatus.ToString(), m.ResolvedByDisappearance.ToString(), m.NetActionableReduction.ToString()
                }));
            }
        }

        private static void RunWorkflowTests()
        {
            int assertions = 0;
            Action<bool, string> check = (ok, name) => { assertions++; if (!ok) throw new Exception("FAILED: " + name); };
            var pathRecord = new ClashRecord();
            FillObject(pathRecord, System.Xml.Linq.XElement.Parse("<clashobject xmlns='urn:test'><objectattribute><name>Element ID</name><value>42</value></objectattribute><pathlink><node>File</node><node>Federation.nwd</node><node>Electrical.nwc</node><node>Tray &amp; Fittings</node></pathlink><smarttags><smarttag><name>Item Path</name><value>legacy</value></smarttag></smarttags></clashobject>"), true);
            check(pathRecord.ItemAPath == "File > Federation.nwd > Electrical.nwc > Tray & Fittings", "native namespaced path preserves hierarchy and decoded XML");
            check(pathRecord.ItemAElementId == "42", "path parsing preserves element ID");
            FillObject(pathRecord, System.Xml.Linq.XElement.Parse("<clashobject><pathlink><node> </node></pathlink><smarttags><smarttag><name>Item Path</name><value>Legacy.nwc</value></smarttag></smarttags></clashobject>"), false);
            check(pathRecord.ItemBPath == "Legacy.nwc", "empty native path falls back to legacy smart tag");
            check(pathRecord.ItemAPath.Contains("Electrical.nwc"), "A and B paths stay separate");
            FillObject(pathRecord, System.Xml.Linq.XElement.Parse("<clashobject/>"), false);
            check(pathRecord.ItemBPath == "", "absent path remains absent");
            Func<string, List<ClashRecord>> rows = status => status == null ? new List<ClashRecord>() : new List<ClashRecord> {
                new ClashRecord { ClashGuid = "test-guid", TestName = "01_Critical_EL vs STR", Status = status, Severity = "Critical" }
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test-guid" };
            var unseen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] statuses = { null, "new", "active", "reviewed", "approved", "resolved", " RESOLVED ", "unknown" };
            foreach (string before in statuses) foreach (string after in statuses)
            {
                var p = rows(before); var c = rows(after);
                var comparison = CompareSnapshots(p, c, seen, false);
                var m = BuildOperational(p, c, seen, false);
                check(m.Resolved == comparison.Resolved.Count, "physical closure agrees " + before + " -> " + after);
                check(m.Actionable == m.PreviousActionable + m.NewActionable + m.ReturnedActionable - m.ResolvedFromActionable - m.ApprovedFromActionable, "actionable balance");
                int reopened = comparison.StateByGuid.Count(x => x.Value == "Reopened");
                check(c.Count(x => !IsResolved(x)) == p.Count(x => !IsResolved(x)) + reopened - comparison.Resolved.Count, "unresolved balance");
            }
            check(CompareSnapshots(rows("active"), rows("resolved"), seen, false).Resolved.Count == 1, "resolved without compact");
            check(CompareSnapshots(rows("resolved"), rows(null), seen, false).Resolved.Count == 0, "compact after resolved not double counted");
            check(CompareSnapshots(rows("resolved"), rows("resolved"), seen, false).Resolved.Count == 0, "retained resolved not double counted");
            check(CompareSnapshots(rows("active"), rows(null), seen, false).Resolved.Count == 1, "direct disappearance");
            check(StateOf(CompareSnapshots(rows("resolved"), rows("active"), seen, false), "test-guid") == "Reopened", "resolved GUID reopens");
            check(BuildOperational(rows("approved"), rows("reviewed"), seen, false).ApprovalRevoked == 1, "approval revocation");
            check(BuildOperational(rows("active"), rows("approved"), seen, false).Resolved == 0, "approval is not resolution");
            check(BuildOperational(rows("approved"), rows("resolved"), seen, false).ResolvedFromActionable == 0, "accepted then resolved is not double operational progress");
            check(BuildOperational(rows(null), rows("resolved"), unseen, true).Resolved == 0, "baseline closed stock not progress");
            check(BuildOperational(rows(null), rows("approved"), unseen, false).NewlyApproved == 0, "first seen approved not approval transition");
            check(BuildOperational(rows(null), rows("new"), unseen, false).NewActionable == 1, "new GUID inflow");
            var life = new Dictionary<string, LifecycleRecord>();
            var prior = rows(null); var ever = new HashSet<string>(); int tick = 0;
            foreach (string status in new[] { "active", "resolved", "resolved", null, "active" })
            {
                var now = rows(status); var cmp = CompareSnapshots(prior, now, ever, tick == 0);
                var snapshot = new SnapshotData { Date = new DateTime(2026, 1, 1).AddDays(tick), Records = now, Tests = new List<string> { "01_Critical_EL vs STR" } };
                UpdateLifecycle(life, snapshot.Date, snapshot, cmp);
                foreach (var r in now) ever.Add(r.ClashGuid);
                prior = now; tick++;
            }
            check(life["test-guid"].ResolutionCount == 1 && life["test-guid"].ReopenCount == 1 && life["test-guid"].IsOpen, "lifecycle survives resolved / retained / compact / reopen");
            Console.WriteLine("PASS: " + assertions + " workflow assertions.");
        }
    }
}
