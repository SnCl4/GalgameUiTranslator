using System;
using System.Drawing;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class ApiSettingsDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly TextBox _visionUrl = new TextBox();
        private readonly TextBox _visionModel = new TextBox();
        private readonly TextBox _visionKey = new TextBox();
        private readonly TextBox _translationUrl = new TextBox();
        private readonly TextBox _translationModel = new TextBox();
        private readonly TextBox _translationKey = new TextBox();
        private readonly TextBox _instructions = new TextBox();
        private readonly TextBox _defaultFont = new TextBox();

        public ApiSettingsDialog(
            AppSettings settings,
            string visionApiKey,
            string translationApiKey)
        {
            _settings = settings;
            VisionApiKey = visionApiKey ?? string.Empty;
            TranslationApiKey = translationApiKey ?? string.Empty;

            Text = "API 与翻译设置";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(660, 620);
            Size = new Size(720, 680);
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildInterface();
            LoadValues();
            UiTheme.Apply(this);
        }

        public string VisionApiKey { get; private set; }
        public string TranslationApiKey { get; private set; }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(CreateApiGroup(
                "视觉识别 API（可留空，仍可手工框选）",
                _visionUrl,
                _visionModel,
                _visionKey,
                "用于识别图片中文字、位置和样式。支持 OpenAI 兼容的多模态 Chat Completions。"), 0, 0);
            root.Controls.Add(CreateApiGroup(
                "文本翻译 API（可直接填写 DeepSeek）",
                _translationUrl,
                _translationModel,
                _translationKey,
                "用于翻译已识别或手工录入的日文。DeepSeek 当前可在这一层直接使用。"), 0, 1);

            var instructionGroup = new GroupBox
            {
                Text = "翻译要求 / 术语规则",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            _instructions.Multiline = true;
            _instructions.ScrollBars = ScrollBars.Vertical;
            _instructions.Dock = DockStyle.Fill;
            instructionGroup.Controls.Add(_instructions);
            root.Controls.Add(instructionGroup, 0, 2);

            var fontPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            fontPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            fontPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            fontPanel.Controls.Add(new Label { Text = "默认中文字体", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _defaultFont.Dock = DockStyle.Fill;
            fontPanel.Controls.Add(_defaultFont, 1, 0);
            root.Controls.Add(fontPanel, 0, 3);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0)
            };
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
            ok.Click += (_, __) => SaveValues();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 4);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(root);
        }

        private static GroupBox CreateApiGroup(
            string title,
            TextBox url,
            TextBox model,
            TextBox key,
            string description)
        {
            var group = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10)
            };
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 4
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow(table, 0, "API 地址", url);
            AddRow(table, 1, "模型名称", model);
            key.UseSystemPasswordChar = true;
            AddRow(table, 2, "API Key", key);
            var note = new Label
            {
                Text = description + " 密钥仅存于内存，关闭软件后清除。",
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(0, 4, 0, 8)
            };
            table.Controls.Add(note, 0, 3);
            table.SetColumnSpan(note, 2);
            group.Controls.Add(table);
            return group;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 7, 3, 3)
            }, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 3, 3, 6);
            table.Controls.Add(control, 1, row);
        }

        private void LoadValues()
        {
            _visionUrl.Text = _settings.VisionApiBaseUrl;
            _visionModel.Text = _settings.VisionModel;
            _visionKey.Text = VisionApiKey;
            _translationUrl.Text = _settings.TranslationApiBaseUrl;
            _translationModel.Text = _settings.TranslationModel;
            _translationKey.Text = TranslationApiKey;
            _instructions.Text = _settings.TranslationInstructions;
            _defaultFont.Text = _settings.DefaultFontFamily;
        }

        private void SaveValues()
        {
            _settings.VisionApiBaseUrl = _visionUrl.Text.Trim();
            _settings.VisionModel = _visionModel.Text.Trim();
            _settings.TranslationApiBaseUrl = _translationUrl.Text.Trim();
            _settings.TranslationModel = _translationModel.Text.Trim();
            _settings.TranslationInstructions = _instructions.Text.Trim();
            _settings.DefaultFontFamily = string.IsNullOrWhiteSpace(_defaultFont.Text)
                ? "Microsoft YaHei"
                : _defaultFont.Text.Trim();
            VisionApiKey = _visionKey.Text.Trim();
            TranslationApiKey = _translationKey.Text.Trim();
        }
    }
}
