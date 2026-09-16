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
                layout.Controls.Add(new Label { Text = "HUMAIN  /  SNAPSHOT MANAGER\r\nاختر العملية المطلوبة. ستظهر نتيجة التغيير قبل التأكيد، مع حفظ نسخة احتياطية تلقائيًا.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(23, 43, 69), Font = new Font("Segoe UI", 12, FontStyle.Bold) });
                import.Dock = DockStyle.Fill; import.WrapContents = true;
                folder.Width = 440; folder.AccessibleName = "XML export folder";
                var browse = MakeButton("اختيار ملفات XML", delegate {
                    using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
                });
                timestamp.Format = DateTimePickerFormat.Custom; timestamp.CustomFormat = "yyyy-MM-dd HH:mm:ss"; timestamp.Width = 205;
                timestamp.AccessibleName = "Actual snapshot date and time";
                replace.Text = "استبدال اللقطة المحددة"; replace.AutoSize = true;
                import.Controls.AddRange(new Control[] { folder, browse, timestamp, replace, MakeButton("استيراد لقطة", () => PrepareImport()) });
                layout.Controls.Add(import);
                grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
                grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                grid.BackgroundColor = Color.White; grid.RowHeadersVisible = false;
                grid.Columns.Add("time", "Snapshot date / time"); grid.Columns.Add("state", "State"); grid.Columns.Add("tests", "Tests"); grid.Columns.Add("records", "Records"); grid.Columns.Add("note", "Reason / note");
                layout.Controls.Add(grid);
                actions.Dock = DockStyle.Fill;
                actions.Controls.AddRange(new Control[] {
                    MakeButton("مراجعة الجودة", () => { InvalidatePreview(); using (var dialog = new QualityReviewForm()) dialog.ShowDialog(this); }), MakeButton("تحديث القائمة", () => Reload()), MakeButton("استبعاد اللقطة", () => PrepareExclusion(true)),
                    MakeButton("استعادة اللقطة", () => PrepareExclusion(false)), MakeButton("تحديث بيانات الداشبورد", () => Prepare("Rebuild outputs", delegate { }, "سيتم تحديث بيانات الداشبورد من اللقطات النشطة.")),
                    MakeButton("حفظ نسخة احتياطية", () => BackupNow()), MakeButton("النسخ الاحتياطية", () => OpenFolder(Path.Combine(AppRoot, "Backups"))),
                    MakeButton("ملفات الداشبورد", () => OpenFolder(PowerBiRoot)) });
                layout.Controls.Add(actions);
                reason.Dock = DockStyle.Fill; reason.AccessibleName = "Reason for snapshot change";
                var reasonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); reasonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                reasonPanel.Controls.Add(new Label { Text = "سبب التغيير:", AutoSize = true }); reasonPanel.Controls.Add(reason); layout.Controls.Add(reasonPanel);
                preview.Multiline = true; preview.ReadOnly = true; preview.ScrollBars = ScrollBars.Both; preview.Dock = DockStyle.Fill; preview.BackColor = Color.White; preview.Font = new Font("Consolas", 10);
                preview.AccessibleName = "نتيجة التغيير"; layout.Controls.Add(preview);
                var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
                status.Dock = DockStyle.Fill; status.Text = "اختر لقطة من القائمة أو اختر ملفات XML لاستيراد لقطة جديدة.";
                apply.Text = "تأكيد التغيير"; apply.Dock = DockStyle.Fill; apply.Enabled = false; apply.BackColor = Color.FromArgb(8, 127, 117); apply.ForeColor = Color.White; apply.FlatStyle = FlatStyle.Flat;
                apply.Click += delegate { ApplyPending(); }; footer.ColumnStyles.Clear(); footer.ColumnCount = 1; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.Controls.Add(status); layout.Controls.Add(footer);
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
                if (grid.SelectedRows.Count != 1) throw new InvalidOperationException("اختر لقطة من القائمة أولًا.");
                return (string)grid.SelectedRows[0].Tag;
            }
            private void SetBusy(bool value)
            {
                busy = value; import.Enabled = actions.Enabled = grid.Enabled = reason.Enabled = !value; apply.Enabled = !value && pending != null; UseWaitCursor = value;
            }
            private async void Reload(string completion = null)
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
                    preview.Text = FriendlyReport(ProjectSummary()); status.Text = completion ?? "جاهز | " + AppRoot;
                }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
            }
            private async void PrepareImport()
            {
                if (busy) return;
                try {
                string source = folder.Text.Trim().Trim('"'); if (!Directory.Exists(source)) throw new DirectoryNotFoundException("اختر مجلد ملفات XML أولًا.");
                string selected = replace.Checked ? SelectedFolder() : null;
                DateTime date = timestamp.Value; date = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second);
                if (selected != null) date = GetSnapshotRefs(true).Single(x => Path.GetFileName(x.FolderPath) == selected).Timestamp;
                string name = selected ?? SnapshotFolderName(date);
                if (selected == null && Directory.Exists(Path.Combine(SnapshotsRoot, name))) throw new InvalidOperationException("That timestamp already exists. Choose another time or replace the selected snapshot.");
                InvalidatePreview(); SetBusy(true); status.Text = "جارٍ قراءة الملفات وفحص البيانات...";
                try
                {
                    string note = reason.Text.Trim();
                    if (selected != null && note.Length == 0) throw new ArgumentException("اكتب سبب استبدال اللقطة أولًا.");
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
                    status.Text = "راجع نتيجة العملية ثم أكّد التنفيذ.";
                }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
                ConfirmPending();
                } catch (Exception ex) { ShowError(ex); }
            }
            private void PrepareExclusion(bool exclude)
            {
                string name = SelectedFolder(), note = reason.Text.Trim();
                if (note.Length == 0) throw new ArgumentException("اكتب سبب التغيير أولًا.");
                bool isExcluded = File.Exists(Path.Combine(SnapshotsRoot, name, "excluded.txt"));
                if (exclude == isExcluded) throw new InvalidOperationException(exclude ? "This snapshot is already excluded." : "This snapshot is already active.");
                Prepare((exclude ? "Exclude " : "Restore ") + name + " | " + note, () => ChangeExclusion(name, exclude, note),
                    "ملفات اللقطة ستظل محفوظة. استبعاد أول لقطة يغير نقطة البداية، واستبعاد آخر لقطة يرجع الرصيد إلى اللقطة السابقة.");
            }
            private async void Prepare(string label, Action action, string intro)
            {
                if (busy) return; InvalidatePreview(); SetBusy(true); status.Text = "جارٍ حساب نتيجة التغيير...";
                try { revision = SnapshotRevision(); preview.Text = intro + "\r\n\r\n" + await Task.Run(() => PreviewChange(action)); pending = action; pendingLabel = label; status.Text = "نتيجة العملية جاهزة للمراجعة. لم يتم تغيير البيانات بعد."; }
                catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); }
                ConfirmPending();
            }
            private static string FriendlyReport(string value)
            {
                return value.Replace("BEFORE", "قبل التغيير").Replace("AFTER", "بعد التغيير").Replace("Active snapshots:", "عدد اللقطات النشطة:").Replace("Baseline:", "لقطة البداية:").Replace("Latest:", "آخر لقطة:").Replace("Total current:", "إجمالي السجلات:").Replace("Approved:", "المقبول:").Replace("Resolved retained:", "المحلول المحتفظ به:").Replace("All-history NewActionable:", "الجديد خلال التاريخ:").Replace("All-history ReturnedActionable:", "العائد للعمل:").Replace("All-history ResolvedFromActionable:", "المحلول من المفتوح:").Replace("All-history ApprovedFromActionable:", "المقبول من المفتوح:").Replace("All-history NetActionableReduction:", "صافي انخفاض العمل:").Replace("Actionable:", "المطلوب حله:").Replace("All historical movements, ages and forecasts are recalculated. Refresh Power BI only after completion.", "ستُحدّث الحركات والأعمار والتوقعات. حدّث Power BI بعد اكتمال العملية.");
            }
            private void ConfirmPending()
            {
                if (pending == null || busy) return;
                string action = pendingLabel.StartsWith("Exclude ") ? "تأكيد الاستبعاد" : pendingLabel.StartsWith("Restore ") ? "تأكيد الاستعادة" : pendingLabel.StartsWith("Import ") ? "تأكيد الاستيراد" : pendingLabel.StartsWith("Replace ") ? "تأكيد الاستبدال" : "تأكيد تحديث البيانات";
                using (var dialog = new Form { Text = "مراجعة التغيير", Size = new Size(900, 690), MinimumSize = new Size(700, 500), StartPosition = FormStartPosition.CenterParent, Font = Font })
                {
                    var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
                    var confirm = new Button { Text = action, AutoSize = true, Height = 34, DialogResult = DialogResult.OK };
                    var cancel = new Button { Text = "رجوع بدون تغيير", AutoSize = true, Height = 34, DialogResult = DialogResult.Cancel };
                    buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
                    var report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Segoe UI", 11), Text = FriendlyReport(preview.Text) };
                    var heading = new Label { Dock = DockStyle.Top, Height = 46, Text = "راجع الأرقام قبل وبعد. لن يتم تنفيذ العملية إلا عند التأكيد.", TextAlign = ContentAlignment.MiddleRight };
                    dialog.Controls.Add(report); dialog.Controls.Add(heading); dialog.Controls.Add(buttons); dialog.CancelButton = cancel; dialog.Shown += delegate { report.SelectionStart = 0; report.SelectionLength = 0; cancel.Focus(); };
                    if (dialog.ShowDialog(this) == DialogResult.OK) ApplyPending();
                    else { InvalidatePreview(); status.Text = "لم يتم تنفيذ العملية. يمكنك اختيار عملية أخرى."; }
                }
            }
            private async void ApplyPending()
            {
                if (pending == null || busy) return;
                bool saved = false; var action = pending; string label = pendingLabel, expected = revision; SetBusy(true); status.Text = "جارٍ الحفظ وتحديث البيانات. انتظر قبل تحديث Power BI.";
                try { await Task.Run(() => CommitChange(label, action, expected)); pending = null; preview.Text += "\r\n\r\nAPPLIED SUCCESSFULLY. Backup saved. Refresh Power BI now."; saved = true; status.Text = "تم الحفظ. يمكنك الآن تحديث Power BI."; }
                catch (Exception ex) { pending = null; ShowError(ex); } finally { SetBusy(false); }
                if (saved) Reload("تم الحفظ وتحديث القائمة. يمكنك الآن تحديث Power BI.");
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
