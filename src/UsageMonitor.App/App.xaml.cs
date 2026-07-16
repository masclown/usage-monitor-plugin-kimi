using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Drawing;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.App.ViewModels;
using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;
// ★ WPF/WinForms 命名冲突 alias：项目 UseWPF + UseWindowsForms + ImplicitUsings 下
//   全局注入 System.Windows.Forms，导致 MessageBox 等类型 ambiguous。
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace UsageMonitor.App;

/// <summary>
/// 应用程序入口 - 初始化系统托盘、插件管理器和核心服务
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private PluginManager _pluginManager = null!;
    private ConfigService _configService = null!;
    private RefreshService _refreshService = null!;
    private UsageHistoryStore _historyStore = null!;
    private UsageHistoryRepository _historyRepository = null!;
    private MainViewModel _viewModel = null!;
    private MainWindow? _mainWindow;
    private Views.TaskbarWindow? _taskbarWindow;
    private Helpers.TaskbarHelper? _taskbarHelper;
    private Views.TrayTooltipWindow? _trayTooltipWindow;
    private Views.HistoryWindow? _historyWindow;
    private DispatcherTimer? _trayHoverCheckTimer;
    private bool _isCursorOverTrayArea;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 住一次全局未捕获异常到 FileLogger，下次启动期 XAML 解析报错时能直接看到异常详情。
        DispatcherUnhandledException += (_, args) =>
        {
            var ex = args.Exception;
            // XamlParseException 的 InnerException 才是真正的根源，逐层写到日志。
            while (ex != null)
            {
                FileLogger.Error("App",
                    $"{(ex == args.Exception ? "Unhandled" : "Caused-by")} UI exception: {ex.GetType().Name}: {ex.Message}",
                    ex);
                ex = ex.InnerException;
            }
            // 不调用 Handled = true，让 WPF 默认行为继续以防状态破坏。
        };

        // Initialize file logger first so all subsequent startup is captured
        FileLogger.Info("App", $"=== UsageMonitor startup (PID={Environment.ProcessId}) ===");
        FileLogger.RotateIfNeeded();

        // 初始化核心服务
        _configService = new ConfigService();
        _configService.Load();

        // 应用已保存的外观主题（必须在任何窗口/控件构造之前，保证首屏即为目标主题）
        Helpers.ThemeManager.Apply(_configService.Settings.Theme);
        // Register the live ConfigService with BrowserLoginService so that when login
        // writes new cookies to config.json directly, the in-memory provider config can
        // be reloaded — avoiding the user having to restart the app.
        BrowserLoginService.RegisterConfigService(_configService);

        _pluginManager = new PluginManager();

        // 先加载外部插件（会清空列表）
        _pluginManager.LoadPlugins();

        // 再注册内置插件（不会被清空）
        RegisterBuiltinPlugins();

        // 同步插件启用状态
        foreach (var plugin in _pluginManager.Plugins)
        {
            plugin.IsEnabled = _configService.Settings.PluginEnabled
                .GetValueOrDefault(plugin.Provider.ProviderId, true);
        }

        // 创建用量历史持久化仓库（SQLite，%AppData%/UsageMonitor/history.db）
        _historyRepository = UsageHistoryRepository.CreateDefault();
        _historyRepository.EnsureSchema();

        // 创建历史数据存储，并同位启用持久化：刷新时 AddPoint 会 fire-and-forget 写 SQL
        _historyStore = new UsageHistoryStore(_historyRepository);
        _historyStore.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);

        // 启动时回填最近 N 个点，避免重启后折线图从头画起（同步等待，避免首屏闪空）
        try
        {
            var providerIds = _pluginManager.Plugins.Select(p => p.Provider.ProviderId).ToList();
            var historyPoints = Math.Max(1, _configService.Settings.HistoryPointCount);
            // LoadFromRepositoryAsync 是 async；这里启动任务即可，后续 _historyStore.HistoryChanged 会触发 UI 重绘
            _ = _historyStore.LoadFromRepositoryAsync(_historyRepository, providerIds, historyPoints);
        }
        catch (Exception ex)
        {
            FileLogger.Error("App", "Initial LoadFromRepositoryAsync failed", ex);
        }

        _refreshService = new RefreshService(_pluginManager, _configService, _historyStore);

        // 创建ViewModel
        _viewModel = new MainViewModel(_pluginManager, _configService, _refreshService, _historyStore);

        // 初始化系统托盘
        InitializeTrayIcon();

        // 监听用量更新
        _refreshService.UsageRefreshed += OnUsageRefreshed;

        // 启动定时刷新
        _refreshService.Start();

        // 初始化任务栏窗口
        if (_configService.Settings.ShowInTaskbar)
            InitializeTaskbarWindow();

        // 初始化托盘悬浮窗
        if (_configService.Settings.ShowTrayTooltip)
            InitializeTrayTooltip();

        // 监听 ViewModel 中 Provider 显示模式变化以重算任务栏窗口尺寸
        _viewModel.Usages.CollectionChanged += (_, _) => _taskbarWindow?.RecalculateSize();
        foreach (var usage in _viewModel.Usages)
        {
            usage.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(ProviderUsageViewModel.DisplayMode))
                    _taskbarWindow?.RecalculateSize();
            };
        }

        // 显示主窗口
        ShowMainWindow();
    }

    /// <summary>
    /// 注册内置插件（Deepseek、MiMo、OpenAI、MiniMax）
    /// </summary>
    private void RegisterBuiltinPlugins()
    {
        _pluginManager.RegisterPlugin(new Plugin.Deepseek.DeepseekProvider());
        _pluginManager.RegisterPlugin(new Plugin.MiMo.MiMoProvider());
        _pluginManager.RegisterPlugin(new Plugin.OpenAI.OpenAIProvider());
        _pluginManager.RegisterPlugin(new Plugin.MiniMax.MiniMaxProvider());
    }

    /// <summary>
    /// 初始化系统托盘图标和右键菜单
    /// </summary>
    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "UsageMonitor - AI用量监控",
            Icon = SystemIcons.Application, // 后续替换为自定义图标
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        // 构建右键菜单
        var contextMenu = new ContextMenuStrip();

        contextMenu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("立即刷新", null, async (_, _) => await _refreshService.RefreshAllAsync());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("设置", null, (_, _) => ShowSettingsWindow());
        contextMenu.Items.Add("历史", null, (_, _) => ShowHistoryWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (_, _) => Shutdown());

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// 初始化托盘悬浮窗 + 鼠标悬停检测定时器
    /// </summary>
    private void InitializeTrayTooltip()
    {
        // 传入 ConfigService 使悬浮窗可以读取/写入拖拽后的位置
        _trayTooltipWindow = new Views.TrayTooltipWindow(_viewModel, _configService);

        // 启动轮询定时器（100ms 一次，检测光标位置）
        _trayHoverCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _trayHoverCheckTimer.Tick += OnTrayHoverCheckTick;
        _trayHoverCheckTimer.Start();
    }

    /// <summary>
    /// 轮询检测光标是否在托盘图标区域 / 悬浮窗区域内
    /// - 进入托盘区域：显示悬浮窗
    /// - 离开托盘区域与悬浮窗：延迟隐藏
    /// </summary>
    private void OnTrayHoverCheckTick(object? sender, EventArgs e)
    {
        if (_trayTooltipWindow == null) return;

        var cursorPos = System.Windows.Forms.Cursor.Position;
        // 转换为 WPF 屏幕坐标
        var wpfPos = new Point(cursorPos.X, cursorPos.Y);

        var inTrayArea = IsCursorInTrayArea(cursorPos);
        var inTooltipArea = IsCursorInTooltipArea(cursorPos);

        if (inTrayArea || inTooltipArea)
        {
            if (!_isCursorOverTrayArea)
            {
                _isCursorOverTrayArea = true;
                if (_configService.Settings.ShowTrayTooltip)
                {
                    _trayTooltipWindow.ShowNearCursor(wpfPos);
                }
            }
        }
        else
        {
            if (_isCursorOverTrayArea)
            {
                _isCursorOverTrayArea = false;
                // 离开托盘区域后启动延迟关闭
                _trayTooltipWindow.RequestHide(_configService.Settings.TrayTooltipHideDelayMs);
            }
        }
    }

    /// <summary>
    /// 判断光标是否在系统托盘图标矩形区域（任务栏右下角，尺寸由用户配置的
    /// TrayTriggerWidth x TrayTriggerHeight 决定，默认 200x40）。
    /// 因需读取 _configService，改为实例方法；轮询每次都重新读取配置，保存后即时生效。
    /// </summary>
    private bool IsCursorInTrayArea(System.Drawing.Point cursor)
    {
        var workArea = SystemParameters.WorkArea;
        // 触发区域尺寸取自配置，并加下限保护（与 MainViewModel setter 一致），避免设为 0 后永远无法触发。
        var width = Math.Max(20, _configService.Settings.TrayTriggerWidth);
        var height = Math.Max(10, _configService.Settings.TrayTriggerHeight);
        // 任务栏右下角托盘区域（屏幕坐标系，WorkArea 已经是 WPF 坐标，需要转 WinForms）
        var trayLeft = (int)(workArea.Right - width);
        var trayTop = (int)workArea.Bottom;
        var trayRight = (int)workArea.Right;
        var trayBottom = trayTop + height;
        return cursor.X >= trayLeft && cursor.X <= trayRight
            && cursor.Y >= trayTop && cursor.Y <= trayBottom;
    }

    /// <summary>
    /// 判断光标是否在悬浮窗矩形区域内
    /// </summary>
    private bool IsCursorInTooltipArea(System.Drawing.Point cursor)
    {
        if (_trayTooltipWindow == null || !_trayTooltipWindow.IsVisible) return false;
        var left = (int)_trayTooltipWindow.Left;
        var top = (int)_trayTooltipWindow.Top;
        var right = left + (int)_trayTooltipWindow.ActualWidth;
        var bottom = top + (int)_trayTooltipWindow.ActualHeight;
        return cursor.X >= left && cursor.X <= right
            && cursor.Y >= top && cursor.Y <= bottom;
    }

    /// <summary>
    /// 用量数据更新回调
    /// </summary>
    private void OnUsageRefreshed(object? sender, UsageRefreshedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.UpdateUsages(e.Usages);

            // 更新托盘提示文本
            if (_notifyIcon != null)
            {
                var tooltipParts = e.Usages
                    .Where(u => u.IsSuccess)
                    .Select(u => u.GetShortDisplayText());
                var tooltip = string.Join("\n", tooltipParts);
                _notifyIcon.Text = string.IsNullOrEmpty(tooltip) ? "UsageMonitor" : tooltip;
            }
        });
    }

    /// <summary>
    /// 初始化任务栏嵌入窗口：构造 TaskbarHelper 并真正通过 EmbedWindow 嵌入任务栏。
    /// </summary>
    private void InitializeTaskbarWindow()
    {
        if (!_configService.Settings.ShowInTaskbar) return;

        _taskbarHelper = new Helpers.TaskbarHelper();
        _taskbarWindow = new Views.TaskbarWindow(_viewModel, _configService, _taskbarHelper);
        _taskbarWindow.EmbedIntoTaskbar();
    }

    /// <summary>
    /// 显示主窗口
    /// </summary>
    private void ShowMainWindow()
    {
        // 关闭托盘悬浮窗
        _trayTooltipWindow?.ForceHide();

        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_viewModel);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    /// <summary>
    /// 显示设置窗口
    /// </summary>
    private void ShowSettingsWindow()
    {
        // 关闭托盘悬浮窗
        _trayTooltipWindow?.ForceHide();

        var settingsWindow = new Views.SettingsWindow(_viewModel);
        settingsWindow.Owner = _mainWindow;
        settingsWindow.ShowDialog();
    }

    /// <summary>
    /// 显示历史窗口（多 Provider 用量历史 + SQLite 数据查询）
    /// </summary>
    public void ShowHistoryWindow()
    {
        // 关闭托盘悬浮窗
        _trayTooltipWindow?.ForceHide();

        try
        {
            // 同一个 history 窗口复用，避免重复打开多个实例。
            if (_historyWindow == null || !_historyWindow.IsLoaded)
            {
                var installed = _pluginManager.Plugins
                    .Select(p => (providerId: p.Provider.ProviderId, displayName: p.Provider.DisplayName));
                var vm = new ViewModels.HistoryViewModel(_historyRepository, installed);
                _historyWindow = new Views.HistoryWindow(vm);
                _historyWindow.Closed += (_, _) => _historyWindow = null;
            }
            _historyWindow.Owner = _mainWindow;
            _historyWindow.Show();
            _historyWindow.Activate();
        }
        catch (Exception ex)
        {
            FileLogger.Error("App", "ShowHistoryWindow failed", ex);
            MessageBox.Show("打开历史窗口失败：" + ex.Message,
                "历史", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLogger.Info("App", "=== UsageMonitor exiting ===");
        _trayHoverCheckTimer?.Stop();
        _refreshService.Dispose();
        _taskbarWindow?.Close();
        _historyWindow?.Close();
        _trayTooltipWindow?.Close();
        _taskbarHelper?.Dispose();
        _notifyIcon?.Dispose();
        _configService.Save();
        _historyRepository?.Dispose();
        FileLogger.Flush();
        base.OnExit(e);
    }
}
