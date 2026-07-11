using System;
using System.Drawing;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    internal sealed class PresetNameDialog : Form
    {
        private readonly TextBox _name = new TextBox();

        public PresetNameDialog(string initialName)
        {
            Text = "保存文字样式预设";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 178);
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildInterface();
            _name.Text = initialName ?? string.Empty;
            _name.SelectAll();
            UiTheme.Apply(this);
        }

        public string PresetName { get; private set; } = string.Empty;

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = UiTheme.WindowBackground
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Controls.Add(new Label
            {
                Text = "为当前文字外观命名",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            root.Controls.Add(new Label
            {
                Text = "同名预设可以覆盖；不会保存文字内容、坐标和背景修复方式。",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            _name.Dock = DockStyle.Fill;
            _name.MaxLength = 60;
            root.Controls.Add(_name, 0, 2);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0),
                BackColor = Color.Transparent
            };
            var save = new ModernButton { Text = "保存", Width = 92, Height = 34, AccentStyle = true };
            var cancel = new ModernButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
            save.Click += (_, __) => SaveAndClose();
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            root.Controls.Add(actions, 0, 3);
            Controls.Add(root);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void SaveAndClose()
        {
            var value = _name.Text.Trim();
            if (value.Length == 0)
            {
                MessageBox.Show(this, "请输入预设名称。", "名称不能为空",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _name.Focus();
                return;
            }

            PresetName = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
