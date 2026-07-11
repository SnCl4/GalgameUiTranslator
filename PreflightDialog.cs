using System;
using System.Drawing;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class PreflightDialog : Form
    {
        private readonly PreflightReport _report;
        private readonly ListView _issues = new ListView();
        private readonly Label _summary = new Label();
        private readonly Button _continueButton = new ModernButton();

        public PreflightDialog(PreflightReport report, bool exporting)
        {
            _report = report;
            Text = exporting ? "导出质量预检" : "工程质量检查";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(960, 600);
            MinimumSize = new Size(760, 460);
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildInterface(exporting);
            PopulateIssues();
            UiTheme.Apply(this);
        }

        private void BuildInterface(bool exporting)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackground,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 2 };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            header.Controls.Add(new Label
            {
                Text = "导出前质量检查",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(17f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            _summary.Dock = DockStyle.Fill;
            _summary.Font = UiTheme.CreateFont(9f);
            header.Controls.Add(_summary, 0, 1);
            root.Controls.Add(header, 0, 0);

            _issues.Dock = DockStyle.Fill;
            _issues.View = View.Details;
            _issues.FullRowSelect = true;
            _issues.GridLines = true;
            _issues.HideSelection = false;
            _issues.BackColor = UiTheme.InputBackground;
            _issues.ForeColor = UiTheme.TextPrimary;
            _issues.Columns.Add("级别", 80);
            _issues.Columns.Add("图片", 240);
            _issues.Columns.Add("区域", 70);
            _issues.Columns.Add("问题", 510);
            root.Controls.Add(_issues, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 12, 0, 0)
            };
            _continueButton.Text = exporting ? "继续导出" : "关闭";
            _continueButton.Width = 128;
            _continueButton.DialogResult = _report.CanExport ? DialogResult.OK : DialogResult.None;
            _continueButton.Enabled = _report.CanExport || !exporting;
            if (_report.CanExport) ((ModernButton)_continueButton).AccentStyle = true;
            if (!exporting) _continueButton.Click += (_, __) => Close();
            var back = new ModernButton
            {
                Text = exporting ? "返回修改" : "关闭",
                Width = 120,
                DialogResult = DialogResult.Cancel
            };
            buttons.Controls.Add(_continueButton);
            if (exporting) buttons.Controls.Add(back);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
            AcceptButton = _report.CanExport ? _continueButton : null;
            CancelButton = back;
        }

        private void PopulateIssues()
        {
            _summary.Text = $"错误 {_report.ErrorCount}  ·  警告 {_report.WarningCount}  ·  提示 {_report.InfoCount}";
            _summary.ForeColor = _report.ErrorCount > 0
                ? Color.FromArgb(242, 105, 105)
                : _report.WarningCount > 0 ? UiTheme.Warning : UiTheme.Success;
            foreach (var issue in _report.Issues)
            {
                var severity = issue.Severity == PreflightSeverity.Error
                    ? "错误"
                    : issue.Severity == PreflightSeverity.Warning ? "警告" : "提示";
                var item = new ListViewItem(severity)
                {
                    ForeColor = issue.Severity == PreflightSeverity.Error
                        ? Color.FromArgb(242, 105, 105)
                        : issue.Severity == PreflightSeverity.Warning ? UiTheme.Warning : UiTheme.TextSecondary
                };
                item.SubItems.Add(issue.ImagePath);
                item.SubItems.Add(issue.RegionIndex?.ToString() ?? "-");
                item.SubItems.Add(issue.Message);
                item.ToolTipText = issue.Code;
                _issues.Items.Add(item);
            }

            if (_report.Issues.Count == 0)
            {
                var item = new ListViewItem("通过") { ForeColor = UiTheme.Success };
                item.SubItems.Add(string.Empty);
                item.SubItems.Add("-");
                item.SubItems.Add("没有发现阻止导出的问题。");
                _issues.Items.Add(item);
            }
        }
    }
}
