using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HUMAIN.CoordinationTracker
{
    internal partial class Program
    {
        private static string ProjectSummary()
        {
            var refs = GetSnapshotRefs();
            if (refs.Count == 0) return "No active snapshots. Power BI will have no current records.";
            var latest = LoadSnapshot(refs.Last());
            int approved = latest.Records.Count(x => string.Equals(x.Status, "approved", StringComparison.OrdinalIgnoreCase));
            int resolved = latest.Records.Count(x => string.Equals(x.Status, "resolved", StringComparison.OrdinalIgnoreCase));
            string movements = "";
            string op = Path.Combine(PowerBiRoot, "OperationalProgress.csv");
            if (File.Exists(op))
            {
                var lines = File.ReadAllLines(op); var header = ParseCsvLine(lines[0]);
                foreach (string field in new[] { "NewActionable", "ReturnedActionable", "ResolvedFromActionable", "ApprovedFromActionable", "NetActionableReduction" })
                {
                    int index = header.IndexOf(field); long sum = 0;
                    if (index >= 0) foreach (string line in lines.Skip(1)) { var values = ParseCsvLine(line); long n; if (values.Count > index && long.TryParse(values[index], out n)) sum += n; }
                    movements += "\r\nAll-history " + field + ": " + sum.ToString("N0");
                }
            }
            return "Active snapshots: " + refs.Count + "\r\nBaseline: " + refs.First().Timestamp.ToString("yyyy-MM-dd HH:mm:ss") +
                "\r\nLatest: " + refs.Last().Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + "\r\nTotal current: " + latest.Records.Count.ToString("N0") +
                " | Actionable: " + (latest.Records.Count - approved - resolved).ToString("N0") + " | Approved: " + approved.ToString("N0") + " | Resolved retained: " + resolved.ToString("N0") + movements;
        }

        private static string PreviewChange(Action action)
        {
            using (AcquireProjectLock())
            {
                RecoverPending();
                string before = ProjectSummary(), root = AppRoot;
                string stage = Path.Combine(root, "Transactions", "preview-" + Guid.NewGuid().ToString("N"));
                CopyTree(SnapshotsRoot, Path.Combine(stage, "Snapshots"));
                CopyTree(Path.Combine(AppRoot, "QualityReviews"), Path.Combine(stage, "QualityReviews"));
                try
                {
                    SetRoot(stage); EnsureFolders(); action(); ValidateSnapshots(); RebuildDerivedFiles();
                    return "BEFORE\r\n" + before + "\r\n\r\nAFTER\r\n" + ProjectSummary() +
                        "\r\n\r\nAll historical movements, ages and forecasts are recalculated. Refresh Power BI only after completion.";
                }
                finally { SetRoot(root); CleanTransaction(stage, root); }
            }
        }

        private sealed class ManagerForm : Form
        {
            private readonly DataGridView grid = new DataGridView();
            private readonly TextBox folder = new TextBox(), reason = new TextBox(), preview = new TextBox();
            private readonly DateTimePicker timestamp = new DateTimePicker();
            private readonly CheckBox replace = new CheckBox();
            private readonly Label status = new Label();
            private readonly Button apply = new Button();
            private readonly FlowLayoutPanel actions = new FlowLayoutPanel(), import = new FlowLayoutPanel();
            private Action pending;
            private string pendingLabel, revision;
            private bool busy;

            public ManagerForm()
            {
                Text = "HUMAIN Coordination Tracker | Snapshot Manager";
                Size = new Size(1180, 850); MinimumSize = new Size(1050, 730); StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 251);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                Controls.Add(layout);
                layout.Controls.Add(new Label { Text = "HUMAIN  /  SNAPSHOT MANAGER\r\nImport, review and manage coordination history. Changes are backed up automatically.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(23, 43, 69), Font = new Font("Segoe UI", 12, FontStyle.Bold) });
                import.Dock = DockStyle.Fill; import.WrapContents = true;
                folder.Width = 440; folder.AccessibleName = "XML export folder";
                var browse = MakeButton("Browse XML folder", delegate {
                    using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
                });
                timestamp.Format = DateTimePickerFormat.Custom; timestamp.CustomFormat = "yyyy-MM-dd HH:mm:ss"; timestamp.Width = 205;
                timestamp.AccessibleName = "Actual snapshot date and time";
                replace.Text = "Replace selected snapshot"; replace.AutoSize = true;
                import.Controls.AddRange(new Control[] { folder, browse, timestamp, replace, MakeButton("Preview import", () => PrepareImport()) });
                layout.Controls.Add(import);
                grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
                grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                grid.BackgroundColor = Color.White; grid.RowHeadersVisible = false;
                grid.Columns.Add("time", "Snapshot date / time"); grid.Columns.Add("state", "State"); grid.Columns.Add("tests", "Tests"); grid.Columns.Add("records", "Records"); grid.Columns.Add("note", "Reason / note");
                layout.Controls.Add(grid);
                actions.Dock = DockStyle.Fill;
                actions.Controls.AddRange(new Control[] {
                    MakeButton("Quality reviews", () => { InvalidatePreview(); using (var dialog = new QualityReviewForm()) dialog.ShowDialog(this); }), MakeButton("Reload list", () => Reload()), MakeButton("Preview exclusion", () => PrepareExclusion(true)),
                    MakeButton("Preview restore", () => PrepareExclusion(false)), MakeButton("Preview rebuild", () => Prepare("Rebuild outputs", delegate { }, "Rebuild the derived CSVs from active snapshots.")),
                    MakeButton("Backup now", () => BackupNow()), MakeButton("Open backups", () => OpenFolder(Path.Combine(AppRoot, "Backups"))),
                    MakeButton("Open CSV folder", () => OpenFolder(PowerBiRoot)) });
                layout.Controls.Add(actions);
                reason.Dock = DockStyle.Fill; reason.AccessibleName = "Reason for snapshot change";
                var reasonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                reasonPanel.Controls.Add(new Label { Text = "Reason for change:", AutoSize = true }); reasonPanel.Controls.Add(reason); layout.Controls.Add(reasonPanel);
                preview.Multiline = true; preview.ReadOnly = true; preview.ScrollBars = ScrollBars.Both; preview.Dock = DockStyle.Fill; preview.BackColor = Color.White; preview.Font = new Font("Consolas", 10);
                preview.AccessibleName = "Change preview"; layout.Controls.Add(preview);
                var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
                status.Dock = DockStyle.Fill; status.Text = "Ready. Select a snapshot or choose an XML folder.";
                apply.Text = "Approve && apply preview"; apply.Dock = DockStyle.Fill; apply.Enabled = false; apply.BackColor = Color.FromArgb(8, 127, 117); apply.ForeColor = Color.White; apply.FlatStyle = FlatStyle.Flat;
                apply.Click += delegate { ApplyPending(); }; footer.Controls.Add(status); footer.Controls.Add(apply); layout.Controls.Add(footer);
                folder.TextChanged += delegate { InvalidatePreview(); }; timestamp.ValueChanged += delegate { InvalidatePreview(); };
                replace.CheckedChanged += delegate { InvalidatePreview(); }; reason.TextChanged += delegate { InvalidatePreview(); }; grid.SelectionChanged += delegate { InvalidatePreview(); };
                FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = "Please wait for the current operation to finish."; } };
                Shown += delegate { Reload(); };
            }

            private Button MakeButton(string text, Action action)
            {
                var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.System };
                b.Click += delegate { try { action(); } catch (Exception ex) { ShowError(ex); } }; return b;
            }
            private void ShowError(Exception ex) { status.Text = "Operation stopped; review the message."; MessageBox.Show(this, ex.Message, "HUMAIN Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            private void InvalidatePreview() { pending = null; apply.Enabled = false; }
            private string SelectedFolder()
            {
                if (grid.SelectedRows.Count != 1) throw new InvalidOperationException("Select one snapshot first.");
                return (string)grid.SelectedRows[0].Tag;
            }
            private void SetBusy(bool value)
            {
                busy = value; import.Enabled = actions.Enabled = grid.Enabled = reason.Enabled = !value; apply.Enabled = !value && pending != null; UseWaitCursor = value;
            }
            private async void Reload()
            {
                if (busy) return; InvalidatePreview(); SetBusy(true);
                try
                {
                    var rows = await Task.Run(() => {
                        using (AcquireProjectLock()) { RecoverPending(); return GetSnapshotRefs(true).Select(s => {
                            var data = LoadSnapshot(s); string marker = Path.Combine(s.FolderPath, "excluded.txt");
                            return new string[] { Path.GetFileName(s.FolderPath), s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), File.Exists(marker) ? "Excluded" : "Active", data.Tests.Count.ToString("N0"), data.Records.Count.ToString("N0"), File.Exists(marker) ? File.ReadAllText(marker).Replace(Environment.NewLine, " / ") : "" };
                        }).ToList(); }
                    });
                    grid.Rows.Clear(); foreach (var r in rows) { int i = grid.Rows.Add(r.Skip(1).ToArray()); grid.Rows[i].Tag = r[0]; }
                    preview.Text = ProjectSummary(); status.Text = "Ready | " + AppRoot;
                }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
            }
            private async void PrepareImport()
            {
                if (busy) return;
                try {
                string source = folder.Text.Trim().Trim('"'); if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Choose the XML export folder.");
                string selected = replace.Checked ? SelectedFolder() : null;
                DateTime date = timestamp.Value; date = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second);
                if (selected != null) date = GetSnapshotRefs(true).Single(x => Path.GetFileName(x.FolderPath) == selected).Timestamp;
                string name = selected ?? SnapshotFolderName(date);
                if (selected == null && Directory.Exists(Path.Combine(SnapshotsRoot, name))) throw new InvalidOperationException("That timestamp already exists. Choose another time or replace the selected snapshot.");
                InvalidatePreview(); SetBusy(true); status.Text = "Reading XML and checking the complete history...";
                try
                {
                    string note = reason.Text.Trim();
                    if (selected != null && note.Length == 0) throw new ArgumentException("Enter a reason for replacing this snapshot.");
                    var imported = await Task.Run(() => ImportSnapshot(source, date));
                    if (imported.Tests.Count == 0) throw new InvalidDataException("No clash tests found in this export.");
                    Action change = delegate {
                        string target = Path.Combine(SnapshotsRoot, name);
                        SaveSnapshot(new SnapshotChoice { Timestamp = date, FolderPath = target, IsOverwrite = selected != null }, imported);
                    };
                    revision = SnapshotRevision();
                    string intro = MessagePreview(imported, date, selected);
                    string report = await Task.Run(() => PreviewChange(change));
                    preview.Text = intro + "\r\n" + report;
                    pending = change; pendingLabel = (selected == null ? "Import " : "Replace ") + name + " | " + note;
                    status.Text = "Preview ready. Review the alerts and before/after results, then approve.";
                }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
                } catch (Exception ex) { ShowError(ex); }
            }
            private void PrepareExclusion(bool exclude)
            {
                string name = SelectedFolder(), note = reason.Text.Trim();
                if (note.Length == 0) throw new ArgumentException("Enter a reason for this change.");
                bool isExcluded = File.Exists(Path.Combine(SnapshotsRoot, name, "excluded.txt"));
                if (exclude == isExcluded) throw new InvalidOperationException(exclude ? "This snapshot is already excluded." : "This snapshot is already active.");
                Prepare((exclude ? "Exclude " : "Restore ") + name + " | " + note, () => ChangeExclusion(name, exclude, note),
                    "Original snapshot files are retained. Excluding a baseline changes the baseline; excluding the latest snapshot moves the current state back.");
            }
            private async void Prepare(string label, Action action, string intro)
            {
                if (busy) return; InvalidatePreview(); SetBusy(true); status.Text = "Calculating preview...";
                try { revision = SnapshotRevision(); preview.Text = intro + "\r\n\r\n" + await Task.Run(() => PreviewChange(action)); pending = action; pendingLabel = label; status.Text = "Preview ready. No live data changed."; }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
            }
            private async void ApplyPending()
            {
                if (pending == null || busy) return;
                var action = pending; string label = pendingLabel, expected = revision; SetBusy(true); status.Text = "Backing up and applying... Do not refresh Power BI yet.";
                try { await Task.Run(() => CommitChange(label, action, expected)); pending = null; preview.Text += "\r\n\r\nAPPLIED SUCCESSFULLY. Backup saved. Refresh Power BI now."; status.Text = "Completed. Refresh Power BI; reload the list to see updated states."; }
                catch (Exception ex) { pending = null; ShowError(ex); } finally { SetBusy(false); }
            }
            private async void BackupNow()
            {
                if (busy) return; SetBusy(true);
                try { string dest = await Task.Run(() => { using (AcquireProjectLock()) { RecoverPending(); return BackupProject("Manual backup"); } }); preview.Text = "BACKUP SAVED\r\n" + dest; status.Text = "Backup completed."; }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
            }
            private void OpenFolder(string path) { Directory.CreateDirectory(path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        }
    }
}
