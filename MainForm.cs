using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    public sealed class MainForm : Form
    {
        private readonly VisionApiClient _apiClient = new VisionApiClient();
        private readonly ImageCanvas _canvas = new ImageCanvas();
        private readonly RegionEditorPanel _editor = new RegionEditorPanel { Name = "WorkspaceRegionEditor" };
        private readonly ListBox _imageList = new ListBox();
        private readonly TextBox _searchBox = new TextBox();
        private readonly ComboBox _imageStatusFilter = new ComboBox();
        private readonly ImageThumbnailCache _thumbnailCache = new ImageThumbnailCache();
        private readonly Font _imageItemTitleFont = UiTheme.CreateFont(9.5f, FontStyle.Bold);
        private readonly Font _imageItemDetailFont = UiTheme.CreateFont(8f);
        private readonly Font _imageItemStatusFont = UiTheme.CreateFont(8f, FontStyle.Bold);
        private readonly ToolStrip _toolStrip = new ToolStrip();
        private readonly ToolStripButton _openFolderButton = new ToolStripButton("导入") { ToolTipText = "导入解包后的 UI 图片目录" };
        private readonly ToolStripButton _openProjectButton = new ToolStripButton("工程") { ToolTipText = "打开已有汉化工程" };
        private readonly ToolStripButton _saveButton = new ToolStripButton("保存");
        private readonly ToolStripButton _undoButton = new ToolStripButton("撤销") { ToolTipText = "撤销上一步（Ctrl+Z）" };
        private readonly ToolStripButton _redoButton = new ToolStripButton("重做") { ToolTipText = "重做下一步（Ctrl+Y）" };
        private readonly ToolStripButton _visionButton = new ToolStripButton("识图") { ToolTipText = "识别当前图片" };
        private readonly ToolStripButton _visionBatchButton = new ToolStripButton("批量识图");
        private readonly ToolStripButton _translateButton = new ToolStripButton("翻译") { ToolTipText = "翻译当前图片" };
        private readonly ToolStripButton _translateAllButton = new ToolStripButton("批量翻译");
        private readonly ToolStripButton _translationResourcesButton = new ToolStripButton("术语库") { ToolTipText = "管理翻译记忆与固定术语" };
        private readonly ToolStripButton _drawButton = new ToolStripButton("框选") { CheckOnClick = true, ToolTipText = "在图片上拖动创建文字区域" };
        private readonly ToolStripButton _maskBrushButton = new ToolStripButton("蒙版笔") { CheckOnClick = true, ToolTipText = "在选中文字区域上绘制需要修复的像素" };
        private readonly ToolStripButton _maskEraserButton = new ToolStripButton("蒙版擦") { CheckOnClick = true, ToolTipText = "擦除当前文字区域的修复蒙版" };
        private readonly ToolStripDropDownButton _maskSizeButton = new ToolStripDropDownButton("笔刷 18");
        private readonly ToolStripButton _maskClearButton = new ToolStripButton("清蒙版") { ToolTipText = "清除选中文字区域的自定义蒙版" };
        private readonly ToolStripButton _deleteButton = new ToolStripButton("删除") { ToolTipText = "删除选中的文字区域" };
        private readonly ToolStripButton _previewButton = new ToolStripButton("预览") { CheckOnClick = true };
        private readonly ToolStripButton _compareButton = new ToolStripButton("原图对比") { CheckOnClick = true, ToolTipText = "拖动分界线对比原图与汉化效果" };
        private readonly ToolStripButton _atlasButton = new ToolStripButton("图集框") { CheckOnClick = true, ToolTipText = "显示 TexturePacker/Spine 精灵边界" };
        private readonly ToolStripButton _fitButton = new ToolStripButton("适应") { ToolTipText = "缩放图片以适应画布" };
        private readonly ToolStripButton _preflightButton = new ToolStripButton("预检") { ToolTipText = "检查空译文、越界、溢出和字体问题" };
        private readonly ToolStripButton _exportButton = new ToolStripButton("导出") { ToolTipText = "批量导出汉化图片" };
        private readonly ToolStripButton _settingsButton = new ToolStripButton("API设置");
        private readonly ToolStripButton _cancelOperationButton = new ToolStripButton("取消") { Enabled = false };
        private readonly ToolStripButton _helpButton = new ToolStripButton("说明");
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel("从首页导入解包后的 UI 图片，或打开已有工程");
        private readonly ToolStripStatusLabel _zoomLabel = new ToolStripStatusLabel();
        private readonly Panel _homePage = new Panel();
        private readonly Panel _workspacePage = new Panel();
        private readonly BatchTaskCenter _batchTaskCenter = new BatchTaskCenter();
        private readonly BatchTaskPage _batchPage;
        private readonly TranslationResourceService _translationResources;
        private readonly Panel _contentHost = new Panel();
        private readonly NavButton _homeNavButton = new NavButton { Text = "◆   首页概览" };
        private readonly NavButton _workspaceNavButton = new NavButton { Text = "▣   图片工作台" };
        private readonly NavButton _batchNavButton = new NavButton { Text = "⇄   批量处理" };
        private readonly NavButton _apiNavButton = new NavButton { Text = "⚙   API 与模型" };
        private readonly NavButton _helpNavButton = new NavButton { Text = "?    使用说明" };
        private readonly Label _sidebarProjectLabel = new Label();
        private readonly Label _dashboardImageCount = new Label();
        private readonly Label _dashboardRegionCount = new Label();
        private readonly Label _dashboardTranslatedCount = new Label();
        private readonly Label _dashboardReviewedCount = new Label();
        private readonly Label _dashboardProjectName = new Label();
        private readonly Label _dashboardProjectPath = new Label();
        private readonly Label _dashboardProgress = new Label();
        private NavButton _activeNavigation;
        private TranslationProject _project;
        private ImageDocument _currentDocument;
        private AppSettings _settings;
        private string _projectPath;
        private string _visionApiKey = string.Empty;
        private string _translationApiKey = string.Empty;
        private readonly ProjectHistory _history = new ProjectHistory(30);
        private readonly System.Windows.Forms.Timer _historyTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _autosaveTimer = new System.Windows.Forms.Timer();
        private string _currentAutosavePath = string.Empty;
        private bool _dirty;
        private bool _loadingImageList;
        private bool _busy;
        private bool _restoringHistory;
        private bool _updatingEditModes;
        private bool _suppressBatchPersistence;
        private string _batchQueuePath = string.Empty;
        private DateTime _lastBatchCheckpointUtc = DateTime.MinValue;
        private readonly bool _suppressRecoveryPrompt;
        private CancellationTokenSource _operationCancellation;

        public MainForm(bool suppressRecoveryPrompt = false)
        {
            _suppressRecoveryPrompt = suppressRecoveryPrompt;
            _batchPage = new BatchTaskPage(_batchTaskCenter);
            _translationResources = TranslationResourceService.LoadDefault();
            _settings = ProjectService.LoadSettings();
            Text = "Galgame UI 图片汉化工具";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(1100, 700);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            BuildInterface();
            WireEvents();
            UiTheme.Apply(this);
            _canvas.DefaultFontFamily = _settings.DefaultFontFamily;
            _editor.RefreshFontNames(_settings.DefaultFontFamily);
            ShowHomePage();
            UpdateDashboard();
            UpdateCommandState();
            _historyTimer.Interval = 450;
            _historyTimer.Tick += (_, __) =>
            {
                _historyTimer.Stop();
                CaptureHistorySnapshot();
                PersistReviewedTranslations();
            };
            _autosaveTimer.Interval = 30000;
            _autosaveTimer.Tick += (_, __) => AutoSaveProject();
            _autosaveTimer.Start();
            if (!_suppressRecoveryPrompt)
                Shown += (_, __) => TryOfferLatestAutosaveRecovery();
        }

        private void BuildInterface()
        {
            BackColor = UiTheme.WindowBackground;

            var statusStrip = new StatusStrip
            {
                BackColor = UiTheme.SidebarBackground,
                ForeColor = UiTheme.TextSecondary,
                SizingGrip = false,
                Padding = new Padding(10, 2, 8, 2)
            };
            _statusLabel.Spring = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.ForeColor = UiTheme.TextSecondary;
            _zoomLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _zoomLabel.BorderStyle = Border3DStyle.Flat;
            _zoomLabel.ForeColor = UiTheme.TextSecondary;
            statusStrip.Items.Add(_statusLabel);
            statusStrip.Items.Add(_zoomLabel);

            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackground,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            shell.Controls.Add(BuildSidebar(), 0, 0);

            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = UiTheme.WindowBackground;
            _contentHost.Padding = new Padding(22, 20, 22, 18);
            shell.Controls.Add(_contentHost, 1, 0);

            BuildHomePage();
            BuildWorkspacePage();
            _contentHost.Controls.Add(_workspacePage);
            _contentHost.Controls.Add(_batchPage);
            _contentHost.Controls.Add(_homePage);

            Controls.Add(shell);
            Controls.Add(statusStrip);
        }

        private Control BuildSidebar()
        {
            var sidebar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.SidebarBackground,
                Padding = new Padding(10, 0, 10, 0)
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 326));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));

            var brand = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Padding = new Padding(12, 25, 4, 15)
            };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            var logo = new Label
            {
                Text = "UI",
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Accent,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiTheme.CreateFont(13f, FontStyle.Bold),
                Margin = new Padding(0, 7, 10, 7)
            };
            var brandText = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 2 };
            brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            brandText.Controls.Add(new Label
            {
                Text = "GalUI Localizer",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(11f, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            brandText.Controls.Add(new Label
            {
                Text = "UI IMAGE LOCALIZER",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);
            brand.Controls.Add(logo, 0, 0);
            brand.Controls.Add(brandText, 1, 0);
            layout.Controls.Add(brand, 0, 0);

            var navigation = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 6
            };
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            for (var index = 0; index < 5; index++) navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            navigation.Controls.Add(new Label
            {
                Text = "功能导航",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Padding = new Padding(18, 8, 0, 0),
                Font = UiTheme.CreateFont(8f, FontStyle.Bold)
            }, 0, 0);
            var navButtons = new[] { _homeNavButton, _workspaceNavButton, _batchNavButton, _apiNavButton, _helpNavButton };
            for (var index = 0; index < navButtons.Length; index++)
            {
                navButtons[index].Dock = DockStyle.Fill;
                navigation.Controls.Add(navButtons[index], 0, index + 1);
            }
            layout.Controls.Add(navigation, 0, 1);

            var projectArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(6, 16, 6, 12) };
            var projectCard = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = 88,
                CornerRadius = 15,
                Padding = new Padding(14),
                BorderColor = UiTheme.BorderSoft
            };
            projectCard.Controls.Add(_sidebarProjectLabel);
            _sidebarProjectLabel.Dock = DockStyle.Fill;
            _sidebarProjectLabel.Text = "当前工程\r\n尚未打开项目";
            _sidebarProjectLabel.ForeColor = UiTheme.TextSecondary;
            _sidebarProjectLabel.Font = UiTheme.CreateFont(9f);
            _sidebarProjectLabel.TextAlign = ContentAlignment.MiddleLeft;
            projectArea.Controls.Add(projectCard);
            layout.Controls.Add(projectArea, 0, 2);

            var versionPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(10, 12, 10, 10) };
            versionPanel.Controls.Add(new Label
            {
                Text = "GalUI Localizer  v" + GetAppVersion() + "\r\n本地非破坏性图片汉化",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8f),
                TextAlign = ContentAlignment.MiddleCenter
            });
            layout.Controls.Add(versionPanel, 0, 3);
            sidebar.Controls.Add(layout);
            return sidebar;
        }

        private void BuildHomePage()
        {
            _homePage.Dock = DockStyle.Fill;
            _homePage.BackColor = UiTheme.WindowBackground;

            var pageLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2
            };
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var hero = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 16),
                Padding = new Padding(28, 22, 28, 22),
                CornerRadius = 24
            };
            var heroLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2
            };
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));

            var heroText = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 5 };
            heroText.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            heroText.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            heroText.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            heroText.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            heroText.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            heroText.Controls.Add(new Label
            {
                Text = "UI IMAGE LOCALIZATION CONSOLE",
                AutoSize = true,
                ForeColor = UiTheme.Accent,
                Font = UiTheme.CreateFont(8.5f, FontStyle.Bold),
                Padding = new Padding(0, 5, 0, 0)
            }, 0, 0);
            heroText.Controls.Add(new Label
            {
                Text = "Galgame UI 图片汉化",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(19f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            heroText.Controls.Add(new Label
            {
                Text = "识别、翻译、修复背景并按原尺寸批量导出",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(10f)
            }, 0, 2);
            heroText.Controls.Add(new Label
            {
                Text = string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8.5f),
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 8, 0, 0)
            }, 0, 3);
            var heroButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false };
            var openFolder = new ModernButton { Text = "导入 UI 图片", Width = 150, AccentStyle = true };
            var openProject = new ModernButton { Text = "打开已有工程", Width = 150 };
            openFolder.Click += (_, __) => OpenImageFolder();
            openProject.Click += (_, __) => OpenProject();
            heroButtons.Controls.Add(openFolder);
            heroButtons.Controls.Add(openProject);
            heroText.Controls.Add(heroButtons, 0, 4);
            heroLayout.Controls.Add(heroText, 0, 0);

            var statsCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(12, 22, 0, 22),
                Padding = new Padding(10),
                CornerRadius = 18,
                BackColor = UiTheme.InputBackground,
                BorderColor = UiTheme.Border
            };
            var stats = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 4 };
            for (var index = 0; index < 4; index++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            stats.Controls.Add(CreateStatBlock(_dashboardImageCount, "图片"), 0, 0);
            stats.Controls.Add(CreateStatBlock(_dashboardRegionCount, "文字区域"), 1, 0);
            stats.Controls.Add(CreateStatBlock(_dashboardTranslatedCount, "已翻译"), 2, 0);
            stats.Controls.Add(CreateStatBlock(_dashboardReviewedCount, "已校对"), 3, 0);
            statsCard.Controls.Add(stats);
            heroLayout.Controls.Add(statsCard, 1, 0);
            hero.Controls.Add(heroLayout);
            pageLayout.Controls.Add(hero, 0, 0);

            var cards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1
            };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            cards.Controls.Add(BuildQuickStartCard(), 0, 0);
            cards.Controls.Add(BuildProjectSummaryCard(), 1, 0);
            cards.Controls.Add(BuildWorkflowCard(), 2, 0);
            pageLayout.Controls.Add(cards, 0, 1);
            _homePage.Controls.Add(pageLayout);
        }

        private static string GetAppVersion()
        {
            var version = typeof(MainForm).Assembly.GetName().Version;
            return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private Control CreateStatBlock(Label valueLabel, string caption)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 2, Margin = new Padding(4) };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            valueLabel.Text = "0";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.BottomCenter;
            valueLabel.Font = UiTheme.CreateFont(18f, FontStyle.Bold);
            valueLabel.ForeColor = UiTheme.Accent;
            panel.Controls.Add(valueLabel, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8f)
            }, 0, 1);
            return panel;
        }

        private Control BuildQuickStartCard()
        {
            var card = CreateDashboardCard("开始处理", "导入解包后的游戏 UI 图片", new Padding(0, 0, 12, 0));
            var actions = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 118, BackColor = Color.Transparent, RowCount = 2, Padding = new Padding(2, 8, 2, 2) };
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            var open = new ModernButton { Text = "打开图片目录", Dock = DockStyle.Fill, AccentStyle = true, Margin = new Padding(0, 0, 0, 7) };
            var project = new ModernButton { Text = "打开工程文件", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0) };
            open.Click += (_, __) => OpenImageFolder();
            project.Click += (_, __) => OpenProject();
            actions.Controls.Add(open, 0, 0);
            actions.Controls.Add(project, 0, 1);
            card.Controls.Add(actions);
            ((Control)card.Tag).BringToFront();
            return card;
        }

        private Control BuildProjectSummaryCard()
        {
            var card = CreateDashboardCard("当前工程", "工程状态与资源目录", new Padding(6, 0, 6, 0));
            card.Controls.Clear();
            card.Tag = null;
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 6, Padding = new Padding(2) };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            content.Controls.Add(new Label
            {
                Text = "当前工程",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(14f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            content.Controls.Add(new Label
            {
                Text = "工程状态与资源目录",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8.5f)
            }, 0, 1);
            _dashboardProjectName.Dock = DockStyle.Fill;
            _dashboardProjectName.ForeColor = UiTheme.TextPrimary;
            _dashboardProjectName.Font = UiTheme.CreateFont(11f, FontStyle.Bold);
            _dashboardProjectName.Text = "尚未打开工程";
            _dashboardProjectPath.Dock = DockStyle.Fill;
            _dashboardProjectPath.ForeColor = UiTheme.TextSecondary;
            _dashboardProjectPath.Font = UiTheme.CreateFont(8.5f);
            _dashboardProjectPath.AutoEllipsis = true;
            _dashboardProgress.Dock = DockStyle.Fill;
            _dashboardProgress.ForeColor = UiTheme.Success;
            _dashboardProgress.Font = UiTheme.CreateFont(9f, FontStyle.Bold);
            content.Controls.Add(_dashboardProjectName, 0, 2);
            content.Controls.Add(_dashboardProjectPath, 0, 3);
            content.Controls.Add(_dashboardProgress, 0, 4);
            var enter = new ModernButton { Text = "进入图片工作台", Width = 160, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            enter.Click += (_, __) => ShowWorkspacePage(_workspaceNavButton);
            content.Controls.Add(enter, 0, 5);
            card.Controls.Add(content);
            return card;
        }

        private Control BuildWorkflowCard()
        {
            var card = CreateDashboardCard("推荐流程", "从原图到可回封资源", new Padding(12, 0, 0, 0));
            card.Controls.Clear();
            card.Tag = null;
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 3, Padding = new Padding(2) };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            content.Controls.Add(new Label
            {
                Text = "推荐流程",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(14f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            content.Controls.Add(new Label
            {
                Text = "从原图到可回封资源",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8.5f)
            }, 0, 1);
            content.Controls.Add(new Label
            {
                Text = "01  导入图片并建立工程\r\n02  AI 识别或手工框选日文\r\n03  DeepSeek 翻译并人工校对\r\n04  调整字体、描边与背景修补\r\n05  预览后按原尺寸批量导出",
                Dock = DockStyle.Fill,
                Padding = new Padding(4, 10, 4, 4),
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(9f),
                TextAlign = ContentAlignment.TopLeft
            }, 0, 2);
            card.Controls.Add(content);
            return card;
        }

        private CardPanel CreateDashboardCard(string title, string subtitle, Padding margin)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = margin, Padding = new Padding(22), CornerRadius = 22 };
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 70, BackColor = Color.Transparent, RowCount = 2 };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            header.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(14f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8.5f)
            }, 0, 1);
            card.Controls.Add(header);
            card.Tag = header;
            return card;
        }

        private void BuildWorkspacePage()
        {
            _workspacePage.Dock = DockStyle.Fill;
            _workspacePage.BackColor = UiTheme.WindowBackground;
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 3
            };
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            header.Controls.Add(new Label
            {
                Text = "图片汉化工作台",
                Dock = DockStyle.Top,
                Height = 42,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(19f, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft
            });
            header.Controls.Add(new Label
            {
                Text = "支持蒙版修复、高级文字样式、DDS 与纹理图集原坐标编辑",
                Dock = DockStyle.Bottom,
                Height = 28,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(9f)
            });
            page.Controls.Add(header, 0, 0);

            _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            _toolStrip.Renderer = new DarkToolStripRenderer();
            _toolStrip.BackColor = UiTheme.CardBackground;
            _toolStrip.ForeColor = UiTheme.TextSecondary;
            _toolStrip.AutoSize = false;
            _toolStrip.Height = 42;
            _toolStrip.Dock = DockStyle.Fill;
            _toolStrip.Padding = new Padding(7, 3, 7, 3);
            _toolStrip.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
            foreach (var diameter in new[] { 8, 18, 32, 48, 72 })
            {
                var item = new ToolStripMenuItem(diameter + " px") { Tag = diameter };
                item.Click += (_, __) =>
                {
                    _canvas.SetMaskBrushSize(diameter);
                    _maskSizeButton.Text = "笔刷 " + diameter;
                    _statusLabel.Text = $"蒙版笔刷直径：{diameter} 像素";
                };
                _maskSizeButton.DropDownItems.Add(item);
            }
            _toolStrip.Items.AddRange(new ToolStripItem[]
            {
                _openFolderButton, _openProjectButton, _saveButton, _undoButton, _redoButton,
                new ToolStripSeparator(),
                _visionButton, _visionBatchButton, _translateButton, _translateAllButton, _translationResourcesButton,
                new ToolStripSeparator(),
                _drawButton, _deleteButton, _maskBrushButton, _maskEraserButton, _maskSizeButton, _maskClearButton,
                _previewButton, _compareButton, _atlasButton, _fitButton,
                new ToolStripSeparator(),
                _preflightButton, _exportButton, _cancelOperationButton
            });
            foreach (ToolStripItem item in _toolStrip.Items)
            {
                item.Margin = new Padding(2, 1, 2, 1);
                item.Padding = new Padding(4, 2, 4, 2);
            }
            page.Controls.Add(_toolStrip, 0, 1);

            var rootSplit = new SplitContainer
            {
                Name = "WorkspaceRootSplit",
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Size = new Size(1200, 700),
                SplitterDistance = 286,
                Panel1MinSize = 220,
                Panel2MinSize = 480,
                SplitterWidth = 10,
                BackColor = UiTheme.WindowBackground,
                Margin = new Padding(0, 10, 0, 0)
            };

            var imageCard = new CardPanel { Dock = DockStyle.Fill, CornerRadius = 17, Padding = new Padding(13) };
            var imageLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 4 };
            imageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            imageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            imageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            imageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            imageLayout.Controls.Add(new Label
            {
                Text = "图片资源",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            _searchBox.PlaceholderText = "搜索文件名";
            _searchBox.Dock = DockStyle.Fill;
            _searchBox.Margin = new Padding(0, 0, 0, 8);
            imageLayout.Controls.Add(_searchBox, 0, 1);
            _imageStatusFilter.Name = "ImageStatusFilter";
            _imageStatusFilter.Dock = DockStyle.Fill;
            _imageStatusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _imageStatusFilter.Margin = new Padding(0, 0, 0, 8);
            imageLayout.Controls.Add(_imageStatusFilter, 0, 2);
            _imageList.Name = "ImageThumbnailList";
            _imageList.Dock = DockStyle.Fill;
            _imageList.HorizontalScrollbar = false;
            _imageList.IntegralHeight = false;
            _imageList.DrawMode = DrawMode.OwnerDrawFixed;
            _imageList.ItemHeight = 72;
            imageLayout.Controls.Add(_imageList, 0, 3);
            imageCard.Controls.Add(imageLayout);
            rootSplit.Panel1.Padding = new Padding(0, 0, 4, 0);
            rootSplit.Panel1.Controls.Add(imageCard);

            var workLayout = new TableLayoutPanel
            {
                Name = "WorkspaceColumns",
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackground,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            workLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            workLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350f));
            workLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var canvasCard = new CardPanel
            {
                Name = "WorkspaceCanvasCard",
                Dock = DockStyle.Fill,
                CornerRadius = 17,
                Padding = new Padding(2),
                BorderColor = UiTheme.Border,
                Margin = new Padding(0, 0, 5, 0)
            };
            _canvas.Dock = DockStyle.Fill;
            canvasCard.Controls.Add(_canvas);
            workLayout.Controls.Add(canvasCard, 0, 0);

            var editorCard = new CardPanel
            {
                Name = "WorkspaceEditorCard",
                Dock = DockStyle.Fill,
                CornerRadius = 17,
                Padding = new Padding(1),
                BorderColor = UiTheme.Border,
                Margin = new Padding(5, 0, 0, 0)
            };
            _editor.Dock = DockStyle.Fill;
            editorCard.Controls.Add(_editor);
            workLayout.Controls.Add(editorCard, 1, 0);
            rootSplit.Panel2.Padding = new Padding(4, 0, 0, 0);
            rootSplit.Panel2.Controls.Add(workLayout);
            page.Controls.Add(rootSplit, 0, 2);
            _workspacePage.Controls.Add(page);
        }

        private void ShowHomePage()
        {
            _homePage.BringToFront();
            _homePage.Visible = true;
            _workspacePage.Visible = false;
            _batchPage.Visible = false;
            SetActiveNavigation(_homeNavButton);
            UpdateDashboard();
        }

        private void ShowWorkspacePage(NavButton activeButton = null)
        {
            _workspacePage.BringToFront();
            _workspacePage.Visible = true;
            _homePage.Visible = false;
            _batchPage.Visible = false;
            SetActiveNavigation(activeButton ?? _workspaceNavButton);
        }

        private void ShowBatchPage()
        {
            _batchPage.BringToFront();
            _batchPage.Visible = true;
            _homePage.Visible = false;
            _workspacePage.Visible = false;
            SetActiveNavigation(_batchNavButton);
        }

        public void ShowWorkspaceForDiagnostics()
        {
            ShowWorkspacePage(_workspaceNavButton);
        }

        private void SetActiveNavigation(NavButton button)
        {
            var buttons = new[] { _homeNavButton, _workspaceNavButton, _batchNavButton, _apiNavButton, _helpNavButton };
            foreach (var current in buttons) current.Active = ReferenceEquals(current, button);
            _activeNavigation = button;
            button?.Parent?.Invalidate(true);
            button?.Parent?.Update();
        }

        private void UpdateDashboard()
        {
            var images = _project?.Images.Count ?? 0;
            var regions = _project?.Images.Sum(image => image.Regions.Count) ?? 0;
            var translated = _project?.Images.Sum(image => image.Regions.Count(region => !string.IsNullOrWhiteSpace(region.Translation))) ?? 0;
            var reviewed = _project?.Images.Sum(image => image.Regions.Count(region => region.Reviewed)) ?? 0;
            _dashboardImageCount.Text = images.ToString();
            _dashboardRegionCount.Text = regions.ToString();
            _dashboardTranslatedCount.Text = translated.ToString();
            _dashboardReviewedCount.Text = reviewed.ToString();

            if (_project == null)
            {
                _dashboardProjectName.Text = "尚未打开工程";
                _dashboardProjectPath.Text = "导入图片目录或打开 .guih.json 工程后开始";
                _dashboardProgress.Text = "等待项目";
                _sidebarProjectLabel.Text = "当前工程\r\n尚未打开项目";
                return;
            }

            var projectName = string.IsNullOrWhiteSpace(_projectPath)
                ? new DirectoryInfo(_project.SourceFolder).Name
                : Path.GetFileNameWithoutExtension(_projectPath);
            var percent = regions == 0 ? 0 : (int)Math.Round(translated * 100d / regions);
            _dashboardProjectName.Text = projectName;
            _dashboardProjectPath.Text = _project.SourceFolder;
            _dashboardProgress.Text = regions == 0 ? "尚未建立文字区域" : $"翻译进度 {percent}%  ·  {translated}/{regions} 条";
            _sidebarProjectLabel.Text = "当前工程\r\n" + projectName + $"\r\n{translated}/{regions} 条已翻译";
        }

        private void BuildLegacyInterface()
        {
            _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            _toolStrip.Padding = new Padding(6, 4, 6, 4);
            _toolStrip.Items.AddRange(new ToolStripItem[]
            {
                _openFolderButton, _openProjectButton, _saveButton,
                new ToolStripSeparator(),
                _visionButton, _visionBatchButton, _translateButton, _translateAllButton, _translationResourcesButton,
                new ToolStripSeparator(),
                _drawButton, _deleteButton, _previewButton, _compareButton, _fitButton,
                new ToolStripSeparator(),
                _exportButton, _settingsButton, _cancelOperationButton,
                new ToolStripSeparator(), _helpButton
            });
            _toolStrip.Dock = DockStyle.Top;

            var statusStrip = new StatusStrip();
            _statusLabel.Spring = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _zoomLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
            statusStrip.Items.Add(_statusLabel);
            statusStrip.Items.Add(_zoomLabel);

            var rootSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Size = new Size(1200, 700),
                SplitterDistance = 270,
                Panel1MinSize = 210,
                Panel2MinSize = 700
            };

            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 3
            };
            leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            leftLayout.Controls.Add(new Label
            {
                Text = "图片列表",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(0, 0, 0, 6)
            }, 0, 0);
            _searchBox.PlaceholderText = "搜索文件名";
            _searchBox.Dock = DockStyle.Top;
            _searchBox.Margin = new Padding(0, 0, 0, 7);
            leftLayout.Controls.Add(_searchBox, 0, 1);
            _imageList.Dock = DockStyle.Fill;
            _imageList.HorizontalScrollbar = true;
            leftLayout.Controls.Add(_imageList, 0, 2);
            rootSplit.Panel1.Controls.Add(leftLayout);

            var workSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                Size = new Size(1200, 700),
                SplitterDistance = 780,
                Panel1MinSize = 450,
                Panel2MinSize = 330
            };
            _canvas.Dock = DockStyle.Fill;
            _editor.Dock = DockStyle.Fill;
            workSplit.Panel1.Controls.Add(_canvas);
            workSplit.Panel2.Controls.Add(_editor);
            rootSplit.Panel2.Controls.Add(workSplit);

            Controls.Add(rootSplit);
            Controls.Add(statusStrip);
            Controls.Add(_toolStrip);
        }

        private void WireEvents()
        {
            _homeNavButton.Click += (_, __) => ShowHomePage();
            _workspaceNavButton.Click += (_, __) => ShowWorkspacePage(_workspaceNavButton);
            _batchNavButton.Click += (_, __) =>
            {
                ShowBatchPage();
                _statusLabel.Text = "批量任务中心：可暂停、取消、重试失败项或继续上次未完成任务";
            };
            _apiNavButton.Click += (_, __) =>
            {
                var previous = _activeNavigation;
                SetActiveNavigation(_apiNavButton);
                ShowApiSettings();
                SetActiveNavigation(previous ?? _homeNavButton);
            };
            _helpNavButton.Click += (_, __) =>
            {
                var previous = _activeNavigation;
                SetActiveNavigation(_helpNavButton);
                ShowHelp();
                SetActiveNavigation(previous ?? _homeNavButton);
            };
            _openFolderButton.Click += (_, __) => OpenImageFolder();
            _openProjectButton.Click += (_, __) => OpenProject();
            _saveButton.Click += (_, __) => SaveProject(false);
            _undoButton.Click += (_, __) => UndoProjectChange();
            _redoButton.Click += (_, __) => RedoProjectChange();
            _settingsButton.Click += (_, __) => ShowApiSettings();
            _visionButton.Click += async (_, __) => await AnalyzeCurrentAsync();
            _visionBatchButton.Click += async (_, __) => await AnalyzeBatchAsync();
            _translateButton.Click += async (_, __) => await TranslateCurrentAsync();
            _translateAllButton.Click += async (_, __) => await TranslateAllPendingAsync();
            _translationResourcesButton.Click += (_, __) => ShowTranslationResources();
            _drawButton.CheckedChanged += (_, __) =>
            {
                if (_updatingEditModes) return;
                if (_drawButton.Checked)
                {
                    _compareButton.Checked = false;
                    _updatingEditModes = true;
                    _maskBrushButton.Checked = false;
                    _maskEraserButton.Checked = false;
                    _updatingEditModes = false;
                    _canvas.SetMaskEditMode(false, false);
                }
                _canvas.CreateMode = _drawButton.Checked;
                _statusLabel.Text = _drawButton.Checked
                    ? "在图片上按住鼠标左键拖动，创建文字区域"
                    : "已退出框选模式";
            };
            _maskBrushButton.CheckedChanged += (_, __) => HandleMaskToolChanged(_maskBrushButton, false);
            _maskEraserButton.CheckedChanged += (_, __) => HandleMaskToolChanged(_maskEraserButton, true);
            _maskClearButton.Click += (_, __) =>
            {
                _canvas.ClearSelectedMask();
                _statusLabel.Text = "已清除当前文字区域的自定义蒙版，将恢复为矩形修复范围";
                UpdateCommandState();
            };
            _deleteButton.Click += (_, __) => DeleteSelectedRegion();
            _previewButton.CheckedChanged += (_, __) => _canvas.SetPreviewEnabled(_previewButton.Checked);
            _compareButton.CheckedChanged += (_, __) =>
            {
                if (_compareButton.Checked)
                {
                    DeactivateMaskTools();
                    _drawButton.Checked = false;
                    _canvas.CreateMode = false;
                }
                _canvas.SetComparisonEnabled(_compareButton.Checked);
                _statusLabel.Text = _compareButton.Checked
                    ? "原图对比已开启：拖动图片中的蓝色分界线查看前后效果"
                    : "已退出原图对比";
            };
            _atlasButton.CheckedChanged += (_, __) => _canvas.SetAtlasOverlay(_atlasButton.Checked);
            _fitButton.Click += (_, __) => _canvas.ZoomToFit();
            _preflightButton.Click += (_, __) => RunPreflight(false);
            _exportButton.Click += async (_, __) => await ExportProjectAsync();
            _cancelOperationButton.Click += (_, __) =>
            {
                _operationCancellation?.Cancel();
                _batchTaskCenter.Cancel();
            };
            _helpButton.Click += (_, __) => ShowHelp();
            _batchPage.RetryRequested += async (_, __) => await RetryBatchFailuresAsync();
            _batchPage.ResumeRequested += async (_, __) => await ResumeBatchQueueAsync();
            _batchTaskCenter.Changed += (_, __) => HandleBatchTaskCenterChanged();

            _searchBox.TextChanged += (_, __) => PopulateImageList(_searchBox.Text);
            _imageStatusFilter.SelectedIndexChanged += (_, __) =>
            {
                if (!_loadingImageList) PopulateImageList(_searchBox.Text);
            };
            _imageList.DrawItem += DrawImageListItem;
            _imageList.SelectedIndexChanged += (_, __) =>
            {
                if (!_loadingImageList && _imageList.SelectedItem is ImageListEntry entry)
                {
                    LoadDocument(entry.Document);
                }
            };
            _thumbnailCache.ThumbnailAvailable += (_, __) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new Action(() => _imageList.Invalidate())); }
                catch (InvalidOperationException) { }
            };

            _canvas.SelectionChanged += (_, __) =>
            {
                if (_canvas.SelectedRegion == null && (_maskBrushButton.Checked || _maskEraserButton.Checked))
                    DeactivateMaskTools();
                _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                UpdateCommandState();
            };
            _canvas.DocumentChanged += (_, __) =>
            {
                _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                MarkDirty();
                RefreshCurrentImageEntry();
            };
            _canvas.ZoomChanged += (_, __) => _zoomLabel.Text = _canvas.ZoomText;
            _editor.RegionSelected += (_, args) => _canvas.SelectRegion(args.Region);
            _editor.RegionEdited += (_, __) =>
            {
                _canvas.NotifyRegionChanged();
                RefreshCurrentImageEntry();
            };
            _editor.LoadFontRequested += (_, __) => LoadCustomFonts();

            FormClosing += OnFormClosing;
            FormClosed += (_, __) =>
            {
                PersistReviewedTranslations();
                PersistBatchQueue();
                _thumbnailCache.Dispose();
                _imageItemTitleFont.Dispose();
                _imageItemDetailFont.Dispose();
                _imageItemStatusFont.Dispose();
                _historyTimer.Dispose();
                _autosaveTimer.Dispose();
            };
            KeyDown += OnMainKeyDown;
        }

        private void HandleMaskToolChanged(ToolStripButton source, bool eraser)
        {
            if (_updatingEditModes) return;
            _updatingEditModes = true;
            try
            {
                if (source.Checked && _canvas.SelectedRegion == null)
                {
                    source.Checked = false;
                    _statusLabel.Text = "请先选中一个文字区域，再使用蒙版工具";
                    return;
                }

                if (source.Checked)
                {
                    _compareButton.Checked = false;
                    _drawButton.Checked = false;
                    _canvas.CreateMode = false;
                    if (eraser) _maskBrushButton.Checked = false;
                    else _maskEraserButton.Checked = false;
                }

                var enabled = _maskBrushButton.Checked || _maskEraserButton.Checked;
                _canvas.SetMaskEditMode(enabled, _maskEraserButton.Checked);
                _statusLabel.Text = enabled
                    ? eraser
                        ? "蒙版橡皮擦：在图片上拖动以缩小修复范围"
                        : "蒙版画笔：在图片上涂抹需要清除和修复的原文字"
                    : "已退出蒙版编辑";
            }
            finally
            {
                _updatingEditModes = false;
            }
        }

        private void DeactivateMaskTools()
        {
            _updatingEditModes = true;
            _maskBrushButton.Checked = false;
            _maskEraserButton.Checked = false;
            _updatingEditModes = false;
            _canvas.SetMaskEditMode(false, false);
        }

        private void OpenImageFolder()
        {
            if (!ConfirmDiscardChanges()) return;
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择解包后的 UI 图片根目录",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    var project = ProjectService.CreateFromFolder(dialog.SelectedPath);
                    if (project.Images.Count == 0)
                    {
                        MessageBox.Show(this,
                            "没有找到可读取的 PNG、JPG、JPEG、BMP 或受支持的 DDS 图片。",
                            "未找到图片", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    _project = project;
                    _projectPath = null;
                    _currentDocument = null;
                    _dirty = true;
                    ResetProjectTracking();
                    PopulateImageList();
                    _imageList.SelectedIndex = 0;
                    _statusLabel.Text = $"已载入 {_project.Images.Count} 张图片";
                    ShowFailuresIfAny("部分资源未导入", project.ImportWarnings);
                    ShowWorkspacePage(_workspaceNavButton);
                    UpdateDashboard();
                    UpdateTitle();
                    UpdateCommandState();
                }
                catch (Exception exception)
                {
                    ShowError("无法读取图片目录", exception);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private void OpenProject()
        {
            if (!ConfirmDiscardChanges()) return;
            using (var dialog = new OpenFileDialog
            {
                Title = "打开汉化工程",
                Filter = "Galgame UI 汉化工程 (*.guih.json)|*.guih.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var project = ProjectService.LoadProject(dialog.FileName);
                    var restoredFromAutosave = false;
                    var autosavePath = ProjectService.GetAutosavePath(project, dialog.FileName);
                    if (File.Exists(autosavePath) &&
                        File.GetLastWriteTimeUtc(autosavePath) > File.GetLastWriteTimeUtc(dialog.FileName))
                    {
                        var answer = MessageBox.Show(this,
                            "发现一个比工程文件更新的自动恢复版本。是否恢复该版本？",
                            "发现自动保存", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (answer == DialogResult.Yes)
                        {
                            project = ProjectService.LoadAutosave(autosavePath).Project;
                            restoredFromAutosave = true;
                        }
                        else
                        {
                            ProjectService.DeleteAutosave(autosavePath);
                        }
                    }
                    if (!Directory.Exists(project.SourceFolder))
                    {
                        using (var locate = new FolderBrowserDialog
                        {
                            Description = "工程中的源图片目录不存在，请重新定位",
                            ShowNewFolderButton = false
                        })
                        {
                            if (locate.ShowDialog(this) != DialogResult.OK) return;
                            project.SourceFolder = locate.SelectedPath;
                        }
                    }

                    AtlasService.RefreshProject(project);

                    _project = project;
                    _projectPath = dialog.FileName;
                    _currentDocument = null;
                    _dirty = restoredFromAutosave;
                    ResetProjectTracking();
                    LoadProjectFonts();
                    PopulateImageList();
                    if (_imageList.Items.Count > 0) _imageList.SelectedIndex = 0;
                    _statusLabel.Text = restoredFromAutosave
                        ? $"已恢复自动保存：{Path.GetFileName(_projectPath)}"
                        : $"已打开工程：{Path.GetFileName(_projectPath)}";
                    ShowWorkspacePage(_workspaceNavButton);
                    UpdateDashboard();
                    UpdateTitle();
                    UpdateCommandState();
                }
                catch (Exception exception)
                {
                    ShowError("无法打开工程", exception);
                }
            }
        }

        private bool SaveProject(bool saveAs)
        {
            if (_project == null) return false;
            var path = _projectPath;
            if (saveAs || string.IsNullOrWhiteSpace(path))
            {
                using (var dialog = new SaveFileDialog
                {
                    Title = "保存汉化工程",
                    Filter = "Galgame UI 汉化工程 (*.guih.json)|*.guih.json|JSON 文件 (*.json)|*.json",
                    FileName = "UI汉化工程.guih.json",
                    AddExtension = true,
                    DefaultExt = "guih.json"
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    path = dialog.FileName;
                }
            }

            try
            {
                var previousAutosavePath = _currentAutosavePath;
                ProjectService.SaveProject(path, _project);
                _projectPath = path;
                _dirty = false;
                ProjectService.DeleteAutosave(previousAutosavePath);
                _currentAutosavePath = ProjectService.GetAutosavePath(_project, _projectPath);
                ProjectService.DeleteAutosave(_currentAutosavePath);
                PersistBatchQueue();
                _statusLabel.Text = "工程已保存";
                UpdateDashboard();
                UpdateTitle();
                return true;
            }
            catch (Exception exception)
            {
                ShowError("保存工程失败", exception);
                return false;
            }
        }

        private void LoadDocument(ImageDocument document)
        {
            if (_project == null || document == null) return;
            var path = ProjectService.GetSourcePath(_project, document);
            try
            {
                DeactivateMaskTools();
                var bitmap = ImageProcessor.LoadBitmapUnlocked(path);
                _currentDocument = document;
                _canvas.DefaultFontFamily = _settings.DefaultFontFamily;
                _canvas.SetDocument(bitmap, document);
                _atlasButton.Checked = document.AtlasSprites.Count > 0;
                _canvas.SetAtlasOverlay(_atlasButton.Checked);
                _editor.SetDocument(document, null);
                _statusLabel.Text = $"{document.RelativePath}  |  {document.Width} × {document.Height}  |  " +
                                    $"{document.Regions.Count} 个文字区域" +
                                    (document.AtlasSprites.Count > 0 ? $"  |  图集 {document.AtlasSprites.Count} 个精灵" : string.Empty);
                UpdateCommandState();
            }
            catch (Exception exception)
            {
                _currentDocument = null;
                _canvas.ClearDocument();
                ShowError("无法打开图片：" + document.RelativePath, exception);
            }
        }

        private void ShowApiSettings()
        {
            using (var dialog = new ApiSettingsDialog(_settings, _visionApiKey, _translationApiKey))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _visionApiKey = dialog.VisionApiKey;
                _translationApiKey = dialog.TranslationApiKey;
                ProjectService.SaveSettings(_settings);
                _canvas.DefaultFontFamily = _settings.DefaultFontFamily;
                _editor.RefreshFontNames(_settings.DefaultFontFamily);
                _statusLabel.Text = "API 与翻译设置已更新";
            }
        }

        private void ShowTranslationResources()
        {
            using (var dialog = new TranslationResourcesDialog(_translationResources.CloneData(), _project))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _translationResources.ReplaceData(dialog.Resources);
                    _translationResources.Save();
                    _statusLabel.Text = $"术语库已保存：翻译记忆 {_translationResources.Data.Memory.Count} 条，术语 {_translationResources.Data.Glossary.Count} 条";
                }
                catch (Exception exception)
                {
                    ShowError("无法保存翻译记忆与术语表", exception);
                }
            }
        }

        private async Task AnalyzeCurrentAsync()
        {
            if (_project == null || _currentDocument == null || !EnsureVisionConfigured()) return;
            var mergeMode = DialogResult.No;
            if (_currentDocument.Regions.Count > 0)
            {
                mergeMode = MessageBox.Show(this,
                    "当前图片已有文字区域。\r\n\r\n选择“是”替换现有区域；选择“否”追加识别结果；选择“取消”停止。",
                    "处理已有区域", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (mergeMode == DialogResult.Cancel) return;
            }

            BeginOperation("正在识别当前图片…");
            try
            {
                var result = await _apiClient.AnalyzeAsync(
                    ProjectService.GetSourcePath(_project, _currentDocument),
                    _currentDocument.Width,
                    _currentDocument.Height,
                    _settings,
                    _visionApiKey,
                    _translationResources.Glossary.Take(200).ToArray(),
                    _operationCancellation.Token);
                ApplyDefaultFont(result.Regions);
                var memoryMatches = _translationResources.ApplyExactMatches(result.Regions);
                if (mergeMode == DialogResult.Yes) _currentDocument.Regions.Clear();
                _currentDocument.Regions.AddRange(result.Regions);
                _canvas.SelectRegion(result.Regions.FirstOrDefault());
                _canvas.NotifyRegionChanged();
                _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                _statusLabel.Text = result.Regions.Count == 0
                    ? "识别完成，但模型没有返回文字区域"
                    : $"识别完成：新增 {result.Regions.Count} 个文字区域，翻译记忆命中 {memoryMatches} 条，请检查低置信度区域";
                PersistReviewedTranslations();
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = "任务已取消";
            }
            catch (Exception exception)
            {
                ShowError("AI 识图失败", exception);
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task AnalyzeBatchAsync()
        {
            if (_project == null || !EnsureVisionConfigured()) return;
            var targets = _project.Images.Where(image => image.Regions.Count == 0).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "所有图片都已有文字区域。批量识图只处理尚未建立区域的图片。",
                    "没有待处理图片", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"将调用视觉 API 处理 {targets.Count} 张尚未识别的图片，可能产生 API 费用。是否继续？",
                    "确认批量识图", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var items = targets.Select(image => new BatchTaskItem
            {
                Kind = BatchTaskKind.Recognition,
                Target = image.RelativePath,
                ImageRelativePath = image.RelativePath,
                State = image
            }).ToList();
            BeginOperation("开始批量识图…");
            ShowBatchPage();
            try
            {
                await _batchTaskCenter.RunAsync(
                    items,
                    ExecuteRecognitionBatchItemAsync,
                    _operationCancellation.Token);

                PopulateImageList(_searchBox.Text, _currentDocument);
                if (_currentDocument != null)
                {
                    _canvas.NotifyRegionChanged();
                    _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                }
                var completed = items.Count(item => item.Status == BatchTaskStatus.Completed);
                var failed = items.Count(item => item.Status == BatchTaskStatus.Failed);
                var cancelled = items.Count(item => item.Status == BatchTaskStatus.Cancelled);
                var totalRegions = items.Sum(item => item.ResultCount);
                var memoryMatches = items.Sum(item => item.MemoryMatchCount);
                _statusLabel.Text = cancelled > 0
                    ? $"批量识图已取消：完成 {completed}/{items.Count}，发现 {totalRegions} 个区域，记忆命中 {memoryMatches} 条"
                    : $"批量识图完成：成功 {completed}/{items.Count}，失败 {failed}，发现 {totalRegions} 个区域，记忆命中 {memoryMatches} 条";
                PersistReviewedTranslations();
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task TranslateCurrentAsync()
        {
            if (_currentDocument == null) return;
            var available = _currentDocument.Regions.Where(region => !string.IsNullOrWhiteSpace(region.SourceText)).ToList();
            if (available.Count == 0)
            {
                MessageBox.Show(this, "当前图片没有已录入的日文原文。请先识图，或手工框选并填写日文。",
                    "没有可翻译文本", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var targets = available;
            if (available.Any(region => !string.IsNullOrWhiteSpace(region.Translation)))
            {
                var answer = MessageBox.Show(this,
                    "当前图片已有译文。\r\n\r\n选择“是”重新翻译全部；选择“否”只翻译空白项；选择“取消”停止。",
                    "处理已有译文", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.No)
                    targets = available.Where(region => string.IsNullOrWhiteSpace(region.Translation)).ToList();
            }

            if (targets.Count == 0)
            {
                _statusLabel.Text = "当前图片没有待翻译项";
                return;
            }

            var memoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var memoryMatches = _translationResources.ApplyExactMatches(targets, memoryIds);
            var apiTargets = targets.Where(region => !memoryIds.Contains(region.Id)).ToList();
            if (memoryMatches > 0)
            {
                _canvas.NotifyRegionChanged();
                _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                PersistReviewedTranslations();
            }

            if (apiTargets.Count == 0)
            {
                _statusLabel.Text = $"已从翻译记忆回填 {memoryMatches}/{targets.Count} 条，无需调用 API";
                return;
            }

            if (!EnsureTranslationConfigured())
            {
                if (memoryMatches > 0)
                    _statusLabel.Text = $"已从翻译记忆回填 {memoryMatches} 条；其余 {apiTargets.Count} 条需要配置翻译 API";
                return;
            }

            BeginOperation(memoryMatches > 0
                ? $"翻译记忆已回填 {memoryMatches} 条，正在调用 API 处理其余 {apiTargets.Count} 条…"
                : "正在调用文本翻译 API…");
            try
            {
                var translations = await _apiClient.TranslateAsync(
                    apiTargets,
                    _settings,
                    _translationApiKey,
                    _translationResources.GetRelevantGlossary(apiTargets),
                    _operationCancellation.Token);
                var apiUpdated = ApplyTranslations(apiTargets, translations);
                _canvas.NotifyRegionChanged();
                _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                _statusLabel.Text = $"翻译完成：记忆复用 {memoryMatches} 条，API 回填 {apiUpdated}/{apiTargets.Count} 条";
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = "翻译已取消";
            }
            catch (Exception exception)
            {
                ShowError("文本翻译失败", exception);
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task TranslateAllPendingAsync()
        {
            if (_project == null) return;
            var targets = _project.Images.SelectMany(image => image.Regions)
                .Where(region => !string.IsNullOrWhiteSpace(region.SourceText) && string.IsNullOrWhiteSpace(region.Translation))
                .ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "工程中没有原文非空且译文为空的条目。",
                    "没有待翻译文本", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var memoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var memoryMatches = _translationResources.ApplyExactMatches(targets, memoryIds);
            var apiTargets = targets.Where(region => !memoryIds.Contains(region.Id)).ToList();
            if (memoryMatches > 0)
            {
                MarkDirty();
                if (_currentDocument != null)
                {
                    _canvas.NotifyRegionChanged();
                    _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                }
                PopulateImageList(_searchBox.Text, _currentDocument);
                PersistReviewedTranslations();
            }

            if (apiTargets.Count == 0)
            {
                _statusLabel.Text = $"批量翻译完成：从翻译记忆回填 {memoryMatches}/{targets.Count} 条，无需调用 API";
                return;
            }

            if (!EnsureTranslationConfigured())
            {
                if (memoryMatches > 0)
                    _statusLabel.Text = $"已从翻译记忆回填 {memoryMatches} 条；其余 {apiTargets.Count} 条需要配置翻译 API";
                return;
            }

            if (MessageBox.Show(this,
                    $"翻译记忆已复用 {memoryMatches} 条。将使用文本翻译 API 处理其余 {apiTargets.Count} 条，按每批 30 条发送。是否继续？",
                    "确认批量翻译", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var batches = Chunk(apiTargets, 30).ToList();
            var items = batches.Select((batch, index) => new BatchTaskItem
            {
                Kind = BatchTaskKind.Translation,
                Target = $"第 {index + 1} 批（{batch.Count} 条）",
                RegionIds = batch.Select(region => region.Id).ToList(),
                State = batch
            }).ToList();
            BeginOperation("开始批量翻译…");
            ShowBatchPage();
            try
            {
                await _batchTaskCenter.RunAsync(
                    items,
                    ExecuteTranslationBatchItemAsync,
                    _operationCancellation.Token);

                if (_currentDocument != null)
                {
                    _canvas.NotifyRegionChanged();
                    _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                }
                PopulateImageList(_searchBox.Text, _currentDocument);
                var failed = items.Count(item => item.Status == BatchTaskStatus.Failed);
                var cancelled = items.Any(item => item.Status == BatchTaskStatus.Cancelled);
                var updated = memoryMatches + items.Sum(item => item.ResultCount);
                _statusLabel.Text = cancelled
                    ? $"批量翻译已取消，已回填 {updated}/{targets.Count} 条（记忆 {memoryMatches} 条）"
                    : $"批量翻译完成：已回填 {updated}/{targets.Count} 条（记忆 {memoryMatches} 条），失败批次 {failed}";
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task ExportProjectAsync()
        {
            if (_project == null) return;
            if (!RunPreflight(true)) return;
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择汉化图片输出目录（不会覆盖源图）",
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var outputRoot = Path.GetFullPath(dialog.SelectedPath).TrimEnd(Path.DirectorySeparatorChar);
                var sourceRoot = Path.GetFullPath(_project.SourceFolder).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(outputRoot, sourceRoot, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "输出目录不能与源图片目录相同，以免覆盖原始资源。",
                        "目录不安全", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _project.OutputFolder = outputRoot;
                var translatedRegions = _project.Images.Sum(image => image.Regions.Count(region => !string.IsNullOrWhiteSpace(region.Translation)));
                var items = _project.Images.Select(image => new BatchTaskItem
                {
                    Kind = BatchTaskKind.Export,
                    Target = image.RelativePath,
                    ImageRelativePath = image.RelativePath,
                    OutputRoot = outputRoot,
                    State = new ExportBatchPayload { Image = image }
                }).ToList();
                foreach (var metadataPath in _project.Images
                             .Select(image => image.AtlasMetadataPath)
                             .Where(path => !string.IsNullOrWhiteSpace(path))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(new BatchTaskItem
                    {
                        Kind = BatchTaskKind.Export,
                        Target = "图集元数据：" + metadataPath,
                        OutputRoot = outputRoot,
                        MetadataRelativePath = metadataPath,
                        State = new ExportBatchPayload { MetadataRelativePath = metadataPath }
                    });
                }
                MarkDirty();
                BeginOperation("开始批量导出…");
                ShowBatchPage();
                try
                {
                    await _batchTaskCenter.RunAsync(
                        items,
                        ExecuteExportBatchItemAsync,
                        _operationCancellation.Token);

                    var failures = items
                        .Where(item => item.Status == BatchTaskStatus.Failed)
                        .Select(item => item.Target + "：" + item.Message)
                        .ToList();
                    WriteExportReport(outputRoot, failures, translatedRegions);
                    var completed = items.Count(item => item.Status == BatchTaskStatus.Completed);
                    var cancelled = items.Any(item => item.Status == BatchTaskStatus.Cancelled);
                    _statusLabel.Text = cancelled
                        ? $"导出已取消：已完成 {completed}/{items.Count}，已写出的文件保留"
                        : $"导出完成：成功 {completed}/{items.Count}，失败 {failures.Count}，{translatedRegions} 个汉化区域";
                }
                finally
                {
                    EndOperation();
                }
            }
        }

        private async Task RetryBatchFailuresAsync()
        {
            if (_busy || !_batchTaskCenter.Items.Any(item => item.Status == BatchTaskStatus.Failed)) return;
            var failedItems = _batchTaskCenter.Items
                .Where(item => item.Status == BatchTaskStatus.Failed)
                .ToList();
            if (!EnsureBatchApisConfigured(failedItems)) return;
            BeginOperation("正在重试失败的批量任务…");
            ShowBatchPage();
            try
            {
                await _batchTaskCenter.RetryFailedAsync(_operationCancellation.Token);
                PopulateImageList(_searchBox.Text, _currentDocument);
                if (_currentDocument != null)
                {
                    _canvas.NotifyRegionChanged();
                    _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
                }
                var reportWarning = RefreshRecoveredExportReports();
                var remaining = _batchTaskCenter.Items.Count(item => item.Status == BatchTaskStatus.Failed);
                _statusLabel.Text = remaining == 0
                    ? "失败任务重试完成，当前没有失败项"
                    : $"重试完成，仍有 {remaining} 个失败项";
                if (!string.IsNullOrWhiteSpace(reportWarning))
                    _statusLabel.Text += "；" + reportWarning;
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task ResumeBatchQueueAsync()
        {
            if (_busy || _project == null) return;
            var pending = _batchTaskCenter.Items
                .Where(item => item.Status == BatchTaskStatus.Pending)
                .ToList();
            if (pending.Count == 0) return;
            if (!EnsureBatchApisConfigured(pending)) return;

            BeginOperation("正在从保存的断点继续批量任务…");
            ShowBatchPage();
            try
            {
                await _batchTaskCenter.ResumePendingAsync(_operationCancellation.Token);
                RefreshWorkspaceAfterBatch();
                var reportWarning = RefreshRecoveredExportReports();
                var remaining = _batchTaskCenter.Items.Count(item => item.Status == BatchTaskStatus.Pending);
                var failed = _batchTaskCenter.Items.Count(item => item.Status == BatchTaskStatus.Failed);
                _statusLabel.Text = remaining > 0
                    ? $"断点续跑已暂停：仍有 {remaining} 项等待处理"
                    : $"断点续跑完成：失败 {failed} 项";
                if (!string.IsNullOrWhiteSpace(reportWarning))
                    _statusLabel.Text += "；" + reportWarning;
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task ExecuteRecognitionBatchItemAsync(
            BatchTaskItem item,
            CancellationToken cancellationToken)
        {
            if (_project == null) throw new InvalidOperationException("当前没有打开工程。");
            var relativePath = string.IsNullOrWhiteSpace(item.ImageRelativePath)
                ? item.Target
                : item.ImageRelativePath;
            var image = _project.Images.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            if (image == null) throw new InvalidDataException("工程中找不到待识别图片：" + relativePath);
            item.State = image;
            item.ImageRelativePath = image.RelativePath;
            item.ResultCount = 0;
            item.MemoryMatchCount = 0;
            if (image.Regions.Count > 0)
            {
                item.Message = $"已有 {image.Regions.Count} 个区域，已跳过重复识别";
                return;
            }

            var result = await _apiClient.AnalyzeAsync(
                ProjectService.GetSourcePath(_project, image),
                image.Width,
                image.Height,
                _settings,
                _visionApiKey,
                _translationResources.Glossary.Take(200).ToArray(),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ApplyDefaultFont(result.Regions);
            item.MemoryMatchCount = _translationResources.ApplyExactMatches(result.Regions);
            image.Regions.AddRange(result.Regions);
            item.ResultCount = result.Regions.Count;
            item.Message = result.Regions.Count == 0
                ? "未发现文字区域"
                : $"新增 {result.Regions.Count} 个区域";
            if (result.Regions.Count > 0) MarkDirty();
        }

        private async Task ExecuteTranslationBatchItemAsync(
            BatchTaskItem item,
            CancellationToken cancellationToken)
        {
            if (_project == null) throw new InvalidOperationException("当前没有打开工程。");
            var regions = FindBatchRegions(item.RegionIds);
            if (regions.Count == 0)
                throw new InvalidDataException("工程中找不到该翻译批次对应的文字区域。");
            item.State = regions;
            item.ResultCount = 0;
            item.MemoryMatchCount = 0;
            var pending = regions.Where(region =>
                    !string.IsNullOrWhiteSpace(region.SourceText) &&
                    string.IsNullOrWhiteSpace(region.Translation))
                .ToList();
            if (pending.Count == 0)
            {
                item.Message = $"{regions.Count} 条译文均已存在，已跳过";
                return;
            }

            var memoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            item.MemoryMatchCount = _translationResources.ApplyExactMatches(pending, memoryIds);
            var apiTargets = pending.Where(region => !memoryIds.Contains(region.Id)).ToList();
            if (item.MemoryMatchCount > 0) MarkDirty();
            var translated = 0;
            if (apiTargets.Count > 0)
            {
                var translations = await _apiClient.TranslateAsync(
                    apiTargets,
                    _settings,
                    _translationApiKey,
                    _translationResources.GetRelevantGlossary(apiTargets),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                translated = ApplyTranslations(apiTargets, translations);
                if (translated > 0) MarkDirty();
            }

            item.ResultCount = item.MemoryMatchCount + translated;
            item.Message = $"回填 {item.ResultCount}/{pending.Count} 条" +
                           (item.MemoryMatchCount > 0 ? $"（记忆 {item.MemoryMatchCount}）" : string.Empty);
        }

        private async Task ExecuteExportBatchItemAsync(
            BatchTaskItem item,
            CancellationToken cancellationToken)
        {
            if (_project == null) throw new InvalidOperationException("当前没有打开工程。");
            if (string.IsNullOrWhiteSpace(item.OutputRoot))
                throw new InvalidDataException("导出任务缺少输出目录，请重新创建导出任务。");
            var outputRoot = Path.GetFullPath(item.OutputRoot);
            var image = string.IsNullOrWhiteSpace(item.ImageRelativePath)
                ? null
                : _project.Images.FirstOrDefault(candidate => string.Equals(
                    candidate.RelativePath,
                    item.ImageRelativePath,
                    StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(item.ImageRelativePath) && image == null)
                throw new InvalidDataException("工程中找不到待导出图片：" + item.ImageRelativePath);
            item.State = new ExportBatchPayload
            {
                Image = image,
                MetadataRelativePath = item.MetadataRelativePath
            };

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (image != null)
                {
                    var source = ProjectService.GetSourcePath(_project, image);
                    var output = ProjectService.GetSafeOutputPath(outputRoot, image.RelativePath);
                    ImageProcessor.ExportDocument(source, output, image);
                    var validation = PreflightService.ValidateExportedFile(source, output, image);
                    if (!string.IsNullOrWhiteSpace(validation))
                        throw new InvalidDataException(validation);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(item.MetadataRelativePath))
                        throw new InvalidDataException("导出任务缺少图片或图集元数据目标。");
                    var source = ProjectService.GetSafeOutputPath(
                        _project.SourceFolder, item.MetadataRelativePath);
                    if (!File.Exists(source)) throw new FileNotFoundException("图集元数据文件不存在。", source);
                    var output = ProjectService.GetSafeOutputPath(outputRoot, item.MetadataRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? outputRoot);
                    File.Copy(source, output, true);
                }
            }, cancellationToken);
            item.ResultCount = 1;
            item.Message = "导出并验证成功";
        }

        private List<TextRegion> FindBatchRegions(IEnumerable<string> regionIds)
        {
            if (_project == null || regionIds == null) return new List<TextRegion>();
            var lookup = _project.Images.SelectMany(image => image.Regions)
                .GroupBy(region => region.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            return regionIds.Where(id => !string.IsNullOrWhiteSpace(id) && lookup.ContainsKey(id))
                .Select(id => lookup[id])
                .ToList();
        }

        private bool EnsureBatchApisConfigured(IEnumerable<BatchTaskItem> items)
        {
            var list = items.ToList();
            var needsVision = list.Any(item =>
                item.Kind == BatchTaskKind.Recognition && RecognitionTaskNeedsApi(item));
            if (needsVision && !EnsureVisionConfigured()) return false;
            var needsTranslation = list.Any(item =>
                item.Kind == BatchTaskKind.Translation && TranslationTaskNeedsApi(item));
            return !needsTranslation || EnsureTranslationConfigured();
        }

        private bool RecognitionTaskNeedsApi(BatchTaskItem item)
        {
            if (_project == null) return false;
            var image = _project.Images.FirstOrDefault(candidate => string.Equals(
                candidate.RelativePath,
                item.ImageRelativePath,
                StringComparison.OrdinalIgnoreCase));
            return image == null || image.Regions.Count == 0;
        }

        private bool TranslationTaskNeedsApi(BatchTaskItem item)
        {
            return FindBatchRegions(item.RegionIds).Any(region =>
                !string.IsNullOrWhiteSpace(region.SourceText) &&
                string.IsNullOrWhiteSpace(region.Translation));
        }

        private void RefreshWorkspaceAfterBatch()
        {
            PopulateImageList(_searchBox.Text, _currentDocument);
            if (_currentDocument == null) return;
            _canvas.NotifyRegionChanged();
            _editor.SetDocument(_currentDocument, _canvas.SelectedRegion);
        }

        private string RefreshRecoveredExportReports()
        {
            if (_project == null) return string.Empty;
            var errors = new List<string>();
            var translatedRegions = _project.Images.Sum(image =>
                image.Regions.Count(region => !string.IsNullOrWhiteSpace(region.Translation)));
            foreach (var group in _batchTaskCenter.Items
                         .Where(item => item.Kind == BatchTaskKind.Export &&
                                        !string.IsNullOrWhiteSpace(item.OutputRoot))
                         .GroupBy(item => item.OutputRoot, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Any(item => item.Status == BatchTaskStatus.Pending ||
                                      item.Status == BatchTaskStatus.Running ||
                                      item.Status == BatchTaskStatus.Paused))
                    continue;
                try
                {
                    var failures = group.Where(item => item.Status == BatchTaskStatus.Failed)
                        .Select(item => item.Target + "：" + item.Message)
                        .ToList();
                    WriteExportReport(group.Key, failures, translatedRegions);
                }
                catch (Exception exception)
                {
                    errors.Add(Path.GetFileName(group.Key) + " 报告保存失败：" + exception.Message);
                }
            }
            return string.Join("；", errors);
        }

        private bool RunPreflight(bool exporting)
        {
            if (_project == null) return false;
            Cursor = Cursors.WaitCursor;
            PreflightReport report;
            try
            {
                report = PreflightService.Analyze(_project);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (exporting && report.Issues.Count == 0)
            {
                _statusLabel.Text = "质量预检通过，可以导出";
                return true;
            }

            using (var dialog = new PreflightDialog(report, exporting))
            {
                var result = dialog.ShowDialog(this);
                if (!exporting) return false;
                return report.CanExport && result == DialogResult.OK;
            }
        }

        private void LoadCustomFonts()
        {
            if (_project == null) return;
            using (var dialog = new OpenFileDialog
            {
                Title = "载入中文字体",
                Filter = "字体文件 (*.ttf;*.otf)|*.ttf;*.otf|所有文件 (*.*)|*.*",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var lastFont = string.Empty;
                var failures = new List<string>();
                foreach (var path in dialog.FileNames)
                {
                    try
                    {
                        var names = FontManager.LoadFontFile(path);
                        lastFont = names.LastOrDefault() ?? lastFont;
                        if (!_project.CustomFontFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                            _project.CustomFontFiles.Add(path);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(Path.GetFileName(path) + "：" + exception.Message);
                    }
                }

                _editor.RefreshFontNames(lastFont);
                if (_editor.CurrentRegion != null && !string.IsNullOrWhiteSpace(lastFont))
                {
                    _editor.CurrentRegion.FontFamily = lastFont;
                    _editor.SelectRegion(_editor.CurrentRegion);
                    _canvas.NotifyRegionChanged();
                }
                MarkDirty();
                _statusLabel.Text = "字体已载入，可在每个文字区域中选择";
                ShowFailuresIfAny("部分字体载入失败", failures);
            }
        }

        private void LoadProjectFonts()
        {
            if (_project == null) return;
            foreach (var path in _project.CustomFontFiles.Where(File.Exists))
            {
                try { FontManager.LoadFontFile(path); }
                catch { }
            }
            _editor.RefreshFontNames(_settings.DefaultFontFamily);
        }

        private bool EnsureVisionConfigured()
        {
            if (string.IsNullOrWhiteSpace(_settings.VisionApiBaseUrl) ||
                string.IsNullOrWhiteSpace(_settings.VisionModel) ||
                (string.IsNullOrWhiteSpace(_visionApiKey) && _settings.VisionApiBaseUrl.IndexOf("openai.com", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ShowApiSettings();
            }

            return !string.IsNullOrWhiteSpace(_settings.VisionApiBaseUrl) &&
                   !string.IsNullOrWhiteSpace(_settings.VisionModel) &&
                   (!string.IsNullOrWhiteSpace(_visionApiKey) || _settings.VisionApiBaseUrl.IndexOf("openai.com", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private bool EnsureTranslationConfigured()
        {
            if (string.IsNullOrWhiteSpace(_settings.TranslationApiBaseUrl) ||
                string.IsNullOrWhiteSpace(_settings.TranslationModel) ||
                (string.IsNullOrWhiteSpace(_translationApiKey) && _settings.TranslationApiBaseUrl.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ShowApiSettings();
            }

            return !string.IsNullOrWhiteSpace(_settings.TranslationApiBaseUrl) &&
                   !string.IsNullOrWhiteSpace(_settings.TranslationModel) &&
                   (!string.IsNullOrWhiteSpace(_translationApiKey) || _settings.TranslationApiBaseUrl.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private void PopulateImageList(string filter = null, ImageDocument selected = null)
        {
            if (_project == null)
            {
                _imageList.Items.Clear();
                _imageStatusFilter.Items.Clear();
                return;
            }
            selected = selected ?? _currentDocument;
            var selectedStatus = (_imageStatusFilter.SelectedItem as ImageStatusFilterEntry)?.Status
                                 ?? ImageWorkflowStatus.All;
            _loadingImageList = true;
            _imageList.BeginUpdate();
            _imageStatusFilter.BeginUpdate();
            try
            {
                var statusCounts = Enum.GetValues(typeof(ImageWorkflowStatus))
                    .Cast<ImageWorkflowStatus>()
                    .ToDictionary(status => status, status => status == ImageWorkflowStatus.All
                        ? _project.Images.Count
                        : _project.Images.Count(image => ImageWorkflowClassifier.Classify(image) == status));
                _imageStatusFilter.Items.Clear();
                foreach (ImageWorkflowStatus status in Enum.GetValues(typeof(ImageWorkflowStatus)))
                    _imageStatusFilter.Items.Add(new ImageStatusFilterEntry(status, statusCounts[status]));
                for (var index = 0; index < _imageStatusFilter.Items.Count; index++)
                {
                    if (((ImageStatusFilterEntry)_imageStatusFilter.Items[index]).Status != selectedStatus) continue;
                    _imageStatusFilter.SelectedIndex = index;
                    break;
                }

                _imageList.Items.Clear();
                var query = _project.Images.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(filter))
                    query = query.Where(image => image.RelativePath.IndexOf(filter.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (selectedStatus != ImageWorkflowStatus.All)
                    query = query.Where(image => ImageWorkflowClassifier.Classify(image) == selectedStatus);
                foreach (var image in query)
                    _imageList.Items.Add(new ImageListEntry(image));

                for (var index = 0; index < _imageList.Items.Count; index++)
                {
                    if (ReferenceEquals(((ImageListEntry)_imageList.Items[index]).Document, selected))
                    {
                        _imageList.SelectedIndex = index;
                        break;
                    }
                }
            }
            finally
            {
                _imageStatusFilter.EndUpdate();
                _imageList.EndUpdate();
                _loadingImageList = false;
                _imageList.Invalidate();
            }
        }

        private void DrawImageListItem(object sender, DrawItemEventArgs eventArgs)
        {
            if (eventArgs.Index < 0 || eventArgs.Index >= _imageList.Items.Count) return;
            var entry = (ImageListEntry)_imageList.Items[eventArgs.Index];
            var selected = (eventArgs.State & DrawItemState.Selected) == DrawItemState.Selected;
            var background = selected ? UiTheme.AccentDark : UiTheme.InputBackground;
            using (var brush = new SolidBrush(background))
                eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);

            var thumbnailBounds = new Rectangle(
                eventArgs.Bounds.Left + 7,
                eventArgs.Bounds.Top + 8,
                64,
                54);
            using (var brush = new SolidBrush(Color.FromArgb(7, 14, 24)))
            using (var border = new Pen(UiTheme.BorderSoft))
            {
                eventArgs.Graphics.FillRectangle(brush, thumbnailBounds);
                eventArgs.Graphics.DrawRectangle(border, thumbnailBounds);
            }

            Bitmap thumbnail = null;
            try
            {
                if (_project != null)
                    thumbnail = _thumbnailCache.GetOrQueue(ProjectService.GetSourcePath(_project, entry.Document));
            }
            catch
            {
                // The list remains usable even if one source image was moved or damaged.
            }
            if (thumbnail != null)
            {
                var scale = Math.Min(
                    (thumbnailBounds.Width - 4) / (double)thumbnail.Width,
                    (thumbnailBounds.Height - 4) / (double)thumbnail.Height);
                var width = Math.Max(1, (int)Math.Round(thumbnail.Width * scale));
                var height = Math.Max(1, (int)Math.Round(thumbnail.Height * scale));
                var destination = new Rectangle(
                    thumbnailBounds.Left + (thumbnailBounds.Width - width) / 2,
                    thumbnailBounds.Top + (thumbnailBounds.Height - height) / 2,
                    width,
                    height);
                eventArgs.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                eventArgs.Graphics.DrawImage(thumbnail, destination);
            }
            else
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    "预览",
                    _imageItemDetailFont,
                    thumbnailBounds,
                    UiTheme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var textLeft = thumbnailBounds.Right + 9;
            var textWidth = Math.Max(20, eventArgs.Bounds.Right - textLeft - 7);
            var nameBounds = new Rectangle(textLeft, eventArgs.Bounds.Top + 8, textWidth, 23);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                Path.GetFileName(entry.Document.RelativePath),
                _imageItemTitleFont,
                nameBounds,
                UiTheme.TextPrimary,
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            var translated = entry.Document.Regions.Count(region => !string.IsNullOrWhiteSpace(region.Translation));
            var details = $"{entry.Document.Width}×{entry.Document.Height}  ·  {translated}/{entry.Document.Regions.Count} 译";
            TextRenderer.DrawText(
                eventArgs.Graphics,
                details,
                _imageItemDetailFont,
                new Rectangle(textLeft, eventArgs.Bounds.Top + 32, textWidth, 18),
                UiTheme.TextSecondary,
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            var status = ImageWorkflowClassifier.Classify(entry.Document);
            var statusText = ImageWorkflowClassifier.GetText(status);
            var statusColor = status == ImageWorkflowStatus.Reviewed ? UiTheme.Success
                : status == ImageWorkflowStatus.NeedsTranslation ? UiTheme.Warning
                : status == ImageWorkflowStatus.NeedsReview ? UiTheme.AccentHover
                : UiTheme.TextSecondary;
            TextRenderer.DrawText(
                eventArgs.Graphics,
                statusText,
                _imageItemStatusFont,
                new Rectangle(textLeft, eventArgs.Bounds.Top + 50, textWidth, 17),
                statusColor,
                TextFormatFlags.SingleLine | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            using (var separator = new Pen(UiTheme.BorderSoft))
                eventArgs.Graphics.DrawLine(
                    separator,
                    eventArgs.Bounds.Left,
                    eventArgs.Bounds.Bottom - 1,
                    eventArgs.Bounds.Right,
                    eventArgs.Bounds.Bottom - 1);
        }

        private void RefreshCurrentImageEntry()
        {
            PopulateImageList(_searchBox.Text, _currentDocument);
        }

        private void DeleteSelectedRegion()
        {
            if (_canvas.SelectedRegion == null) return;
            _canvas.DeleteSelected();
            _editor.SetDocument(_currentDocument, null);
            MarkDirty();
        }

        private void ApplyDefaultFont(IEnumerable<TextRegion> regions)
        {
            foreach (var region in regions)
                region.FontFamily = _settings.DefaultFontFamily;
        }

        private static int ApplyTranslations(IEnumerable<TextRegion> regions, IReadOnlyDictionary<string, string> translations)
        {
            var count = 0;
            foreach (var region in regions)
            {
                if (translations.TryGetValue(region.Id, out var translation))
                {
                    region.Translation = translation;
                    region.Reviewed = false;
                    count++;
                }
            }
            return count;
        }

        private static IEnumerable<List<TextRegion>> Chunk(List<TextRegion> items, int size)
        {
            for (var index = 0; index < items.Count; index += size)
                yield return items.GetRange(index, Math.Min(size, items.Count - index));
        }

        private void ResetProjectTracking()
        {
            _historyTimer.Stop();
            _thumbnailCache.Clear();
            _imageStatusFilter.Items.Clear();
            _suppressBatchPersistence = true;
            try
            {
                _batchTaskCenter.ClearAll();
            }
            finally
            {
                _suppressBatchPersistence = false;
            }
            if (_project != null)
            {
                _history.Reset(_project);
                _currentAutosavePath = ProjectService.GetAutosavePath(_project, _projectPath);
                RestoreBatchQueue();
            }
            else
            {
                _currentAutosavePath = string.Empty;
                _batchQueuePath = string.Empty;
            }
            UpdateCommandState();
        }

        private void HandleBatchTaskCenterChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(HandleBatchTaskCenterChanged)); }
                catch (InvalidOperationException) { }
                return;
            }

            var items = _batchTaskCenter.Items;
            var unfinished = items.Count(item =>
                item.Status == BatchTaskStatus.Pending ||
                item.Status == BatchTaskStatus.Running ||
                item.Status == BatchTaskStatus.Paused ||
                item.Status == BatchTaskStatus.Failed);
            _batchNavButton.Text = unfinished > 0
                ? $"⇄   批量处理 ({unfinished})"
                : "⇄   批量处理";
            if (_suppressBatchPersistence || _project == null) return;

            // Running is transient. Fast exports are checkpointed at most once per second;
            // API tasks normally take longer and therefore still checkpoint item by item.
            if (items.Any(item => item.Status == BatchTaskStatus.Running)) return;
            var forceCheckpoint = !_batchTaskCenter.IsRunning ||
                                  items.Any(item => item.Status == BatchTaskStatus.Paused);
            if (!forceCheckpoint && DateTime.UtcNow - _lastBatchCheckpointUtc < TimeSpan.FromSeconds(1))
                return;
            PersistBatchQueue();
            if (_busy && _dirty) SaveBatchRecoveryCheckpoint();
            _lastBatchCheckpointUtc = DateTime.UtcNow;
        }

        private void PersistBatchQueue()
        {
            if (_suppressBatchPersistence || _project == null ||
                string.IsNullOrWhiteSpace(_project.SourceFolder)) return;
            try
            {
                _batchQueuePath = BatchTaskPersistenceService.GetQueuePath(_project.SourceFolder);
                BatchTaskPersistenceService.Save(
                    _batchQueuePath,
                    _project.SourceFolder,
                    _projectPath,
                    _batchTaskCenter.Items);
            }
            catch (Exception exception)
            {
                if (!IsDisposed) _statusLabel.Text = "任务断点保存失败：" + exception.Message;
            }
        }

        private void SaveBatchRecoveryCheckpoint()
        {
            if (_project == null) return;
            try
            {
                _currentAutosavePath = ProjectService.GetAutosavePath(_project, _projectPath);
                ProjectService.SaveAutosave(_currentAutosavePath, _project, _projectPath);
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "工程断点保存失败：" + exception.Message;
            }
        }

        private void RestoreBatchQueue()
        {
            if (_project == null || string.IsNullOrWhiteSpace(_project.SourceFolder)) return;
            _batchQueuePath = BatchTaskPersistenceService.GetQueuePath(_project.SourceFolder);
            _suppressBatchPersistence = true;
            try
            {
                var restored = BatchTaskPersistenceService.Load(_batchQueuePath, _project.SourceFolder).ToList();
                _batchTaskCenter.Restore(restored);
                for (var index = 0; index < restored.Count; index++)
                {
                    var item = restored[index];
                    ReconcileRestoredTask(item);
                    BindRestoredTask(item, index == restored.Count - 1);
                }
            }
            catch (Exception exception)
            {
                _batchTaskCenter.ClearAll();
                _statusLabel.Text = "批量任务恢复失败：" + exception.Message;
            }
            finally
            {
                _suppressBatchPersistence = false;
            }
            PersistBatchQueue();
            HandleBatchTaskCenterChanged();
        }

        private void BindRestoredTask(BatchTaskItem item, bool notify)
        {
            if (_project == null) return;
            if (item.Kind == BatchTaskKind.Recognition)
            {
                var image = _project.Images.FirstOrDefault(candidate => string.Equals(
                    candidate.RelativePath,
                    item.ImageRelativePath,
                    StringComparison.OrdinalIgnoreCase));
                _batchTaskCenter.AttachExecutor(item, ExecuteRecognitionBatchItemAsync, image, notify);
                return;
            }
            if (item.Kind == BatchTaskKind.Translation)
            {
                _batchTaskCenter.AttachExecutor(
                    item,
                    ExecuteTranslationBatchItemAsync,
                    FindBatchRegions(item.RegionIds),
                    notify);
                return;
            }
            _batchTaskCenter.AttachExecutor(
                item,
                ExecuteExportBatchItemAsync,
                new ExportBatchPayload
                {
                    Image = _project.Images.FirstOrDefault(candidate => string.Equals(
                        candidate.RelativePath,
                        item.ImageRelativePath,
                        StringComparison.OrdinalIgnoreCase)),
                    MetadataRelativePath = item.MetadataRelativePath
                },
                notify);
        }

        private void ReconcileRestoredTask(BatchTaskItem item)
        {
            if (_project == null || item.Status != BatchTaskStatus.Completed) return;
            if (item.Kind == BatchTaskKind.Recognition && item.ResultCount > 0)
            {
                var image = _project.Images.FirstOrDefault(candidate => string.Equals(
                    candidate.RelativePath,
                    item.ImageRelativePath,
                    StringComparison.OrdinalIgnoreCase));
                if (image == null || image.Regions.Count == 0)
                {
                    item.Status = BatchTaskStatus.Pending;
                    item.Message = "工程缺少上次识别结果，等待重新处理";
                }
            }
            else if (item.Kind == BatchTaskKind.Translation)
            {
                var regions = FindBatchRegions(item.RegionIds);
                if (regions.Count != item.RegionIds.Count || regions.Any(region =>
                        !string.IsNullOrWhiteSpace(region.SourceText) &&
                        string.IsNullOrWhiteSpace(region.Translation)))
                {
                    item.Status = BatchTaskStatus.Pending;
                    item.Message = "工程缺少上次翻译结果，等待重新处理";
                }
            }
        }

        private void CaptureHistorySnapshot()
        {
            if (_project == null || _restoringHistory) return;
            _history.Capture(_project);
            UpdateCommandState();
        }

        private void UndoProjectChange()
        {
            if (_project == null || _busy) return;
            _historyTimer.Stop();
            CaptureHistorySnapshot();
            var restored = _history.Undo();
            if (restored == null) return;
            RestoreProjectFromHistory(restored, "已撤销上一步");
        }

        private void RedoProjectChange()
        {
            if (_project == null || _busy) return;
            _historyTimer.Stop();
            CaptureHistorySnapshot();
            var restored = _history.Redo();
            if (restored == null) return;
            RestoreProjectFromHistory(restored, "已重做下一步");
        }

        private void RestoreProjectFromHistory(TranslationProject restored, string status)
        {
            var relativePath = _currentDocument?.RelativePath;
            var selectedRegionId = _canvas.SelectedRegion?.Id;
            _restoringHistory = true;
            try
            {
                _project = restored;
                _currentDocument = _project.Images.FirstOrDefault(image =>
                    string.Equals(image.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                    ?? _project.Images.FirstOrDefault();
                _currentAutosavePath = ProjectService.GetAutosavePath(_project, _projectPath);
                LoadProjectFonts();
                PopulateImageList(_searchBox.Text, _currentDocument);
                if (_currentDocument != null)
                {
                    LoadDocument(_currentDocument);
                    var selected = _currentDocument.Regions.FirstOrDefault(region =>
                        string.Equals(region.Id, selectedRegionId, StringComparison.OrdinalIgnoreCase));
                    if (selected != null) _canvas.SelectRegion(selected);
                }
                else
                {
                    _canvas.ClearDocument();
                    _editor.SetDocument(null, null);
                }
                _dirty = true;
                _statusLabel.Text = status;
                UpdateDashboard();
                UpdateTitle();
            }
            finally
            {
                _restoringHistory = false;
                UpdateCommandState();
            }
        }

        private void AutoSaveProject()
        {
            if (_project == null || !_dirty || _busy || _restoringHistory) return;
            try
            {
                _historyTimer.Stop();
                CaptureHistorySnapshot();
                _currentAutosavePath = ProjectService.GetAutosavePath(_project, _projectPath);
                ProjectService.SaveAutosave(_currentAutosavePath, _project, _projectPath);
                _statusLabel.Text = "已自动保存恢复点  " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "自动保存失败：" + exception.Message;
            }
        }

        private void TryOfferLatestAutosaveRecovery()
        {
            if (_project != null) return;
            foreach (var path in ProjectService.FindAutosaves())
            {
                try
                {
                    var autosave = ProjectService.LoadAutosave(path);
                    if (!Directory.Exists(autosave.Project.SourceFolder))
                    {
                        ProjectService.DeleteAutosave(path);
                        continue;
                    }

                    var answer = MessageBox.Show(this,
                        $"发现 {autosave.SavedAt:yyyy-MM-dd HH:mm:ss} 的自动恢复工程。\r\n\r\n源目录：{autosave.Project.SourceFolder}\r\n\r\n是否恢复？",
                        "恢复未保存工程", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes)
                    {
                        RestoreAutosave(path, autosave);
                    }
                    else
                    {
                        ProjectService.DeleteAutosave(path);
                    }
                    break;
                }
                catch
                {
                    ProjectService.DeleteAutosave(path);
                }
            }
        }

        private void RestoreAutosave(string path, AutosaveDocument autosave)
        {
            _project = autosave.Project;
            _projectPath = string.IsNullOrWhiteSpace(autosave.OriginalProjectPath)
                ? null
                : autosave.OriginalProjectPath;
            _currentDocument = _project.Images.FirstOrDefault();
            _dirty = true;
            ResetProjectTracking();
            _currentAutosavePath = path;
            LoadProjectFonts();
            PopulateImageList(null, _currentDocument);
            if (_currentDocument != null) LoadDocument(_currentDocument);
            ShowWorkspacePage(_workspaceNavButton);
            _statusLabel.Text = "已恢复自动保存工程，请确认后正式保存";
            UpdateDashboard();
            UpdateTitle();
            UpdateCommandState();
        }

        private void BeginOperation(string status)
        {
            _historyTimer.Stop();
            CaptureHistorySnapshot();
            _busy = true;
            _operationCancellation = new CancellationTokenSource();
            _statusLabel.Text = status;
            UpdateCommandState();
        }

        private void EndOperation()
        {
            _busy = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            CaptureHistorySnapshot();
            UpdateCommandState();
        }

        private void UpdateCommandState()
        {
            var hasProject = _project != null;
            var hasImage = _currentDocument != null;
            _openFolderButton.Enabled = !_busy;
            _openProjectButton.Enabled = !_busy;
            _saveButton.Enabled = hasProject && !_busy;
            _undoButton.Enabled = hasProject && _history.CanUndo && !_busy;
            _redoButton.Enabled = hasProject && _history.CanRedo && !_busy;
            _visionButton.Enabled = hasImage && !_busy;
            _visionBatchButton.Enabled = hasProject && !_busy;
            _translateButton.Enabled = hasImage && !_busy;
            _translateAllButton.Enabled = hasProject && !_busy;
            _translationResourcesButton.Enabled = !_busy;
            _drawButton.Enabled = hasImage && !_busy;
            _deleteButton.Enabled = _canvas.SelectedRegion != null && !_busy;
            _maskBrushButton.Enabled = _canvas.SelectedRegion != null && !_busy;
            _maskEraserButton.Enabled = _canvas.SelectedRegion != null && !_busy;
            _maskSizeButton.Enabled = _canvas.SelectedRegion != null && !_busy;
            _maskClearButton.Enabled = _canvas.HasSelectedMask && !_busy;
            _previewButton.Enabled = hasImage && !_busy;
            _compareButton.Enabled = hasImage && !_busy;
            _atlasButton.Enabled = hasImage && _currentDocument.AtlasSprites.Count > 0 && !_busy;
            _fitButton.Enabled = hasImage && !_busy;
            _preflightButton.Enabled = hasProject && !_busy;
            _exportButton.Enabled = hasProject && !_busy;
            _settingsButton.Enabled = !_busy;
            _cancelOperationButton.Enabled = _busy;
            _searchBox.Enabled = !_busy;
            _imageStatusFilter.Enabled = !_busy;
            _imageList.Enabled = !_busy;
            // Keep property labels readable when no image is open. Input handlers
            // already ignore edits until a text region is selected.
            _editor.Enabled = !_busy;
        }

        private void MarkDirty()
        {
            if (_project == null) return;
            _dirty = true;
            if (!_busy && !_restoringHistory)
            {
                _historyTimer.Stop();
                _historyTimer.Start();
            }
            UpdateDashboard();
            UpdateTitle();
        }

        private void PersistReviewedTranslations()
        {
            if (_project == null) return;
            try
            {
                _translationResources.CollectReviewed(_project);
                if (_translationResources.IsDirty) _translationResources.Save();
            }
            catch
            {
                // Project editing must continue even if the local memory file cannot be written.
            }
        }

        private void UpdateTitle()
        {
            var name = string.IsNullOrWhiteSpace(_projectPath) ? "未命名工程" : Path.GetFileName(_projectPath);
            Text = $"Galgame UI 图片汉化工具 - {name}{(_dirty ? " *" : string.Empty)}";
        }

        private bool ConfirmDiscardChanges()
        {
            if (!_dirty) return true;
            var answer = MessageBox.Show(this, "当前工程有未保存修改。是否先保存？",
                "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return SaveProject(false);
            ProjectService.DeleteAutosave(_currentAutosavePath);
            return true;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy && _batchTaskCenter.IsRunning)
            {
                var preserve = MessageBox.Show(this,
                    "批量任务仍在运行。退出时将保存当前工程和任务断点，下次打开工程后可点击“继续未完成”。是否退出？",
                    "保存断点并退出", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (preserve != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _batchTaskCenter.SuspendForShutdown();
                _operationCancellation?.Cancel();
                SaveBatchRecoveryCheckpoint();
                PersistBatchQueue();
                return;
            }

            if (_busy)
            {
                var answer = MessageBox.Show(this, "任务仍在运行。确认取消任务并退出？",
                    "任务运行中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _operationCancellation?.Cancel();
                _batchTaskCenter.Cancel();
            }

            if (!ConfirmDiscardChanges()) e.Cancel = true;
        }

        private void OnMainKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                SaveProject(false);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Z && !(ActiveControl is TextBoxBase))
            {
                UndoProjectChange();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Y && !(ActiveControl is TextBoxBase))
            {
                RedoProjectChange();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown))
            {
                DeleteSelectedRegion();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F)
            {
                _canvas.ZoomToFit();
            }
        }

        private void WriteExportReport(string outputRoot, IReadOnlyCollection<string> failures, int translatedRegions)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Galgame UI 图片汉化导出报告");
            builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("源目录：" + _project.SourceFolder);
            builder.AppendLine("输出目录：" + outputRoot);
            builder.AppendLine("图片总数：" + _project.Images.Count);
            builder.AppendLine("文字区域：" + _project.Images.Sum(image => image.Regions.Count));
            builder.AppendLine("已有译文：" + translatedRegions);
            builder.AppendLine("失败图片：" + failures.Count);
            if (failures.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("失败详情：");
                foreach (var failure in failures) builder.AppendLine("- " + failure);
            }
            File.WriteAllText(Path.Combine(outputRoot, "UI汉化导出报告.txt"), builder.ToString(), Encoding.UTF8);
        }

        private static void ShowFailuresIfAny(string title, IReadOnlyCollection<string> failures)
        {
            if (failures.Count == 0) return;
            var message = string.Join("\r\n", failures.Take(12));
            if (failures.Count > 12) message += $"\r\n……另有 {failures.Count - 12} 项";
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "推荐流程：\r\n\r\n" +
                "1. 打开解包后的 PNG/JPG/BMP/DDS 图片文件夹。\r\n" +
                "2. 在 API 设置中填写 DeepSeek 文本翻译接口；视觉 API 可以暂时留空。\r\n" +
                "3. 使用视觉 API 自动识别，或点击“框选文字”手工建立区域并录入日文。\r\n" +
                "4. 点击“翻译当前图”或“翻译全部待译”。\r\n" +
                "5. 逐框检查字体、渐变、阴影、发光、旋转、竖排和换行。\r\n" +
                "6. 复杂背景先选中文字框，再使用“蒙版笔/蒙版擦”和“内容感知修复”。\r\n" +
                "7. 打开预览并执行预检，再从批量任务中心观察导出进度。\r\n\r\n" +
                "操作：滚轮缩放，中键平移；拖动文字框移动，拖动右下角调整大小；Ctrl+S 保存。",
                "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowError(string title, Exception exception)
        {
            _statusLabel.Text = title;
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private sealed class ImageStatusFilterEntry
        {
            public ImageStatusFilterEntry(ImageWorkflowStatus status, int count)
            {
                Status = status;
                Count = count;
            }

            public ImageWorkflowStatus Status { get; }
            public int Count { get; }

            public override string ToString()
            {
                var label = Status == ImageWorkflowStatus.All
                    ? "全部图片"
                    : ImageWorkflowClassifier.GetText(Status);
                return $"{label}（{Count}）";
            }
        }

        private sealed class ImageListEntry
        {
            public ImageListEntry(ImageDocument document) => Document = document;
            public ImageDocument Document { get; }

            public override string ToString()
            {
                if (Document.Regions.Count == 0) return "○ " + Document.RelativePath;
                var translated = Document.Regions.Count(region => !string.IsNullOrWhiteSpace(region.Translation));
                var reviewed = Document.Regions.Count(region => region.Reviewed);
                return $"● {Document.RelativePath}  [{translated}/{Document.Regions.Count}译, {reviewed}校]";
            }
        }

        private sealed class ExportBatchPayload
        {
            public ImageDocument Image { get; set; }
            public string MetadataRelativePath { get; set; } = string.Empty;
        }
    }
}
