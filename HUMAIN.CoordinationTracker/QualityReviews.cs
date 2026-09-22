using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private class QualityDecision
        {
            public string Status { get; set; }
            public string Reason { get; set; }
            public string Reviewer { get; set; }
            public string ReviewedAt { get; set; }
            public QualityDecision() { Status = "Pending Review"; Reason = Reviewer = ReviewedAt = ""; }
        }

        // Bind a decision to the precise test interval and source records, not just a date/name.
        private static string QualityAlertId(SnapshotData previous, SnapshotData current, string test)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var data = new object[] { "quality-rules-v1", previous.Date.ToString("O"), current.Date.ToString("O"), test,
                previous.Tests.Contains(test), current.Tests.Contains(test),
                previous.Records.Where(x => x.TestName == test).OrderBy(x => x.ClashGuid, StringComparer.Ordinal).ToArray(),
                current.Records.Where(x => x.TestName == test).OrderBy(x => x.ClashGuid, StringComparer.Ordinal).ToArray() };
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(serializer.Serialize(data)))).Replace("-", "").ToLowerInvariant();
        }

        private static QualityDecision ReadQualityDecision(string id)
        {
            string path = Path.Combine(AppRoot, "QualityReviews", id + ".json");
            if (!File.Exists(path)) return new QualityDecision();
            var decision = new JavaScriptSerializer().Deserialize<QualityDecision>(File.ReadAllText(path));
            if (decision == null || !new[] { "Pending Review", "Accepted", "Needs Correction" }.Contains(decision.Status))
                throw new InvalidDataException("Invalid saved quality review: " + id);
            return decision;
        }

        private static int RequiresQualityAction(QualityMetric metric, QualityDecision decision)
        {
            // Acceptance is not scope accounting: omitted/added tests still require correction.
            return metric.HasAlert == 0 ? 0 : decision.Status == "Accepted" && metric.TestMissing == 0 && metric.TestAdded == 0 ? 0 : 1;
        }

        private static void SaveQualityDecision(string id, string status, string reason, string reviewer)
        {
            if (id.Length != 64 || id.Any(c => !"0123456789abcdef".Contains(c))) throw new ArgumentException("Invalid alert identifier.");
            if (!new[] { "Pending Review", "Accepted", "Needs Correction" }.Contains(status)) throw new ArgumentException("Choose a review status.");
            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(reviewer)) throw new ArgumentException("Enter the review reason and reviewer name.");
            var decision = new QualityDecision { Status = status, Reason = reason.Trim().Replace('\r', ' ').Replace('\n', ' '), Reviewer = reviewer.Trim().Replace('\r', ' ').Replace('\n', ' '), ReviewedAt = DateTimeOffset.Now.ToString("O") };
            string root = Path.Combine(AppRoot, "QualityReviews"); Directory.CreateDirectory(root);
            string json = new JavaScriptSerializer().Serialize(decision);
            File.WriteAllText(Path.Combine(root, id + ".json"), json, Encoding.UTF8);
            File.AppendAllText(Path.Combine(root, "history.jsonl"), new JavaScriptSerializer().Serialize(new { AlertId = id, Decision = decision }) + Environment.NewLine, Encoding.UTF8);
        }

        private static List<Dictionary<string, string>> ReadQualityAlerts()
        {
            string file = Path.Combine(PowerBiRoot, "SnapshotQuality.csv");
            if (!File.Exists(file)) return new List<Dictionary<string, string>>();
            var lines = File.ReadAllLines(file); var header = ParseCsvLine(lines[0]);
            if (!header.Contains("AlertId")) throw new InvalidDataException("Rebuild outputs once with this version to enable quality reviews.");
            return lines.Skip(1).Where(x => x.Length > 0).Select(line => {
                var values = ParseCsvLine(line);
                if (values.Count != header.Count) throw new InvalidDataException("Invalid quality export row. Rebuild outputs.");
                return header.Select((h, i) => new { h, v = values[i] }).ToDictionary(x => x.h, x => x.v);
            }).Where(x => x["HasAlert"] == "1").ToList();
        }

        private static void SaveQualityBatch(Dictionary<string, QualityDecision> changes, string expected)
        {
            if (changes.Count == 0) return;
            foreach (var item in changes)
                if (string.IsNullOrWhiteSpace(item.Value.Reason) || string.IsNullOrWhiteSpace(item.Value.Reviewer) || !new[] { "Pending Review", "Accepted", "Needs Correction" }.Contains(item.Value.Status))
                    throw new ArgumentException("Every changed review needs a decision, reason and reviewer.");
            CommitChange("Quality review batch | " + changes.Count + " decisions", delegate {
                foreach (var item in changes) SaveQualityDecision(item.Key, item.Value.Status, item.Value.Reason, item.Value.Reviewer);
            }, expected);
        }

        private sealed class QualityReviewForm : Form
        {
            private readonly DataGridView alerts = new DataGridView();
            private readonly TextBox details = new TextBox(), reason = new TextBox(), reviewer = new TextBox();
            private readonly ComboBox state = new ComboBox();
            private readonly Button save = new Button();
            private readonly Label status = new Label();
            private string revision;
            private readonly Dictionary<string, QualityDecision> drafts = new Dictionary<string, QualityDecision>();
            private Dictionary<string, string> editing;
            private bool loading;
            private bool busy;
            public QualityReviewForm()
            {
                Text = "HUMAIN | Quality reviews"; Size = new Size(1120, 720); MinimumSize = new Size(950, 650);
                StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 251);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 7, ColumnCount = 1 };
                foreach (float height in new float[] { 74, 220, 145, 42, 65, 42, 40 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                layout.AutoScroll = true; Controls.Add(layout);
                layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "QUALITY REVIEW\r\nAccept only verified alerts. Original flags remain visible.\r\nMissing / added tests remain forecast blockers until scope or export issues are corrected." });
                alerts.Dock = DockStyle.Fill; alerts.ReadOnly = true; alerts.AllowUserToAddRows = false; alerts.MultiSelect = false;
                alerts.SelectionMode = DataGridViewSelectionMode.FullRowSelect; alerts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; alerts.RowHeadersVisible = false;
                foreach (var h in new[] { "Snapshot", "Test", "Review status", "Forecast blocker", "Reviewer" }) alerts.Columns.Add(h, h);
                alerts.Columns.Add("Unsaved", "Unsaved"); alerts.Columns[1].FillWeight = 200; alerts.Columns[3].FillWeight = 180; layout.Controls.Add(alerts);
                alerts.Columns[3].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                alerts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
                details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; layout.Controls.Add(details);
                var inputs = new FlowLayoutPanel { Dock = DockStyle.Fill };
                inputs.Controls.Add(new Label { Text = "Decision:", AutoSize = true }); state.DropDownStyle = ComboBoxStyle.DropDownList; state.Width = 180;
                state.Items.AddRange(new object[] { "Pending Review", "Accepted", "Needs Correction" }); inputs.Controls.Add(state);
                inputs.Controls.Add(new Label { Text = "Reviewed by:", AutoSize = true }); reviewer.Width = 250; reviewer.AccessibleName = "Reviewer name"; inputs.Controls.Add(reviewer); layout.Controls.Add(inputs);
                var reasonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
                reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                reasonPanel.Controls.Add(new Label { Text = "Reason (required):", AutoSize = true }); reason.Dock = DockStyle.Fill; reason.Multiline = true; reason.AccessibleName = "Quality review reason"; reasonPanel.Controls.Add(reason); layout.Controls.Add(reasonPanel);
                save.Text = "Save all changes"; save.Dock = DockStyle.Right; save.Width = 230; layout.Controls.Add(save); status.Dock = DockStyle.Fill; layout.Controls.Add(status);
                state.SelectedIndexChanged += delegate { UpdateDraft(); }; reason.TextChanged += delegate { UpdateDraft(); }; reviewer.TextChanged += delegate { UpdateDraft(); };
                alerts.SelectionChanged += delegate { SelectAlert(); }; save.Click += delegate { Save(); };
                FormClosing += (s, e) => { if (busy) e.Cancel = true;
                    else if (drafts.Count > 0 && MessageBox.Show(this, "Discard unsaved review changes?", "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; };
                Shown += delegate { LoadAlerts(); };
            }
            private void LoadAlerts()
            {
                try
                {
                    using (AcquireProjectLock())
                    {
                        RecoverPending(); revision = SnapshotRevision(); loading = true; editing = null; alerts.Rows.Clear();
                        foreach (var a in ReadQualityAlerts())
                        { int i = alerts.Rows.Add(a["SnapshotDateTime"], a["TestName"], a["ReviewStatus"], QualityBlockerText(a), a["ReviewedBy"]); alerts.Rows[i].Tag = a; }
                    }
                    loading = false; SelectAlert(); UpdateSaveState();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Quality reviews"); save.Enabled = false; }
            }
            private Dictionary<string, string> Selected()
            { return alerts.SelectedRows.Count == 1 ? alerts.SelectedRows[0].Tag as Dictionary<string, string> : null; }
            private void UpdateSaveState()
            {
                save.Enabled = !busy && drafts.Count > 0;
                save.Text = drafts.Count == 0 ? "Save all changes" : "Save all changes (" + drafts.Count + ")";
                status.Text = drafts.Count + " unsaved reviews. Edit each alert, then save once.";
            }
            private void UpdateDraft()
            {
                if (loading || busy || editing == null) return;
                string id = editing["AlertId"];
                var draft = new QualityDecision { Status = state.SelectedItem as string, Reason = reason.Text, Reviewer = reviewer.Text };
                string originalReviewer = string.IsNullOrWhiteSpace(editing["ReviewedBy"]) ? Environment.UserName : editing["ReviewedBy"];
                if (draft.Status == editing["ReviewStatus"] && draft.Reason == editing["ReviewReason"] && draft.Reviewer == originalReviewer) drafts.Remove(id);
                else drafts[id] = draft;
                foreach (DataGridViewRow row in alerts.Rows)
                {
                    var a = row.Tag as Dictionary<string,string>;
                    if (a != null && a["AlertId"] == id) { row.Cells[2].Value = draft.Status; row.Cells[4].Value = draft.Reviewer; row.Cells[5].Value = drafts.ContainsKey(id) ? "Yes" : ""; }
                }
                UpdateSaveState();
            }
            private void SelectAlert()
            {
                if (loading) return;
                editing = Selected(); var a = editing; loading = true;
                try
                {
                    if (a == null) { details.Text = "Select an alert."; return; }
                    details.Text = "Forecast blocker: " + QualityBlockerText(a) + "\r\n" + ((a["RequiresAction"] == "1" && a["ReviewStatus"] == "Accepted") ? "Your review is saved. Added / missing test scope still needs correction; acceptance does not remove test records.\r\n" : "") + a["Reason"] + "\r\nPrevious records: " + a["PreviousTotal"] + " | Current records: " + a["CurrentTotal"] +
                        "\r\nLast saved review: " + a["ReviewedAt"] + "\r\nSaved reason: " + a["ReviewReason"];
                    QualityDecision draft;
                    if (drafts.TryGetValue(a["AlertId"], out draft)) { state.SelectedItem = draft.Status; reason.Text = draft.Reason; reviewer.Text = draft.Reviewer; }
                    else { state.SelectedItem = a["ReviewStatus"]; reason.Text = a["ReviewReason"]; reviewer.Text = string.IsNullOrWhiteSpace(a["ReviewedBy"]) ? Environment.UserName : a["ReviewedBy"]; }
                }
                finally { loading = false; UpdateSaveState(); }
            }
            private async void Save()
            {
                if (busy || drafts.Count == 0) return;
                var changes = drafts.ToDictionary(x => x.Key, x => new QualityDecision { Status = x.Value.Status, Reason = x.Value.Reason, Reviewer = x.Value.Reviewer });
                var invalid = changes.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Value.Reason) || string.IsNullOrWhiteSpace(x.Value.Reviewer) || string.IsNullOrWhiteSpace(x.Value.Status));
                if (invalid.Key != null)
                {
                    foreach (DataGridViewRow row in alerts.Rows) { var a = row.Tag as Dictionary<string,string>; if (a != null && a["AlertId"] == invalid.Key) { alerts.CurrentCell = row.Cells[0]; break; } }
                    MessageBox.Show(this, "Every changed review needs a decision, reason and reviewer. Complete the selected review, then save again.", "Quality reviews"); return;
                }
                busy = true; alerts.Enabled = save.Enabled = state.Enabled = reason.Enabled = reviewer.Enabled = false; status.Text = "Saving all reviews and rebuilding once...";
                bool saved = false;
                try
                {
                    string expected = revision;
                    await Task.Run(() => SaveQualityBatch(changes, expected));
                    drafts.Clear(); LoadAlerts(); saved = true;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message + "\r\nYour unsaved changes are kept in this window. If project data changed, reopen reviews and review the latest alerts.", "Quality reviews"); }
                finally { busy = false; alerts.Enabled = state.Enabled = reason.Enabled = reviewer.Enabled = true; UpdateSaveState(); if (saved) status.Text = "All reviews saved. Refresh Power BI."; }
            }

        }

        private static void RunQualityReviewTests()
        {
            string original = AppRoot; int count = 0;
            Action<bool, string> check = (ok, name) => { if (!ok) throw new Exception("FAILED review: " + name); count++; };
            try
            {
                SetRoot(Path.Combine(Path.GetTempPath(), "HUMAIN-review-tests-" + Guid.NewGuid().ToString("N"))); EnsureFolders();
                var metric = new QualityMetric { HasAlert = 1, LargeDrop = 1 };
                var pending = new QualityDecision(); check(RequiresQualityAction(metric, pending) == 1, "pending blocks forecast");
                var accepted = new QualityDecision { Status = "Accepted" }; check(RequiresQualityAction(metric, accepted) == 0, "accepted drop releases gate");
                accepted.Status = "Needs Correction"; check(RequiresQualityAction(metric, accepted) == 1, "correction blocks");
                accepted.Status = "Accepted"; metric.TestMissing = 1; check(RequiresQualityAction(metric, accepted) == 1, "accepted missing scope still blocks");
                metric.TestMissing = 0; metric.TestAdded = 1; check(RequiresQualityAction(metric, accepted) == 1, "accepted added scope still blocks");
                metric.HasAlert = 0; check(RequiresQualityAction(metric, pending) == 0, "no flag no block");
                var first = new SnapshotData { Date = new DateTime(2026, 1, 1), Tests = new List<string> { "test" }, Records = new List<ClashRecord> { new ClashRecord { ClashGuid = "a", TestName = "test", Status = "active" } } };
                var second = new SnapshotData { Date = first.Date.AddDays(1), Tests = first.Tests, Records = new List<ClashRecord>() };
                string id = QualityAlertId(first, second, "test");
                SaveQualityDecision(id, "Accepted", "Verified compact", "Tester");
                check(ReadQualityDecision(id).Status == "Accepted", "decision persisted");
                check(ReadQualityDecision(id).Reviewer == "Tester" && ReadQualityDecision(id).ReviewedAt.Length > 0, "review attribution");
                check(QualityAlertId(first, second, "test") == id, "unchanged rebuild preserves identity");
                first.Records[0].Status = "resolved";
                string changed = QualityAlertId(first, second, "test"); check(changed != id && ReadQualityDecision(changed).Status == "Pending Review", "changed interval requires new review");
                try { SaveQualityDecision(id, "Accepted", "", "Tester"); check(false, "reject blank reason"); } catch (ArgumentException) { count++; }
                SaveQualityDecision(id, "Pending Review", "Reconsider", "Tester");
                check(File.ReadAllLines(Path.Combine(AppRoot, "QualityReviews", "history.jsonl")).Length == 2, "history retains earlier decision");
                string rev = SnapshotRevision(); SaveQualityDecision(id, "Needs Correction", "Bad export", "Tester");
                check(SnapshotRevision() != rev, "review changes invalidate stale preview");
                CommitChange("rebuild preserves reviews", delegate { }); check(ReadQualityDecision(id).Status == "Needs Correction", "staged rebuild preserves review store");
                CommitChange("seed review interval", delegate {
                    for (int day = 0; day < 2; day++)
                    {
                        DateTime date = new DateTime(2026, 2, 1).AddDays(day);
                        var snapshot = new SnapshotData { Date = date, Tests = new List<string> { "test" }, Records = Enumerable.Range(0, day == 0 ? 100 : 0)
                            .Select(i => new ClashRecord { ClashGuid = "review-" + i, TestName = "test", Status = "active", SnapshotDate = date }).ToList() };
                        SaveSnapshot(new SnapshotChoice { Timestamp = date, FolderPath = Path.Combine(SnapshotsRoot, SnapshotFolderName(date)) }, snapshot);
                    }
                });
                var alert = ReadQualityAlerts().Single();
                check(alert["ReviewStatus"] == "Pending Review" && alert["RequiresAction"] == "1", "new exported alert blocks");
                string operational = File.ReadAllText(Path.Combine(PowerBiRoot, "OperationalProgress.csv"));
                CommitChange("accept test alert", () => SaveQualityDecision(alert["AlertId"], "Accepted", "Verified full export", "Tester"));
                alert = ReadQualityAlerts().Single(); check(alert["HasAlert"] == "1" && alert["RequiresAction"] == "0" && alert["ReviewStatus"] == "Accepted", "accepted export retains original flag and clears blocker");
                check(File.ReadAllText(Path.Combine(PowerBiRoot, "OperationalProgress.csv")) == operational, "acceptance never changes business counts");
                ManagementFault = point => { if (point == "published-PowerBI") throw new IOException("review rollback test"); };
                try { CommitChange("review rollback", () => SaveQualityDecision(alert["AlertId"], "Needs Correction", "Failure test", "Tester")); } catch (IOException) { }
                ManagementFault = null;
                check(ReadQualityAlerts().Single()["RequiresAction"] == "0" && ReadQualityDecision(alert["AlertId"]).Status == "Accepted", "review and exports roll back together");
                var batch = new Dictionary<string, QualityDecision> {
                    { id, new QualityDecision { Status = "Accepted", Reason = "Batch first", Reviewer = "Tester" } },
                    { alert["AlertId"], new QualityDecision { Status = "Needs Correction", Reason = "Batch second", Reviewer = "Tester" } }
                };
                int backups = Directory.GetDirectories(Path.Combine(AppRoot, "Backups")).Length;
                SaveQualityBatch(batch, SnapshotRevision());
                check(ReadQualityDecision(id).Reason == "Batch first" && ReadQualityDecision(alert["AlertId"]).Reason == "Batch second", "batch saves distinct decisions");
                check(Directory.GetDirectories(Path.Combine(AppRoot, "Backups")).Length == backups + 1, "batch uses one backup and transaction");
                batch[id].Reason = ""; string unchanged = SnapshotRevision();
                try { SaveQualityBatch(batch, unchanged); check(false, "invalid batch must fail"); } catch (ArgumentException) { count++; }
                check(unchanged == SnapshotRevision(), "invalid batch writes nothing");
                batch[id].Reason = "Rollback first"; batch[alert["AlertId"]].Reason = "Rollback second";
                ManagementFault = point => { if (point == "published-PowerBI") throw new IOException("batch failure"); };
                try { SaveQualityBatch(batch, SnapshotRevision()); } catch (IOException) { }
                ManagementFault = null;
                check(ReadQualityDecision(id).Reason == "Batch first" && ReadQualityDecision(alert["AlertId"]).Reason == "Batch second", "batch rollback preserves both decisions");
                Console.WriteLine("PASS: " + count + " quality review assertions.");
            }
            finally { ManagementFault = null; SetRoot(original); }
        }
    }
}
