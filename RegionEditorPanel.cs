using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class TextRegionEventArgs : EventArgs
    {
        public TextRegionEventArgs(TextRegion region) => Region = region;
        public TextRegion Region { get; }
    }

    public sealed class RegionEditorPanel : UserControl
    {
        private readonly ListBox _regionList = new ListBox();
        private readonly TextBox _sourceText = new TextBox();
        private readonly TextBox _translationText = new TextBox();
        private readonly NumericUpDown _x = CreateCoordinateInput();
        private readonly NumericUpDown _y = CreateCoordinateInput();
        private readonly NumericUpDown _width = CreateCoordinateInput();
        private readonly NumericUpDown _height = CreateCoordinateInput();
        private readonly ComboBox _stylePreset = new ComboBox { Name = "StylePresetCombo" };
        private readonly ModernButton _applyPreset = new ModernButton { Name = "ApplyStylePresetButton", Text = "应用", Height = 32 };
        private readonly ModernButton _savePreset = new ModernButton { Name = "SaveStylePresetButton", Text = "保存", Height = 32 };
        private readonly ModernButton _deletePreset = new ModernButton { Name = "DeleteStylePresetButton", Text = "删除", Height = 32 };
        private readonly ComboBox _fontFamily = new ComboBox();
        private readonly NumericUpDown _fontSize = new NumericUpDown();
        private readonly CheckBox _autoFit = new CheckBox();
        private readonly CheckBox _bold = new CheckBox();
        private readonly Button _textColor = new Button();
        private readonly Button _outlineColor = new Button();
        private readonly NumericUpDown _outlineWidth = new NumericUpDown();
        private readonly NumericUpDown _letterSpacing = new NumericUpDown();
        private readonly NumericUpDown _lineSpacing = new NumericUpDown();
        private readonly CheckBox _verticalText = new CheckBox();
        private readonly NumericUpDown _rotation = new NumericUpDown();
        private readonly CheckBox _shadowEnabled = new CheckBox();
        private readonly Button _shadowColor = new Button();
        private readonly NumericUpDown _shadowOffsetX = new NumericUpDown();
        private readonly NumericUpDown _shadowOffsetY = new NumericUpDown();
        private readonly NumericUpDown _glowWidth = new NumericUpDown();
        private readonly Button _glowColor = new Button();
        private readonly ComboBox _textFillMode = new ComboBox();
        private readonly Button _gradientEndColor = new Button();
        private readonly ComboBox _horizontal = new ComboBox();
        private readonly ComboBox _vertical = new ComboBox();
        private readonly ComboBox _background = new ComboBox();
        private readonly NumericUpDown _clearPadding = new NumericUpDown();
        private readonly CheckBox _reviewed = new CheckBox();
        private readonly Label _confidence = new Label();
        private readonly Timer _editTimer = new Timer();
        private readonly TextStylePresetService _presetService;
        private ImageDocument _document;
        private TextRegion _region;
        private bool _loading;

        public RegionEditorPanel()
        {
            _presetService = TextStylePresetService.LoadDefault();
            AutoScroll = true;
            BackColor = UiTheme.CardBackground;
            BuildInterface();
            WireEvents();
            UiTheme.Apply(this);
            RefreshStylePresets();
            _editTimer.Interval = 220;
            _editTimer.Tick += (_, __) =>
            {
                _editTimer.Stop();
                RegionEdited?.Invoke(this, EventArgs.Empty);
            };
        }

        public event EventHandler<TextRegionEventArgs> RegionSelected;
        public event EventHandler RegionEdited;
        public event EventHandler LoadFontRequested;

        public TextRegion CurrentRegion => _region;

        public void SetDocument(ImageDocument document, TextRegion selected)
        {
            _document = document;
            RefreshRegionList(selected);
            SelectRegion(selected);
        }

        public void SelectRegion(TextRegion region)
        {
            _region = region;
            _loading = true;
            try
            {
                for (var index = 0; index < _regionList.Items.Count; index++)
                {
                    if (ReferenceEquals(((RegionListEntry)_regionList.Items[index]).Region, region))
                    {
                        _regionList.SelectedIndex = index;
                        break;
                    }
                }

                if (region == null)
                {
                    ClearInputs();
                    return;
                }

                _sourceText.Text = region.SourceText;
                _translationText.Text = region.Translation;
                SetNumeric(_x, region.X);
                SetNumeric(_y, region.Y);
                SetNumeric(_width, region.Width);
                SetNumeric(_height, region.Height);
                _fontFamily.Text = region.FontFamily;
                SetNumeric(_fontSize, (decimal)region.FontSize);
                _autoFit.Checked = region.AutoFit;
                _bold.Checked = region.Bold;
                SetColorButton(_textColor, Color.FromArgb(region.TextColorArgb));
                SetColorButton(_outlineColor, Color.FromArgb(region.OutlineColorArgb));
                SetNumeric(_outlineWidth, (decimal)region.OutlineWidth);
                SetNumeric(_letterSpacing, (decimal)region.LetterSpacing);
                SetNumeric(_lineSpacing, (decimal)Math.Max(0.5f, region.LineSpacing));
                _verticalText.Checked = region.VerticalText;
                SetNumeric(_rotation, (decimal)region.RotationDegrees);
                _shadowEnabled.Checked = region.ShadowEnabled;
                SetColorButton(_shadowColor, Color.FromArgb(region.ShadowColorArgb));
                SetNumeric(_shadowOffsetX, region.ShadowOffsetX);
                SetNumeric(_shadowOffsetY, region.ShadowOffsetY);
                SetNumeric(_glowWidth, (decimal)region.GlowWidth);
                SetColorButton(_glowColor, Color.FromArgb(region.GlowColorArgb));
                SelectOption(_textFillMode, region.TextFillMode);
                SetColorButton(_gradientEndColor, Color.FromArgb(region.GradientEndColorArgb));
                SelectOption(_horizontal, region.HorizontalAlignment);
                SelectOption(_vertical, region.VerticalAlignment);
                SelectOption(_background, region.BackgroundMode);
                SetNumeric(_clearPadding, region.ClearPadding);
                _reviewed.Checked = region.Reviewed;
                _confidence.Text = $"识别置信度：{region.Confidence:P0}";
                _confidence.ForeColor = region.Confidence < 0.75f ? UiTheme.Warning : UiTheme.TextSecondary;
            }
            finally
            {
                _loading = false;
                UpdatePresetButtons();
            }
        }

        public void RefreshRegionList(TextRegion selected = null)
        {
            selected = selected ?? _region;
            _loading = true;
            try
            {
                _regionList.BeginUpdate();
                _regionList.Items.Clear();
                if (_document != null)
                {
                    for (var index = 0; index < _document.Regions.Count; index++)
                    {
                        _regionList.Items.Add(new RegionListEntry(_document.Regions[index], index + 1));
                    }
                }
                _regionList.EndUpdate();

                for (var index = 0; index < _regionList.Items.Count; index++)
                {
                    if (ReferenceEquals(((RegionListEntry)_regionList.Items[index]).Region, selected))
                    {
                        _regionList.SelectedIndex = index;
                        break;
                    }
                }
            }
            finally
            {
                _loading = false;
            }
        }

        public void RefreshFontNames(string selectedName = null)
        {
            var selected = selectedName ?? _fontFamily.Text;
            _loading = true;
            try
            {
                _fontFamily.BeginUpdate();
                _fontFamily.Items.Clear();
                foreach (var name in FontManager.GetAvailableFontNames())
                {
                    _fontFamily.Items.Add(name);
                }
                _fontFamily.EndUpdate();
                _fontFamily.Text = selected;
            }
            finally
            {
                _loading = false;
            }
        }

        private void BuildInterface()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            var row = 0;

            var title = new Label
            {
                Text = "文字区域与排版",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(0, 0, 0, 6)
            };
            table.Controls.Add(title, 0, row);
            table.SetColumnSpan(title, 2);
            row++;

            _regionList.Height = 110;
            _regionList.Dock = DockStyle.Fill;
            table.Controls.Add(_regionList, 0, row);
            table.SetColumnSpan(_regionList, 2);
            row++;

            AddFullWidthText(table, ref row, "日文原文", _sourceText);
            AddFullWidthText(table, ref row, "中文译文", _translationText);

            var boundsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddLabeledInput(boundsPanel, "X", _x);
            AddLabeledInput(boundsPanel, "Y", _y);
            AddLabeledInput(boundsPanel, "宽", _width);
            AddLabeledInput(boundsPanel, "高", _height);
            AddRow(table, ref row, "像素区域", boundsPanel);

            var presetTitle = new Label
            {
                Text = "文字样式预设",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0, 12, 0, 5)
            };
            table.Controls.Add(presetTitle, 0, row);
            table.SetColumnSpan(presetTitle, 2);
            row++;

            _stylePreset.DropDownStyle = ComboBoxStyle.DropDownList;
            AddRow(table, ref row, "预设", _stylePreset);
            var presetActions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty
            };
            presetActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            presetActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            presetActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            _applyPreset.Dock = DockStyle.Fill;
            _savePreset.Dock = DockStyle.Fill;
            _deletePreset.Dock = DockStyle.Fill;
            _applyPreset.Margin = new Padding(0, 2, 3, 2);
            _savePreset.Margin = new Padding(2, 2, 2, 2);
            _deletePreset.Margin = new Padding(3, 2, 0, 2);
            presetActions.Controls.Add(_applyPreset, 0, 0);
            presetActions.Controls.Add(_savePreset, 1, 0);
            presetActions.Controls.Add(_deletePreset, 2, 0);
            AddRow(table, ref row, "预设操作", presetActions);

            var fontPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            fontPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            fontPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _fontFamily.DropDownStyle = ComboBoxStyle.DropDown;
            _fontFamily.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _fontFamily.AutoCompleteSource = AutoCompleteSource.ListItems;
            _fontFamily.Dock = DockStyle.Fill;
            var loadFont = new Button { Text = "载入字体…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            loadFont.Click += (_, __) => LoadFontRequested?.Invoke(this, EventArgs.Empty);
            fontPanel.Controls.Add(_fontFamily, 0, 0);
            fontPanel.Controls.Add(loadFont, 1, 0);
            AddRow(table, ref row, "字体", fontPanel);

            _fontSize.Minimum = 6;
            _fontSize.Maximum = 300;
            _fontSize.DecimalPlaces = 1;
            _fontSize.Increment = 0.5m;
            AddRow(table, ref row, "字号(px)", _fontSize);

            var fontFlags = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            _autoFit.Text = "自动缩小适配";
            _bold.Text = "粗体";
            fontFlags.Controls.Add(_autoFit);
            fontFlags.Controls.Add(_bold);
            AddRow(table, ref row, "字体选项", fontFlags);

            ConfigureColorButton(_textColor);
            AddRow(table, ref row, "文字颜色", _textColor);
            ConfigureColorButton(_outlineColor);
            AddRow(table, ref row, "描边颜色", _outlineColor);

            _outlineWidth.Minimum = 0;
            _outlineWidth.Maximum = 20;
            _outlineWidth.DecimalPlaces = 1;
            _outlineWidth.Increment = 0.5m;
            AddRow(table, ref row, "描边宽度", _outlineWidth);

            var advancedTitle = new Label
            {
                Text = "高级文字样式",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0, 12, 0, 5)
            };
            table.Controls.Add(advancedTitle, 0, row);
            table.SetColumnSpan(advancedTitle, 2);
            row++;

            _letterSpacing.Minimum = -10;
            _letterSpacing.Maximum = 50;
            _letterSpacing.DecimalPlaces = 1;
            _letterSpacing.Increment = 0.5m;
            AddRow(table, ref row, "字间距(px)", _letterSpacing);

            _lineSpacing.Minimum = 0.5m;
            _lineSpacing.Maximum = 3m;
            _lineSpacing.DecimalPlaces = 2;
            _lineSpacing.Increment = 0.05m;
            AddRow(table, ref row, "行距倍率", _lineSpacing);

            var directionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            _verticalText.Text = "竖排文字";
            directionPanel.Controls.Add(_verticalText);
            AddLabeledInput(directionPanel, "旋转°", _rotation);
            _rotation.Minimum = -180;
            _rotation.Maximum = 180;
            _rotation.DecimalPlaces = 1;
            _rotation.Increment = 1m;
            AddRow(table, ref row, "方向", directionPanel);

            var shadowPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _shadowEnabled.Text = "启用";
            shadowPanel.Controls.Add(_shadowEnabled);
            AddLabeledInput(shadowPanel, "X", _shadowOffsetX);
            AddLabeledInput(shadowPanel, "Y", _shadowOffsetY);
            _shadowOffsetX.Minimum = -50;
            _shadowOffsetX.Maximum = 50;
            _shadowOffsetY.Minimum = -50;
            _shadowOffsetY.Maximum = 50;
            AddRow(table, ref row, "阴影偏移", shadowPanel);
            ConfigureColorButton(_shadowColor);
            AddRow(table, ref row, "阴影颜色", _shadowColor);

            _glowWidth.Minimum = 0;
            _glowWidth.Maximum = 30;
            _glowWidth.DecimalPlaces = 1;
            _glowWidth.Increment = 0.5m;
            AddRow(table, ref row, "发光宽度", _glowWidth);
            ConfigureColorButton(_glowColor);
            AddRow(table, ref row, "发光颜色", _glowColor);

            AddOptions(_textFillMode,
                new Choice("Solid", "纯色填充"),
                new Choice("VerticalGradient", "垂直渐变"));
            AddRow(table, ref row, "文字填充", _textFillMode);
            ConfigureColorButton(_gradientEndColor);
            AddRow(table, ref row, "渐变终色", _gradientEndColor);

            AddOptions(_horizontal,
                new Choice("Left", "左对齐"), new Choice("Center", "居中"), new Choice("Right", "右对齐"));
            AddRow(table, ref row, "水平对齐", _horizontal);
            AddOptions(_vertical,
                new Choice("Top", "顶部"), new Choice("Center", "居中"), new Choice("Bottom", "底部"));
            AddRow(table, ref row, "垂直对齐", _vertical);
            AddOptions(_background,
                new Choice("ContentAware", "内容感知修复"),
                new Choice("Gradient", "边缘渐变修补"),
                new Choice("Solid", "边缘纯色采样"),
                new Choice("Transparent", "清为透明"),
                new Choice("Keep", "保留原图"));
            AddRow(table, ref row, "清除背景", _background);

            _clearPadding.Minimum = 0;
            _clearPadding.Maximum = 30;
            AddRow(table, ref row, "清除外扩", _clearPadding);

            var reviewPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            _reviewed.Text = "已人工校对";
            _confidence.AutoSize = true;
            _confidence.Padding = new Padding(8, 4, 0, 0);
            reviewPanel.Controls.Add(_reviewed);
            reviewPanel.Controls.Add(_confidence);
            AddRow(table, ref row, "校对状态", reviewPanel);

            var applyStyle = new Button
            {
                Text = "将当前字体和样式应用到本图全部区域",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 4)
            };
            applyStyle.Click += (_, __) => ApplyStyleToCurrentImage();
            table.Controls.Add(applyStyle, 0, row);
            table.SetColumnSpan(applyStyle, 2);

            Controls.Add(table);
        }

        private void WireEvents()
        {
            _regionList.SelectedIndexChanged += (_, __) =>
            {
                if (_loading || !(_regionList.SelectedItem is RegionListEntry entry)) return;
                RegionSelected?.Invoke(this, new TextRegionEventArgs(entry.Region));
            };

            _stylePreset.SelectedIndexChanged += (_, __) => UpdatePresetButtons();
            _applyPreset.Click += (_, __) => ApplySelectedPreset();
            _savePreset.Click += (_, __) => SaveCurrentStylePreset();
            _deletePreset.Click += (_, __) => DeleteSelectedPreset();

            _sourceText.TextChanged += (_, __) => TranslationContentChanged();
            _translationText.TextChanged += (_, __) => TranslationContentChanged();
            _x.ValueChanged += (_, __) => InputChanged();
            _y.ValueChanged += (_, __) => InputChanged();
            _width.ValueChanged += (_, __) => InputChanged();
            _height.ValueChanged += (_, __) => InputChanged();
            _fontFamily.TextChanged += (_, __) => InputChanged();
            _fontSize.ValueChanged += (_, __) => InputChanged();
            _autoFit.CheckedChanged += (_, __) => InputChanged();
            _bold.CheckedChanged += (_, __) => InputChanged();
            _outlineWidth.ValueChanged += (_, __) => InputChanged();
            _letterSpacing.ValueChanged += (_, __) => InputChanged();
            _lineSpacing.ValueChanged += (_, __) => InputChanged();
            _verticalText.CheckedChanged += (_, __) => InputChanged();
            _rotation.ValueChanged += (_, __) => InputChanged();
            _shadowEnabled.CheckedChanged += (_, __) => InputChanged();
            _shadowOffsetX.ValueChanged += (_, __) => InputChanged();
            _shadowOffsetY.ValueChanged += (_, __) => InputChanged();
            _glowWidth.ValueChanged += (_, __) => InputChanged();
            _textFillMode.SelectedIndexChanged += (_, __) => InputChanged();
            _horizontal.SelectedIndexChanged += (_, __) => InputChanged();
            _vertical.SelectedIndexChanged += (_, __) => InputChanged();
            _background.SelectedIndexChanged += (_, __) => InputChanged();
            _clearPadding.ValueChanged += (_, __) => InputChanged();
            _reviewed.CheckedChanged += (_, __) => InputChanged();
            _textColor.Click += (_, __) => ChooseColor(_textColor);
            _outlineColor.Click += (_, __) => ChooseColor(_outlineColor);
            _shadowColor.Click += (_, __) => ChooseColor(_shadowColor);
            _glowColor.Click += (_, __) => ChooseColor(_glowColor);
            _gradientEndColor.Click += (_, __) => ChooseColor(_gradientEndColor);
        }

        private void InputChanged()
        {
            if (_loading || _region == null)
            {
                return;
            }

            _region.SourceText = _sourceText.Text;
            _region.Translation = _translationText.Text;
            _region.X = (int)_x.Value;
            _region.Y = (int)_y.Value;
            _region.Width = Math.Max(1, (int)_width.Value);
            _region.Height = Math.Max(1, (int)_height.Value);
            _region.FontFamily = string.IsNullOrWhiteSpace(_fontFamily.Text) ? "Microsoft YaHei" : _fontFamily.Text.Trim();
            _region.FontSize = (float)_fontSize.Value;
            _region.AutoFit = _autoFit.Checked;
            _region.Bold = _bold.Checked;
            _region.TextColorArgb = ((Color)_textColor.Tag).ToArgb();
            _region.OutlineColorArgb = ((Color)_outlineColor.Tag).ToArgb();
            _region.OutlineWidth = (float)_outlineWidth.Value;
            _region.LetterSpacing = (float)_letterSpacing.Value;
            _region.LineSpacing = (float)_lineSpacing.Value;
            _region.VerticalText = _verticalText.Checked;
            _region.RotationDegrees = (float)_rotation.Value;
            _region.ShadowEnabled = _shadowEnabled.Checked;
            _region.ShadowColorArgb = ((Color)_shadowColor.Tag).ToArgb();
            _region.ShadowOffsetX = (int)_shadowOffsetX.Value;
            _region.ShadowOffsetY = (int)_shadowOffsetY.Value;
            _region.GlowWidth = (float)_glowWidth.Value;
            _region.GlowColorArgb = ((Color)_glowColor.Tag).ToArgb();
            _region.TextFillMode = GetChoice(_textFillMode, "Solid");
            _region.GradientEndColorArgb = ((Color)_gradientEndColor.Tag).ToArgb();
            _region.HorizontalAlignment = GetChoice(_horizontal, "Center");
            _region.VerticalAlignment = GetChoice(_vertical, "Center");
            _region.BackgroundMode = GetChoice(_background, "Gradient");
            _region.ClearPadding = (int)_clearPadding.Value;
            _region.Reviewed = _reviewed.Checked;
            RefreshRegionList(_region);
            _editTimer.Stop();
            _editTimer.Start();
        }

        private void TranslationContentChanged()
        {
            if (!_loading && _region != null && _reviewed.Checked)
            {
                _reviewed.Checked = false;
                return;
            }
            InputChanged();
        }

        private void ApplyStyleToCurrentImage()
        {
            if (_region == null || _document == null)
            {
                return;
            }

            foreach (var target in _document.Regions.Where(target => !ReferenceEquals(target, _region)))
            {
                target.FontFamily = _region.FontFamily;
                target.FontSize = _region.FontSize;
                target.Bold = _region.Bold;
                target.AutoFit = _region.AutoFit;
                target.TextColorArgb = _region.TextColorArgb;
                target.OutlineColorArgb = _region.OutlineColorArgb;
                target.OutlineWidth = _region.OutlineWidth;
                target.LetterSpacing = _region.LetterSpacing;
                target.LineSpacing = _region.LineSpacing;
                target.VerticalText = _region.VerticalText;
                target.RotationDegrees = _region.RotationDegrees;
                target.ShadowEnabled = _region.ShadowEnabled;
                target.ShadowColorArgb = _region.ShadowColorArgb;
                target.ShadowOffsetX = _region.ShadowOffsetX;
                target.ShadowOffsetY = _region.ShadowOffsetY;
                target.GlowWidth = _region.GlowWidth;
                target.GlowColorArgb = _region.GlowColorArgb;
                target.TextFillMode = _region.TextFillMode;
                target.GradientEndColorArgb = _region.GradientEndColorArgb;
                target.HorizontalAlignment = _region.HorizontalAlignment;
                target.VerticalAlignment = _region.VerticalAlignment;
                target.BackgroundMode = _region.BackgroundMode;
                target.ClearPadding = _region.ClearPadding;
            }

            RegionEdited?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshStylePresets(string selectedName = null)
        {
            selectedName = selectedName ?? (_stylePreset.SelectedItem as TextStylePreset)?.Name;
            _loading = true;
            try
            {
                _stylePreset.BeginUpdate();
                _stylePreset.Items.Clear();
                foreach (var preset in _presetService.Presets)
                    _stylePreset.Items.Add(preset);
                _stylePreset.EndUpdate();

                var selectedIndex = -1;
                if (!string.IsNullOrWhiteSpace(selectedName))
                {
                    for (var index = 0; index < _stylePreset.Items.Count; index++)
                    {
                        if (_stylePreset.Items[index] is TextStylePreset preset &&
                            string.Equals(preset.Name, selectedName, StringComparison.CurrentCultureIgnoreCase))
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                }
                if (selectedIndex < 0 && _stylePreset.Items.Count > 0) selectedIndex = 0;
                _stylePreset.SelectedIndex = selectedIndex;
            }
            finally
            {
                _loading = false;
                UpdatePresetButtons();
            }
        }

        private void ApplySelectedPreset()
        {
            if (_region == null || !(_stylePreset.SelectedItem is TextStylePreset preset)) return;
            TextStylePresetService.Apply(preset, _region);
            SelectRegion(_region);
            RegionEdited?.Invoke(this, EventArgs.Empty);
        }

        private void SaveCurrentStylePreset()
        {
            if (_region == null) return;
            var selected = _stylePreset.SelectedItem as TextStylePreset;
            var suggestion = selected == null ? "我的样式" : selected.Name + " 副本";
            using (var dialog = new PresetNameDialog(suggestion))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var existing = _presetService.Presets.FirstOrDefault(preset =>
                    string.Equals(preset.Name, dialog.PresetName, StringComparison.CurrentCultureIgnoreCase));
                if (existing != null && MessageBox.Show(this,
                        $"预设“{existing.Name}”已经存在，是否用当前样式覆盖？",
                        "覆盖同名预设", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    var saved = _presetService.Upsert(dialog.PresetName, _region);
                    _presetService.Save();
                    RefreshStylePresets(saved.Name);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, "无法保存文字样式预设：\r\n" + exception.Message,
                        "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteSelectedPreset()
        {
            if (!(_stylePreset.SelectedItem is TextStylePreset preset)) return;
            if (MessageBox.Show(this, $"确定删除文字样式预设“{preset.Name}”吗？",
                    "删除预设", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                _presetService.Delete(preset.Name);
                _presetService.Save();
                RefreshStylePresets();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "无法删除文字样式预设：\r\n" + exception.Message,
                    "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdatePresetButtons()
        {
            var hasPreset = _stylePreset.SelectedItem is TextStylePreset;
            _stylePreset.Enabled = _stylePreset.Items.Count > 0;
            _applyPreset.Enabled = _region != null && hasPreset;
            _savePreset.Enabled = _region != null;
            _deletePreset.Enabled = hasPreset;
        }

        private void ChooseColor(Button button)
        {
            using (var dialog = new ColorDialog
            {
                Color = (Color)button.Tag,
                FullOpen = true,
                AnyColor = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetColorButton(button, dialog.Color);
                    InputChanged();
                }
            }
        }

        private void ClearInputs()
        {
            _sourceText.Clear();
            _translationText.Clear();
            _confidence.Text = string.Empty;
        }

        private static NumericUpDown CreateCoordinateInput()
        {
            return new NumericUpDown { Minimum = 0, Maximum = 100000, Width = 72 };
        }

        private static void AddFullWidthText(TableLayoutPanel table, ref int row, string label, TextBox box)
        {
            var title = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 3) };
            table.Controls.Add(title, 0, row);
            table.SetColumnSpan(title, 2);
            row++;
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Height = 64;
            box.Dock = DockStyle.Fill;
            table.Controls.Add(box, 0, row);
            table.SetColumnSpan(box, 2);
            row++;
        }

        private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control)
        {
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 4, 3)
            }, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 4, 0, 3);
            table.Controls.Add(control, 1, row);
            row++;
        }

        private static void AddLabeledInput(FlowLayoutPanel panel, string label, Control input)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            panel.Controls.Add(input);
        }

        private static void ConfigureColorButton(Button button)
        {
            button.Text = "选择颜色";
            button.AutoSize = false;
            button.Height = 27;
            SetColorButton(button, Color.White);
        }

        private static void SetColorButton(Button button, Color color)
        {
            button.Tag = color;
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() < 0.45f ? Color.White : Color.Black;
            button.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static void AddOptions(ComboBox comboBox, params Choice[] choices)
        {
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Items.AddRange(choices);
        }

        private static void SelectOption(ComboBox comboBox, string value)
        {
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.Items[index] is Choice choice && choice.Value == value)
                {
                    comboBox.SelectedIndex = index;
                    return;
                }
            }

            if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
        }

        private static string GetChoice(ComboBox comboBox, string fallback)
        {
            return comboBox.SelectedItem is Choice choice ? choice.Value : fallback;
        }

        private static void SetNumeric(NumericUpDown input, decimal value)
        {
            input.Value = Math.Max(input.Minimum, Math.Min(input.Maximum, value));
        }

        private sealed class Choice
        {
            public Choice(string value, string label)
            {
                Value = value;
                Label = label;
            }

            public string Value { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }

        private sealed class RegionListEntry
        {
            public RegionListEntry(TextRegion region, int index)
            {
                Region = region;
                Index = index;
            }

            public TextRegion Region { get; }
            private int Index { get; }

            public override string ToString()
            {
                var source = string.IsNullOrWhiteSpace(Region.SourceText) ? "（未录入原文）" : Region.SourceText.Replace("\r", " ").Replace("\n", " ");
                var translation = string.IsNullOrWhiteSpace(Region.Translation) ? "待翻译" : Region.Translation.Replace("\r", " ").Replace("\n", " ");
                if (source.Length > 16) source = source.Substring(0, 16) + "…";
                if (translation.Length > 16) translation = translation.Substring(0, 16) + "…";
                return $"[{Index}] {source}  →  {translation}";
            }
        }
    }
}
