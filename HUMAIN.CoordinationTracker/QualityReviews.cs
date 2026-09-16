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

        private sealed class QualityReviewForm : Form
        {
            private readonly DataGridView alerts = new DataGridView();
            private readonly TextBox details = new TextBox(), reason = new TextBox(), reviewer = new TextBox();
            private readonly ComboBox state = new ComboBox();
            private readonly Button save = new Button();
            private readonly Label status = new Label();
            private string revision;
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
                alerts.Columns[1].FillWeight = 200; layout.Controls.Add(alerts);
                details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; layout.Controls.Add(details);
                var inputs = new FlowLayoutPanel { Dock = DockStyle.Fill };
                inputs.Controls.Add(new Label { Text = "Decision:", AutoSize = true }); state.DropDownStyle = ComboBoxStyle.DropDownList; state.Width = 180;
                state.Items.AddRange(new object[] { "Pending Review", "Accepted", "Needs Correction" }); inputs.Controls.Add(state);
                inputs.Controls.Add(new Label { Text = "Reviewed by:", AutoSize = true }); reviewer.Width = 250; reviewer.AccessibleName = "Reviewer name"; inputs.Controls.Add(reviewer); layout.Controls.Add(inputs);
                var reasonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
                reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                reasonPanel.Controls.Add(new Label { Text = "Reason (required):", AutoSize = true }); reason.Dock = DockStyle.Fill; reason.Multiline = true; reason.AccessibleName = "Quality review reason"; reasonPanel.Controls.Add(reason); layout.Controls.Add(reasonPanel);
                save.Text = "Save review and rebuild"; save.Dock = DockStyle.Right; save.Width = 230; layout.Controls.Add(save); status.Dock = DockStyle.Fill; layout.Controls.Add(status);
                alerts.SelectionChanged += delegate { SelectAlert(); }; save.Click += delegate { Save(); };
                FormClosing += (s, e) => { if (busy) e.Cancel = true; };
                Shown += delegate { LoadAlerts(); };
            }
            private void LoadAlerts()
            {
                try
                {
                    using (AcquireProjectLock())
                    {
                        RecoverPending(); revision = SnapshotRevision(); alerts.Rows.Clear();
                        foreach (var a in ReadQualityAlerts())
                        { int i = alerts.Rows.Add(a["SnapshotDateTime"], a["TestName"], a["ReviewStatus"], a["RequiresAction"] == "1" ? "Yes" : "No", a["ReviewedBy"]); alerts.Rows[i].Tag = a; }
                    }
                    SelectAlert(); status.Text = alerts.Rows.Count + " original alerts. Review decisions do not change clash counts.";
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Quality reviews"); save.Enabled = false; }
            }
            private Dictionary<string, string> Selected()
            { return alerts.SelectedRows.Count == 1 ? alerts.SelectedRows[0].Tag as Dictionary<string, string> : null; }
            private void SelectAlert()
            {
                var a = Selected(); save.Enabled = a != null && !busy; if (a == null) { details.Text = "Select an alert."; return; }
                details.Text = a["Reason"] + "\r\nPrevious records: " + a["PreviousTotal"] + " | Current records: " + a["CurrentTotal"] +
                    "\r\nLast review: " + a["ReviewedAt"] + "\r\nReason: " + a["ReviewReason"];
                state.SelectedItem = a["ReviewStatus"]; reason.Text = a["ReviewReason"]; reviewer.Text = string.IsNullOrWhiteSpace(a["ReviewedBy"]) ? Environment.UserName : a["ReviewedBy"];
            }
            private async void Save()
            {
                var a = Selected(); if (a == null || busy) return;
                string decision = state.SelectedItem as string, note = reason.Text.Trim(), by = reviewer.Text.Trim();
                if (note.Length == 0 || by.Length == 0) { MessageBox.Show(this, "Enter the reason and reviewer name.", "Quality reviews"); return; }
                busy = true; alerts.Enabled = save.Enabled = state.Enabled = reason.Enabled = reviewer.Enabled = false; status.Text = "Saving review, backup and updated exports...";
                try
                {
                    string id = a["AlertId"], expected = revision;
                    await Task.Run(() => CommitChange("Quality review " + id + " | " + decision + " | " + by + " | " + note,
                        () => SaveQualityDecision(id, decision, note, by), expected));
                    LoadAlerts(); status.Text = "Saved. Refresh Power BI. Forecast still requires 3 dates and positive net burn.";
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Quality reviews"); LoadAlerts(); }
                finally { busy = false; alerts.Enabled = state.Enabled = reason.Enabled = reviewer.Enabled = true; save.Enabled = Selected() != null; }
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
                Console.WriteLine("PASS: " + count + " quality review assertions.");
            }
            finally { ManagementFault = null; SetRoot(original); }
        }
    }
}
