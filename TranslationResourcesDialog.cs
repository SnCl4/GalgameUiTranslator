using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class TranslationResourcesDialog : Form
    {
        private readonly BindingList<TranslationMemoryEntry> _memory;
        private readonly BindingList<GlossaryEntry> _glossary;
        private readonly TranslationProject _project;
        private readonly DataGridView _memoryGrid = CreateGrid();
        private readonly DataGridView _glossaryGrid = CreateGrid();
        private readonly Label _summary = new Label();

        public TranslationResourcesDialog(TranslationResourceData resources, TranslationProject project)
        {
            resources = resources ?? new TranslationResourceData();
            _project = project;
            _memory = new BindingList<TranslationMemoryEntry>(
                (resources.Memory ?? new System.Collections.Generic.List<TranslationMemoryEntry>())
                .Select(CloneMemory).ToList());
            _glossary = new BindingList<GlossaryEntry>(
                (resources.Glossary ?? new System.Collections.Generic.List<GlossaryEntry>())
                .Select(CloneGlossary).ToList());

            Text = "翻译记忆与术语表";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 590);
            Size = new Size(980, 700);
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildInterface();
            _memory.ListChanged += (_, __) => UpdateSummary();
            _glossary.ListChanged += (_, __) => UpdateSummary();
            UiTheme.Apply(this);
            ApplyGridTheme(_memoryGrid);
            ApplyGridTheme(_glossaryGrid);
            UpdateSummary();
        }

        public TranslationResourceData Resources { get; private set; }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackground,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2
            };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            header.Controls.Add(new Label
            {
                Text = "翻译记忆与术语表",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(17f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "完全相同的日文会优先复用已校对译文；相关术语会随翻译请求发送给 API。",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(9f),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            root.Controls.Add(header, 0, 0);

            ConfigureMemoryGrid();
            ConfigureGlossaryGrid();
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(CreateMemoryPage());
            tabs.TabPages.Add(CreateGlossaryPage());
            root.Controls.Add(tabs, 0, 1);

            _summary.Dock = DockStyle.Fill;
            _summary.ForeColor = UiTheme.TextSecondary;
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_summary, 0, 2);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0),
                BackColor = Color.Transparent
            };
            var save = new ModernButton { Text = "保存", Width = 104, Height = 38, AccentStyle = true };
            var cancel = new ModernButton { Text = "取消", Width = 104, Height = 38, DialogResult = DialogResult.Cancel };
            save.Click += (_, __) => SaveAndClose();
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            root.Controls.Add(footer, 0, 3);

            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(root);
        }

        private TabPage CreateMemoryPage()
        {
            var page = CreateTabPage("翻译记忆");
            var layout = CreateTabLayout();
            var actions = CreateActionPanel();
            var collect = CreateActionButton("收录当前工程已校对译文", 210);
            var add = CreateActionButton("新增", 82);
            var remove = CreateActionButton("删除选中", 104);
            var clear = CreateActionButton("清空", 82);
            collect.Enabled = _project != null;
            collect.Click += (_, __) => CollectReviewedFromProject();
            add.Click += (_, __) => AddMemoryRow();
            remove.Click += (_, __) => RemoveSelected(_memoryGrid, _memory);
            clear.Click += (_, __) => ClearMemory();
            actions.Controls.Add(collect);
            actions.Controls.Add(add);
            actions.Controls.Add(remove);
            actions.Controls.Add(clear);
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(_memoryGrid, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage CreateGlossaryPage()
        {
            var page = CreateTabPage("术语表");
            var layout = CreateTabLayout();
            var actions = CreateActionPanel();
            var add = CreateActionButton("新增术语", 104);
            var remove = CreateActionButton("删除选中", 104);
            var clear = CreateActionButton("清空", 82);
            add.Click += (_, __) => AddGlossaryRow();
            remove.Click += (_, __) => RemoveSelected(_glossaryGrid, _glossary);
            clear.Click += (_, __) => ClearGlossary();
            actions.Controls.Add(add);
            actions.Controls.Add(remove);
            actions.Controls.Add(clear);
            actions.Controls.Add(new Label
            {
                Text = "只会发送当前批次原文中实际出现的术语",
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(14, 10, 0, 0)
            });
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(_glossaryGrid, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private void ConfigureMemoryGrid()
        {
            _memoryGrid.AutoGenerateColumns = false;
            _memoryGrid.Columns.Add(CreateTextColumn("日文原文", nameof(TranslationMemoryEntry.Source), 40));
            _memoryGrid.Columns.Add(CreateTextColumn("中文译文", nameof(TranslationMemoryEntry.Translation), 40));
            _memoryGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "复用次数",
                DataPropertyName = nameof(TranslationMemoryEntry.UseCount),
                Width = 84,
                ReadOnly = true
            });
            _memoryGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "更新时间",
                DataPropertyName = nameof(TranslationMemoryEntry.UpdatedAt),
                Width = 142,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm" }
            });
            _memoryGrid.DataSource = _memory;
        }

        private void ConfigureGlossaryGrid()
        {
            _glossaryGrid.AutoGenerateColumns = false;
            _glossaryGrid.Columns.Add(CreateTextColumn("日文术语", nameof(GlossaryEntry.Source), 31));
            _glossaryGrid.Columns.Add(CreateTextColumn("固定译法", nameof(GlossaryEntry.Translation), 31));
            _glossaryGrid.Columns.Add(CreateTextColumn("备注 / 使用场景", nameof(GlossaryEntry.Note), 38));
            _glossaryGrid.DataSource = _glossary;
        }

        private void CollectReviewedFromProject()
        {
            if (_project == null) return;
            var changed = 0;
            foreach (var region in _project.Images.SelectMany(image => image.Regions)
                         .Where(region => region.Reviewed &&
                                          !string.IsNullOrWhiteSpace(region.SourceText) &&
                                          !string.IsNullOrWhiteSpace(region.Translation)))
            {
                var key = TranslationResourceService.Normalize(region.SourceText);
                var existing = _memory.LastOrDefault(entry =>
                    TranslationResourceService.Normalize(entry.Source) == key);
                if (existing == null)
                {
                    _memory.Add(new TranslationMemoryEntry
                    {
                        Source = region.SourceText.Trim(),
                        Translation = region.Translation.Trim(),
                        UpdatedAt = DateTime.Now
                    });
                    changed++;
                }
                else if (!string.Equals(existing.Translation, region.Translation.Trim(), StringComparison.Ordinal))
                {
                    existing.Translation = region.Translation.Trim();
                    existing.UpdatedAt = DateTime.Now;
                    changed++;
                }
            }
            _memoryGrid.Refresh();
            UpdateSummary($"已从当前工程收录或更新 {changed} 条已校对译文");
        }

        private void AddMemoryRow()
        {
            _memory.Add(new TranslationMemoryEntry { UpdatedAt = DateTime.Now });
            SelectLastRow(_memoryGrid);
            UpdateSummary();
        }

        private void AddGlossaryRow()
        {
            _glossary.Add(new GlossaryEntry());
            SelectLastRow(_glossaryGrid);
            UpdateSummary();
        }

        private void ClearMemory()
        {
            if (_memory.Count == 0 || MessageBox.Show(this, "确定清空全部翻译记忆吗？", "确认清空",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _memory.Clear();
            UpdateSummary();
        }

        private void ClearGlossary()
        {
            if (_glossary.Count == 0 || MessageBox.Show(this, "确定清空全部术语吗？", "确认清空",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _glossary.Clear();
            UpdateSummary();
        }

        private void SaveAndClose()
        {
            _memoryGrid.EndEdit();
            _glossaryGrid.EndEdit();
            Resources = new TranslationResourceData
            {
                Memory = _memory.Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Translation))
                    .ToList(),
                Glossary = _glossary.Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Translation))
                    .ToList()
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateSummary(string message = null)
        {
            _summary.Text = string.IsNullOrWhiteSpace(message)
                ? $"翻译记忆 {_memory.Count} 条  |  术语 {_glossary.Count} 条  |  空白行会在保存时自动忽略"
                : message;
        }

        private static TableLayoutPanel CreateTabLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.CardBackground,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            return layout;
        }

        private static FlowLayoutPanel CreateActionPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 3, 0, 4)
            };
        }

        private static ModernButton CreateActionButton(string text, int width)
        {
            return new ModernButton { Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 8, 0) };
        }

        private static TabPage CreateTabPage(string text)
        {
            return new TabPage(text) { BackColor = UiTheme.CardBackground, Padding = new Padding(3) };
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string title, string property, float weight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                DataPropertyName = property,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight
            };
        }

        private static DataGridView CreateGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = UiTheme.InputBackground,
                GridColor = UiTheme.BorderSoft,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
        }

        private static void ApplyGridTheme(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.CardBackgroundLight,
                ForeColor = UiTheme.TextPrimary,
                SelectionBackColor = UiTheme.CardBackgroundLight,
                SelectionForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(9f, FontStyle.Bold),
                Padding = new Padding(5)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.InputBackground,
                ForeColor = UiTheme.TextPrimary,
                SelectionBackColor = UiTheme.AccentDark,
                SelectionForeColor = Color.White,
                Font = UiTheme.CreateFont(9f),
                Padding = new Padding(4)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.CardBackground;
            grid.ColumnHeadersHeight = 38;
            grid.RowTemplate.Height = 34;
        }

        private static void RemoveSelected<T>(DataGridView grid, BindingList<T> list)
        {
            var selected = grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem)
                .OfType<T>()
                .Distinct()
                .ToList();
            foreach (var item in selected) list.Remove(item);
        }

        private static void SelectLastRow(DataGridView grid)
        {
            if (grid.Rows.Count == 0) return;
            var row = grid.Rows[grid.Rows.Count - 1];
            row.Selected = true;
            grid.CurrentCell = row.Cells[0];
            grid.BeginEdit(true);
        }

        private static TranslationMemoryEntry CloneMemory(TranslationMemoryEntry entry)
        {
            return new TranslationMemoryEntry
            {
                Source = entry.Source,
                Translation = entry.Translation,
                UseCount = entry.UseCount,
                UpdatedAt = entry.UpdatedAt
            };
        }

        private static GlossaryEntry CloneGlossary(GlossaryEntry entry)
        {
            return new GlossaryEntry
            {
                Source = entry.Source,
                Translation = entry.Translation,
                Note = entry.Note
            };
        }
    }
}
