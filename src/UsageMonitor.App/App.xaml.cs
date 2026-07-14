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
    private MainViewModel _viewModel = null!;
    private MainWindow? _mainWindow;
    private Views.TaskbarWindow? _taskbarWindow;
    private Views.TrayTooltipWindow? _trayTooltipWindow;
    private DispatcherTimer? _trayHoverCheckTimer;
    private bool _isCursorOverTrayArea;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize file logger first so all subsequent startup is captured
        FileLogger.Info("App", $"=== UsageMonitor startup (PID={Environment.ProcessId}) ===");
        FileLogger.RotateIfNeeded();

        // 初始化核心服务
        _configService = new ConfigService();
        _configService.Load();
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

        // 创建历史数据存储
        _historyStore = new UsageHistoryStore();
        _historyStore.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);

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
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (_, _) => Shutdown());

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// 初始化托盘悬浮窗 + 鼠标悬停检测定时器
    /// </summary>
    private void InitializeTrayTooltip()
    {
        _trayTooltipWindow = new Views.TrayTooltipWindow(_viewModel);

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
    /// 判断光标是否在系统托盘图标矩形区域（任务栏右下角 200x40）
    /// </summary>
    private static bool IsCursorInTrayArea(System.Drawing.Point cursor)
    {
        var workArea = SystemParameters.WorkArea;
        // 任务栏右下角托盘区域（屏幕坐标系，WorkArea 已经是 WPF 坐标，需要转 WinForms）
        var trayLeft = (int)(workArea.Right - 200);
        var trayTop = (int)workArea.Bottom;
        var trayRight = (int)workArea.Right;
        var trayBottom = trayTop + 40;
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
    /// 初始化任务栏嵌入窗口
    /// </summary>
    private void InitializeTaskbarWindow()
    {
        _taskbarWindow = new Views.TaskbarWindow(_viewModel);
        _taskbarWindow.ShowInTaskbarDisplay();
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

    protected override void OnExit(ExitEventArgs e)
    {
        FileLogger.Info("App", "=== UsageMonitor exiting ===");
        _trayHoverCheckTimer?.Stop();
        _refreshService.Dispose();
        _taskbarWindow?.Close();
        _trayTooltipWindow?.Close();
        _notifyIcon?.Dispose();
        _configService.Save();
        FileLogger.Flush();
        base.OnExit(e);
    }
}
