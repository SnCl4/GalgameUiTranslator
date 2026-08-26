using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class ApiSettingsDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly VisionApiClient _apiClient = new VisionApiClient();
        private readonly ComboBox _recognitionMode = new ComboBox();
        private readonly NumericUpDown _minimumConfidence = new NumericUpDown();
        private readonly CheckBox _cloudTiling = new CheckBox();
        private readonly ComboBox _visionPreset = new ComboBox();
        private readonly TextBox _visionUrl = new TextBox();
        private readonly TextBox _visionModel = new TextBox();
        private readonly TextBox _visionKey = new TextBox();
        private readonly Button _visionTestButton = new ModernButton();
        private readonly Label _visionTestStatus = new Label();
        private readonly ComboBox _translationPreset = new ComboBox();
        private readonly TextBox _translationUrl = new TextBox();
        private readonly TextBox _translationModel = new TextBox();
        private readonly TextBox _translationKey = new TextBox();
        private readonly Button _translationTestButton = new ModernButton();
        private readonly Label _translationTestStatus = new Label();
        private readonly TextBox _instructions = new TextBox();
        private readonly TextBox _defaultFont = new TextBox();
        private readonly CheckBox _rememberApiKeys = new CheckBox();
        private readonly Button _clearStoredKeys = new ModernButton();
        private readonly Dictionary<string, CredentialDraft> _credentialDrafts =
            new Dictionary<string, CredentialDraft>(StringComparer.OrdinalIgnoreCase);
        private string _visionCredentialId = string.Empty;
        private string _translationCredentialId = string.Empty;
        private bool _loadingValues;
        private bool _synchronizingKeys;

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
            MinimumSize = new Size(720, 720);
            Size = new Size(820, 900);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
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
                RowCount = 7
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(CreateRecognitionGroup(), 0, 0);
            root.Controls.Add(CreateCredentialStorageGroup(), 0, 1);
            root.Controls.Add(CreateApiGroup(
                "云端视觉识别 API（本地模式可留空）",
                _visionPreset,
                _visionUrl,
                _visionModel,
                _visionKey,
                _visionTestButton,
                _visionTestStatus,
                true,
                "用于云端识别或与本地 OCR 合并。连接测试只发送一条短文本，不上传图片，最长等待 30 秒。"), 0, 2);
            root.Controls.Add(CreateApiGroup(
                "文本翻译 API（可直接填写 DeepSeek）",
                _translationPreset,
                _translationUrl,
                _translationModel,
                _translationKey,
                _translationTestButton,
                _translationTestStatus,
                false,
                "只负责把已识别或手工录入的原文翻译为中文；识图阶段不会再生成译文。"), 0, 3);

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
            root.Controls.Add(instructionGroup, 0, 4);

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
            root.Controls.Add(fontPanel, 0, 5);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0)
            };
            var ok = new Button { Text = "确定", AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
            ok.Click += (_, __) => SaveAndClose();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 6);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(root);
        }

        private GroupBox CreateRecognitionGroup()
        {
            var group = new GroupBox
            {
                Name = "RecognitionSettingsGroup",
                Text = "图片识别方式",
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
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _recognitionMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _recognitionMode.Name = "RecognitionModeCombo";
            _recognitionMode.Items.AddRange(new object[]
            {
                "本地 OCR（推荐，图片不上云）",
                "本地 + 云端合并（降低漏字率，会产生费用）",
                "仅使用云端视觉 API"
            });
            AddRow(table, 0, "识图方式", _recognitionMode);

            _minimumConfidence.Name = "LocalOcrConfidence";
            _minimumConfidence.Minimum = 10;
            _minimumConfidence.Maximum = 95;
            _minimumConfidence.Increment = 5;
            _minimumConfidence.DecimalPlaces = 0;
            AddRow(table, 1, "最低保留置信度（%）", _minimumConfidence);

            _cloudTiling.Name = "CloudTilingCheckBox";
            _cloudTiling.Text = "大图自动分块识别（边长超过 1536px 时提高小字识别率，但会增加请求次数）";
            _cloudTiling.AutoSize = true;
            AddRow(table, 2, "云端大图", _cloudTiling);

            var note = new Label
            {
                Text = "本地 OCR 不需要 API Key。合并模式会同时运行本地与云端识图：重叠区域优先采用高置信度结果，并保留双方发现的独立区域。",
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(0, 4, 0, 8)
            };
            table.Controls.Add(note, 0, 3);
            table.SetColumnSpan(note, 2);
            group.Controls.Add(table);
            return group;
        }

        private GroupBox CreateCredentialStorageGroup()
        {
            var group = new GroupBox
            {
                Name = "ApiCredentialStorageGroup",
                Text = "供应商密钥库",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10)
            };
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            _rememberApiKeys.Name = "RememberApiKeysCheckBox";
            _rememberApiKeys.Text = "使用 Windows 凭据管理器按供应商保存 API Key";
            _rememberApiKeys.AutoSize = true;
            _rememberApiKeys.Margin = new Padding(3, 8, 12, 3);
            _clearStoredKeys.Name = "ClearStoredApiKeysButton";
            _clearStoredKeys.Text = "清除当前供应商密钥";
            _clearStoredKeys.AutoSize = true;
            _clearStoredKeys.Click += (_, __) => ClearCurrentStoredKeys();
            panel.Controls.Add(_rememberApiKeys);
            panel.Controls.Add(_clearStoredKeys);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2
            };
            root.Controls.Add(panel, 0, 0);
            root.Controls.Add(new Label
            {
                Text = "DeepSeek、Gemini 和自定义接口分别保存；识图与翻译选择同一供应商时自动共用。密钥不写入工程或 settings.json。",
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(0, 3, 0, 6)
            }, 0, 1);
            group.Controls.Add(root);
            return group;
        }

        private GroupBox CreateApiGroup(
            string title,
            ComboBox preset,
            TextBox url,
            TextBox model,
            TextBox key,
            Button testButton,
            Label testStatus,
            bool vision,
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
                RowCount = 6
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            preset.DropDownStyle = ComboBoxStyle.DropDownList;
            preset.Name = vision ? "VisionProviderPreset" : "TranslationProviderPreset";
            preset.Items.AddRange((vision ? ApiProviderProfiles.Vision : ApiProviderProfiles.Translation)
                .Cast<object>()
                .ToArray());
            preset.SelectedIndexChanged += (_, __) => SwitchProviderPreset(
                vision,
                preset,
                url,
                model,
                key);
            AddRow(table, 0, "供应商预设", preset);
            AddRow(table, 1, "API 地址", url);
            AddRow(table, 2, "模型名称", model);
            url.Leave += (_, __) => RefreshCredentialBinding(vision, url, model, key);
            model.Leave += (_, __) => RefreshCredentialBinding(vision, url, model, key);
            key.UseSystemPasswordChar = true;
            key.Name = vision ? "VisionApiKeyTextBox" : "TranslationApiKeyTextBox";
            key.TextChanged += (_, __) => SynchronizeProviderKey(vision, key);
            AddRow(table, 3, "API Key", key);

            testButton.Name = vision ? "TestVisionApiButton" : "TestTranslationApiButton";
            testButton.Text = "测试连接";
            testButton.AutoSize = true;
            testButton.Click += async (_, __) => await TestConnectionAsync(
                vision,
                url,
                model,
                key,
                testButton,
                testStatus);
            var testPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            testStatus.AutoSize = true;
            testStatus.Anchor = AnchorStyles.Left;
            testStatus.Margin = new Padding(10, 8, 3, 3);
            testStatus.ForeColor = UiTheme.TextSecondary;
            testPanel.Controls.Add(testButton);
            testPanel.Controls.Add(testStatus);
            table.Controls.Add(new Label { Text = "接口状态", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            table.Controls.Add(testPanel, 1, 4);

            var note = new Label
            {
                Text = description + " 选择供应商预设时会自动切换到该供应商单独保存的密钥。",
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(0, 4, 0, 8)
            };
            table.Controls.Add(note, 0, 5);
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
            _loadingValues = true;
            var recognitionMode = RecognitionModes.Normalize(_settings.RecognitionMode);
            _recognitionMode.SelectedIndex = recognitionMode == RecognitionModes.Cloud
                ? 2
                : recognitionMode == RecognitionModes.LocalThenCloud ? 1 : 0;
            var confidence = Math.Max(0.1f, Math.Min(0.95f, _settings.LocalOcrMinimumConfidence));
            _minimumConfidence.Value = (decimal)(confidence * 100f);
            _cloudTiling.Checked = _settings.CloudTilingEnabled;
            _rememberApiKeys.Checked = _settings.RememberApiKeys;
            _visionUrl.Text = _settings.VisionApiBaseUrl;
            _visionModel.Text = _settings.VisionModel;
            _translationUrl.Text = _settings.TranslationApiBaseUrl;
            _translationModel.Text = _settings.TranslationModel;
            _instructions.Text = _settings.TranslationInstructions;
            _defaultFont.Text = _settings.DefaultFontFamily;
            SelectMatchingPreset(_visionPreset, ApiProviderProfiles.Vision, _visionUrl.Text, _visionModel.Text);
            SelectMatchingPreset(
                _translationPreset,
                ApiProviderProfiles.Translation,
                _translationUrl.Text,
                _translationModel.Text);
            _visionCredentialId = ApiCredentialStore.GetCredentialId(_visionUrl.Text, _visionModel.Text);
            _translationCredentialId = ApiCredentialStore.GetCredentialId(
                _translationUrl.Text,
                _translationModel.Text);
            _visionKey.Text = ResolveCredential(
                VisionApiKey,
                _visionUrl.Text,
                _visionModel.Text);
            _translationKey.Text = ResolveCredential(
                TranslationApiKey,
                _translationUrl.Text,
                _translationModel.Text);
            if (string.Equals(_visionCredentialId, _translationCredentialId, StringComparison.OrdinalIgnoreCase))
            {
                var shared = FirstNonEmpty(_visionKey.Text, _translationKey.Text);
                _visionKey.Text = shared;
                _translationKey.Text = shared;
                UpdateDraftValue(_visionCredentialId, shared);
            }
            _loadingValues = false;
        }

        private void SaveValues()
        {
            CacheCurrentKey(true, _visionUrl, _visionModel, _visionKey);
            CacheCurrentKey(false, _translationUrl, _translationModel, _translationKey);
            _settings.RecognitionMode = _recognitionMode.SelectedIndex == 2
                ? RecognitionModes.Cloud
                : _recognitionMode.SelectedIndex == 1
                    ? RecognitionModes.LocalThenCloud
                    : RecognitionModes.Local;
            _settings.LocalOcrMinimumConfidence = (float)_minimumConfidence.Value / 100f;
            _settings.CloudTilingEnabled = _cloudTiling.Checked;
            _settings.RememberApiKeys = _rememberApiKeys.Checked;
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
            if (_rememberApiKeys.Checked) PersistCredentialDrafts();
        }

        private void SaveAndClose()
        {
            try
            {
                SaveValues();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法保存 API 设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SwitchProviderPreset(
            bool vision,
            ComboBox combo,
            TextBox url,
            TextBox model,
            TextBox key)
        {
            if (_loadingValues || !(combo.SelectedItem is ApiProviderPreset preset)) return;
            CacheCurrentKey(vision, url, model, key);
            if (!preset.IsCustom)
            {
                url.Text = preset.BaseUrl;
                model.Text = preset.Model;
            }
            BindCredential(vision, url, model, key);
        }

        private void RefreshCredentialBinding(
            bool vision,
            TextBox url,
            TextBox model,
            TextBox key)
        {
            if (_loadingValues) return;
            CacheCurrentKey(vision, url, model, key);
            BindCredential(vision, url, model, key);
        }

        private void BindCredential(
            bool vision,
            TextBox url,
            TextBox model,
            TextBox key)
        {
            var credentialId = ApiCredentialStore.GetCredentialId(url.Text, model.Text);
            if (vision) _visionCredentialId = credentialId;
            else _translationCredentialId = credentialId;

            var value = ResolveCredential(string.Empty, url.Text, model.Text);
            var otherId = vision ? _translationCredentialId : _visionCredentialId;
            var otherValue = vision ? _translationKey.Text : _visionKey.Text;
            if (string.Equals(credentialId, otherId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(otherValue))
            {
                value = otherValue.Trim();
                UpdateDraftValue(credentialId, value);
            }
            SetKeyText(key, value);
        }

        private string ResolveCredential(
            string suppliedKey,
            string baseUrl,
            string model)
        {
            var id = ApiCredentialStore.GetCredentialId(baseUrl, model);
            if (_credentialDrafts.TryGetValue(id, out var existing))
            {
                if (!string.IsNullOrWhiteSpace(suppliedKey)) existing.ApiKey = suppliedKey.Trim();
                return existing.ApiKey;
            }

            var value = (suppliedKey ?? string.Empty).Trim();
            if (value.Length == 0 && _rememberApiKeys.Checked)
                ApiCredentialStore.TryRead(baseUrl, model, out value);
            _credentialDrafts[id] = new CredentialDraft
            {
                BaseUrl = (baseUrl ?? string.Empty).Trim(),
                Model = (model ?? string.Empty).Trim(),
                ApiKey = value
            };
            return value;
        }

        private void CacheCurrentKey(
            bool vision,
            TextBox url,
            TextBox model,
            TextBox key)
        {
            var id = vision ? _visionCredentialId : _translationCredentialId;
            if (string.IsNullOrWhiteSpace(id))
                id = ApiCredentialStore.GetCredentialId(url.Text, model.Text);
            if (!_credentialDrafts.TryGetValue(id, out var draft))
            {
                draft = new CredentialDraft
                {
                    BaseUrl = url.Text.Trim(),
                    Model = model.Text.Trim()
                };
                _credentialDrafts[id] = draft;
            }
            draft.ApiKey = key.Text.Trim();
        }

        private void SynchronizeProviderKey(bool vision, TextBox source)
        {
            if (_loadingValues || _synchronizingKeys) return;
            CacheCurrentKey(
                vision,
                vision ? _visionUrl : _translationUrl,
                vision ? _visionModel : _translationModel,
                source);
            if (!string.Equals(
                    _visionCredentialId,
                    _translationCredentialId,
                    StringComparison.OrdinalIgnoreCase)) return;

            var target = vision ? _translationKey : _visionKey;
            SetKeyText(target, source.Text);
            UpdateDraftValue(vision ? _visionCredentialId : _translationCredentialId, source.Text.Trim());
        }

        private void SetKeyText(TextBox target, string value)
        {
            _synchronizingKeys = true;
            try
            {
                target.Text = value ?? string.Empty;
            }
            finally
            {
                _synchronizingKeys = false;
            }
        }

        private void UpdateDraftValue(string credentialId, string value)
        {
            if (string.IsNullOrWhiteSpace(credentialId) ||
                !_credentialDrafts.TryGetValue(credentialId, out var draft)) return;
            draft.ApiKey = (value ?? string.Empty).Trim();
        }

        private void PersistCredentialDrafts()
        {
            foreach (var draft in _credentialDrafts.Values)
            {
                if (string.IsNullOrWhiteSpace(draft.ApiKey))
                    ApiCredentialStore.Delete(draft.BaseUrl, draft.Model);
                else
                    ApiCredentialStore.Write(draft.BaseUrl, draft.Model, draft.ApiKey);
            }
        }

        private void ClearCurrentStoredKeys()
        {
            if (MessageBox.Show(
                    this,
                    "将从 Windows 凭据管理器删除当前视觉与翻译供应商的 API Key。是否继续？",
                    "清除已保存密钥",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                CacheCurrentKey(true, _visionUrl, _visionModel, _visionKey);
                CacheCurrentKey(false, _translationUrl, _translationModel, _translationKey);
                ApiCredentialStore.Delete(_visionUrl.Text, _visionModel.Text);
                if (!string.Equals(
                        _visionCredentialId,
                        _translationCredentialId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ApiCredentialStore.Delete(_translationUrl.Text, _translationModel.Text);
                }
                UpdateDraftValue(_visionCredentialId, string.Empty);
                UpdateDraftValue(_translationCredentialId, string.Empty);
                SetKeyText(_visionKey, string.Empty);
                SetKeyText(_translationKey, string.Empty);
                _visionTestStatus.Text = string.Empty;
                _translationTestStatus.Text = string.Empty;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法清除密钥",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first.Trim() : (second ?? string.Empty).Trim();
        }

        private static void SelectMatchingPreset(
            ComboBox combo,
            System.Collections.Generic.IEnumerable<ApiProviderPreset> profiles,
            string baseUrl,
            string model)
        {
            var match = ApiProviderProfiles.Match(profiles, baseUrl, model);
            combo.SelectedItem = match;
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private async Task TestConnectionAsync(
            bool vision,
            TextBox url,
            TextBox model,
            TextBox key,
            Button button,
            Label status)
        {
            var temporary = new AppSettings
            {
                VisionApiBaseUrl = url.Text.Trim(),
                VisionModel = model.Text.Trim(),
                TranslationApiBaseUrl = url.Text.Trim(),
                TranslationModel = model.Text.Trim(),
                TranslationInstructions = _instructions.Text.Trim()
            };
            button.Enabled = false;
            status.ForeColor = UiTheme.TextSecondary;
            status.Text = "正在测试…";
            try
            {
                status.Text = vision
                    ? await _apiClient.TestVisionConnectionAsync(temporary, key.Text.Trim(), CancellationToken.None)
                    : await _apiClient.TestTranslationConnectionAsync(temporary, key.Text.Trim(), CancellationToken.None);
                status.ForeColor = UiTheme.Success;
            }
            catch (Exception exception)
            {
                status.Text = "连接失败";
                status.ForeColor = Color.FromArgb(242, 105, 105);
                MessageBox.Show(this, exception.Message, "API 连接测试失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                button.Enabled = true;
            }
        }

        private sealed class CredentialDraft
        {
            public string BaseUrl { get; set; } = string.Empty;
            public string Model { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
        }
    }
}
