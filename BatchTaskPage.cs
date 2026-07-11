using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class BatchTaskPage : UserControl
    {
        private readonly BatchTaskCenter _center;
        private readonly DataGridView _grid = new DataGridView();
        private readonly ThemedProgressBar _progress = new ThemedProgressBar();
        private readonly Label _summary = new Label();
        private readonly ModernButton _pauseButton = new ModernButton();
        private readonly ModernButton _cancelButton = new ModernButton();
        private readonly ModernButton _resumeButton = new ModernButton();
        private readonly ModernButton _retryButton = new ModernButton();
        private readonly ModernButton _clearButton = new ModernButton();
        private readonly ModernButton _reportButton = new ModernButton();
        private readonly Label _emptyState = new Label();

        public BatchTaskPage(BatchTaskCenter center)
        {
            _center = center ?? throw new ArgumentNullException(nameof(center));
            Name = "BatchTaskCenterPage";
            Dock = DockStyle.Fill;
            BackColor = UiTheme.WindowBackground;
            BuildInterface();
            UiTheme.Apply(this);
            ConfigureGridTheme();
            _center.Changed += (_, __) => RefreshFromCenter();
            RefreshFromCenter();
        }

        public event EventHandler RetryRequested;
        public event EventHandler ResumeRequested;

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            header.Controls.Add(new Label
            {
                Text = "批量任务中心",
                Dock = DockStyle.Top,
                Height = 48,
                Font = UiTheme.CreateFont(20f, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.BottomLeft
            });
            header.Controls.Add(new Label
            {
                Text = "查看识图、翻译和导出状态；任务会自动保存，软件重启后可从断点继续",
                Dock = DockStyle.Bottom,
                Height = 30,
                ForeColor = UiTheme.TextSecondary
            });
            root.Controls.Add(header, 0, 0);

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("Kind", "类型");
            _grid.Columns.Add("Target", "目标");
            _grid.Columns.Add("Status", "状态");
            _grid.Columns.Add("Attempts", "尝试");
            _grid.Columns.Add("Message", "结果 / 错误");
            _grid.Columns[0].FillWeight = 42;
            _grid.Columns[1].FillWeight = 145;
            _grid.Columns[2].FillWeight = 55;
            _grid.Columns[3].FillWeight = 38;
            _grid.Columns[4].FillWeight = 180;
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
            gridHost.Controls.Add(_grid);
            _emptyState.Text = "暂无批量任务\r\n\r\n请从图片工作台点击“批量识图”“批量翻译”或“导出”";
            _emptyState.Size = new Size(620, 94);
            _emptyState.TextAlign = ContentAlignment.MiddleCenter;
            _emptyState.ForeColor = UiTheme.TextSecondary;
            _emptyState.Font = UiTheme.CreateFont(11f);
            _emptyState.BackColor = UiTheme.CardBackground;
            gridHost.Controls.Add(_emptyState);
            gridHost.Resize += (_, __) => _emptyState.Location = new Point(
                Math.Max(0, (gridHost.ClientSize.Width - _emptyState.Width) / 2),
                Math.Max(_grid.ColumnHeadersHeight + 12,
                    (gridHost.ClientSize.Height - _emptyState.Height) / 2));
            _emptyState.BringToFront();
            root.Controls.Add(gridHost, 0, 1);

            var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            _progress.Dock = DockStyle.Fill;
            _progress.Margin = new Padding(0, 10, 14, 8);
            _summary.Dock = DockStyle.Fill;
            _summary.TextAlign = ContentAlignment.MiddleRight;
            _summary.ForeColor = UiTheme.TextPrimary;
            _summary.Font = UiTheme.CreateFont(10f, FontStyle.Bold);
            progressPanel.Controls.Add(_progress, 0, 0);
            progressPanel.Controls.Add(_summary, 1, 0);
            root.Controls.Add(progressPanel, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 0)
            };
            ConfigureButton(_pauseButton, "暂停");
            ConfigureButton(_cancelButton, "取消任务");
            ConfigureButton(_resumeButton, "继续未完成");
            _resumeButton.Name = "ResumeBatchTasksButton";
            ConfigureButton(_retryButton, "重试失败项");
            ConfigureButton(_clearButton, "清理已完成");
            ConfigureButton(_reportButton, "保存任务报告");
            _pauseButton.Click += (_, __) =>
            {
                if (_center.IsPaused) _center.Resume();
                else _center.Pause();
            };
            _cancelButton.Click += (_, __) => _center.Cancel();
            _resumeButton.Click += (_, __) => ResumeRequested?.Invoke(this, EventArgs.Empty);
            _retryButton.Click += (_, __) => RetryRequested?.Invoke(this, EventArgs.Empty);
            _clearButton.Click += (_, __) => _center.ClearCompleted();
            _reportButton.Click += (_, __) => SaveReport();
            buttons.Controls.AddRange(new Control[]
            {
                _pauseButton, _cancelButton, _resumeButton, _retryButton, _clearButton, _reportButton
            });
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);
        }

        private void RefreshFromCenter()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshFromCenter));
                return;
            }

            var items = _center.Items;
            _grid.Rows.Clear();
            foreach (var item in items)
            {
                var rowIndex = _grid.Rows.Add(
                    item.KindText, item.Target, item.StatusText, item.Attempts, item.Message);
                var row = _grid.Rows[rowIndex];
                row.DefaultCellStyle.ForeColor = item.Status == BatchTaskStatus.Failed ? Color.FromArgb(255, 120, 120)
                    : item.Status == BatchTaskStatus.Completed ? UiTheme.Success
                    : item.Status == BatchTaskStatus.Running ? UiTheme.AccentHover
                    : UiTheme.TextPrimary;
            }

            var terminal = items.Count(item => item.Status == BatchTaskStatus.Completed ||
                                               item.Status == BatchTaskStatus.Failed ||
                                               item.Status == BatchTaskStatus.Cancelled);
            _progress.SetProgress(terminal, Math.Max(1, items.Count));
            _emptyState.Visible = items.Count == 0;
            _summary.Text = $"总计 {items.Count}  |  完成 {items.Count(item => item.Status == BatchTaskStatus.Completed)}  |  " +
                            $"失败 {items.Count(item => item.Status == BatchTaskStatus.Failed)}  |  等待 {items.Count - terminal}";
            _pauseButton.Enabled = _center.IsRunning;
            _pauseButton.Text = _center.IsPaused ? "继续" : "暂停";
            _cancelButton.Enabled = _center.IsRunning;
            _resumeButton.Enabled = _center.CanResume;
            _retryButton.Enabled = !_center.IsRunning && items.Any(item => item.Status == BatchTaskStatus.Failed);
            _clearButton.Enabled = !_center.IsRunning && items.Any(item =>
                item.Status == BatchTaskStatus.Completed || item.Status == BatchTaskStatus.Cancelled);
            _reportButton.Enabled = items.Count > 0;
        }

        private void ConfigureGridTheme()
        {
            _grid.BackgroundColor = UiTheme.CardBackground;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.GridColor = UiTheme.BorderSoft;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.CardBackgroundLight;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextPrimary;
            _grid.DefaultCellStyle.BackColor = UiTheme.InputBackground;
            _grid.DefaultCellStyle.ForeColor = UiTheme.TextPrimary;
            _grid.DefaultCellStyle.SelectionBackColor = UiTheme.AccentDark;
            _grid.DefaultCellStyle.SelectionForeColor = UiTheme.TextPrimary;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.CardBackground;
        }

        private static void ConfigureButton(ModernButton button, string text)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Height = 40;
            button.Padding = new Padding(14, 5, 14, 5);
            button.Margin = new Padding(0, 0, 10, 0);
        }

        private void SaveReport()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "保存批量任务报告",
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "批量任务报告.txt",
                AddExtension = true,
                DefaultExt = "txt"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, _center.CreateReport(), new UTF8Encoding(false));
            }
        }

        private sealed class ThemedProgressBar : Control
        {
            private int _value;
            private int _maximum = 1;

            public ThemedProgressBar()
            {
                DoubleBuffered = true;
                BackColor = UiTheme.InputBackground;
                ForeColor = UiTheme.TextPrimary;
                Font = UiTheme.CreateFont(9f, FontStyle.Bold);
                MinimumSize = new Size(80, 24);
            }

            public void SetProgress(int value, int maximum)
            {
                _maximum = Math.Max(1, maximum);
                _value = Math.Max(0, Math.Min(_maximum, value));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs eventArgs)
            {
                base.OnPaint(eventArgs);
                eventArgs.Graphics.Clear(BackColor);
                var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                var ratio = _value / (float)_maximum;
                var fill = new Rectangle(bounds.X, bounds.Y, (int)Math.Round(bounds.Width * ratio), bounds.Height);
                using (var brush = new SolidBrush(UiTheme.AccentDark))
                using (var border = new Pen(UiTheme.Border))
                {
                    if (fill.Width > 0) eventArgs.Graphics.FillRectangle(brush, fill);
                    eventArgs.Graphics.DrawRectangle(border, bounds);
                }

                var text = $"{_value}/{_maximum}  ({ratio:P0})";
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    text,
                    Font,
                    ClientRectangle,
                    ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
