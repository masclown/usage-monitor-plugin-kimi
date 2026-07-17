using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Drawing;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Models;
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

        // 显式初始化本地化服务（显式调用作为未来从配置/系统区域读取语言的占位入口；
        // 当前 I18n 已默认 zh-CN，这里主要是为了在日志中留下一条可观察的初始化记录）。
        I18n.SetLanguage(I18n.DefaultLanguage);
        FileLogger.Info("App", $"I18n initialized: language='{I18n.CurrentLanguage}'");

        // 初始化核心服务
        _configService = new ConfigService();
        _configService.Load();

        // 应用已保存的外观主题（必须在任何窗口/控件构造之前，保证首屏即为目标主题）
        Helpers.ThemeManager.Apply(_configService.Settings.Theme);

        // 加载用量色阶到全局静态表（保证 XAML 首次绑定 PercentToBrushConverter 时拿到正确颜色）。
        UsageMonitor.App.Helpers.UsageTierScale.ApplyConfig(_configService.GetEffectiveUsageTierConfig());
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

        // REQ-004：把"在屏幕上调整"蒙版打开动作与 MainViewModel.OpenTriggerOverlayAction 绑定。
        // 使用 System.Action 无参委托，避免 RelayCommand 无法接 1 参数（未使用）。
        _viewModel.OpenTriggerOverlayAction = ShowTriggerOverlayWindow;

        // 订阅配置变更：让"启用任务栏显示/启用托盘悬浮窗"两个开关在运行时即时生效（关闭时销毁、开启时重建）
        _configService.ConfigChanged += (_, _) => Dispatcher.Invoke(SyncOverlayWindowsFromSettings);
        // 订阅配置变更：用量色阶在设置页保存后即时同步到全局色阶（让所有进度条 / 热力图重新取色）。
        _configService.ConfigChanged += (_, _) => Dispatcher.Invoke(() =>
            UsageMonitor.App.Helpers.UsageTierScale.ApplyConfig(_configService.GetEffectiveUsageTierConfig()));

        // 初始化系统托盘
        InitializeTrayIcon();

        // 监听用量更新
        _refreshService.UsageRefreshed += OnUsageRefreshed;

        // 启动定时刷新
        _refreshService.Start();

        // 按当前配置同步任务栏窗口 + 托盘悬浮窗（运行时勾选/取消会通过 ConfigChanged 回调即时同步）
        SyncOverlayWindowsFromSettings();

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
    /// 初始化托盘悬浮窗 + 鼠标悬停检测定时器。
    /// 幂等：已存在则跳过；开关关闭时不创建。
    /// </summary>
    private void InitializeTrayTooltip()
    {
        if (_trayTooltipWindow != null) return;
        if (!_configService.Settings.ShowTrayTooltip) return;

        // 传入 ConfigService 使悬浮窗可以读取/写入拖拽后的位置
        _trayTooltipWindow = new Views.TrayTooltipWindow(_viewModel, _configService);

        // 启动轮询定时器（100ms 一次，检测光标位置）
        _trayHoverCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _trayHoverCheckTimer.Tick += OnTrayHoverCheckTick;
        _trayHoverCheckTimer.Start();
        FileLogger.Info("App", "TrayTooltipWindow 已创建并启动悬停检测定时器");
    }

    /// <summary>
    /// 销毁托盘悬浮窗：先停轮询定时器（避免定时器回调继续访问已释放的窗），再关闭悬浮窗实例并清空字段。
    /// 由 SyncOverlayWindowsFromSettings 在用户关闭对应开关时调用，允许下次需要时重新 Initialize。
    /// </summary>
    private void DisposeTrayTooltip()
    {
        if (_trayHoverCheckTimer != null)
        {
            _trayHoverCheckTimer.Stop();
            _trayHoverCheckTimer = null;
        }
        _isCursorOverTrayArea = false;

        if (_trayTooltipWindow != null)
        {
            _trayTooltipWindow.Close();
            _trayTooltipWindow = null;
        }
        FileLogger.Info("App", "TrayTooltipWindow 已销毁并停止悬停检测定时器");
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
    /// REQ-004：判断光标是否在系统托盘图标矩形区域。读取
    /// <see cref="AppSettings.TrayTooltipTriggerRect"/> 后用 <see cref="RectInt.Contains"/> 判断命中。
    /// <para>
    /// 与 v1（仅用右下角 TrayTriggerWidth/Height）区别：本方法全面切到 X/Y/W/H 模型，
    /// 范围不再限于屏幕右下角，理论可调为屏幕任意矩形；未配置时回退到 RectInt.DefaultBottomRight()。
    /// </para>
    /// </summary>
    private bool IsCursorInTrayArea(System.Drawing.Point cursor)
    {
        RectInt r;
        try { r = _configService.Settings.TrayTooltipTriggerRect; }
        catch { r = RectInt.DefaultBottomRight(); }
        // RectInt NaN / 0 守底：宽高 <0 时交给 RectInt.Contains 自然返回 false；这里额外截断退化情况。
        if (r.Width <= 0 || r.Height <= 0) r = RectInt.DefaultBottomRight();
        return r.Contains(cursor);
    }

    /// <summary>
    /// REQ-004：打开托盘悬浮窗触发区域调试矩形（由 MainViewModel.EditTriggerAreaCommand 调用）。
    /// <para>
    /// 幂等：单例复用避免重复创建闪烁；设置窗口未打开时也能直接由托盘上下文菜单触发（如未来扩展）。
    /// </para>
    /// </summary>
    private void ShowTriggerOverlayWindow()
    {
        var existing = System.Windows.Application.Current.Windows
            .OfType<Views.TriggerAreaOverlayWindow>()
            .FirstOrDefault(w => w.IsVisible);
        if (existing != null)
        {
            existing.Activate();
            return;
        }
        var overlay = new Views.TriggerAreaOverlayWindow(_configService);
        overlay.Closed += (_, _) => FileLogger.Info("App", "TriggerAreaOverlayWindow 已关闭");
        FileLogger.Info("App", "TriggerAreaOverlayWindow 已打开");
        overlay.Show();
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
    /// 幂等：已存在则跳过；开关关闭时不创建；创建后绑定 Usages 变更回调以响应 Provider 显隐/模式变化。
    /// </summary>
    private void InitializeTaskbarWindow()
    {
        if (_taskbarWindow != null) return;
        if (!_configService.Settings.ShowInTaskbar) return;

        _taskbarHelper = new Helpers.TaskbarHelper();
        _taskbarWindow = new Views.TaskbarWindow(_viewModel, _configService, _taskbarHelper);
        _taskbarWindow.EmbedIntoTaskbar();
        AttachTaskbarWindowResizeHandlers();
        FileLogger.Info("App", "TaskbarWindow 已创建并嵌入任务栏");
    }

    /// <summary>
    /// 销毁任务栏窗口：先关窗、后解嵌入 TaskbarHelper、最后清空字段，允许 SyncOverlayWindowsFromSettings 之后重新创建。
    /// </summary>
    private void DisposeTaskbarWindow()
    {
        if (_taskbarWindow == null && _taskbarHelper == null) return;

        // 先关窗后解嵌入：避免 OnPreviewMouseXxx 在 SetParent(0) 之后还访问 _taskbarHelper
        if (_taskbarWindow != null)
        {
            _taskbarWindow.Close();
            _taskbarWindow = null;
        }
        _taskbarHelper?.Dispose();
        _taskbarHelper = null;
        FileLogger.Info("App", "TaskbarWindow 已销毁并从任务栏解嵌入");
    }

    /// <summary>
    /// 绑定 ViewModel 中 Provider 集合与单个 Provider 的显示模式变化事件，触发任务栏窗口重排宽度/位置。
    /// 重建 TaskbarWindow 时必须重新订阅一次，否则新窗口失去与 Provider 变化联动的响应能力。
    /// </summary>
    private void AttachTaskbarWindowResizeHandlers()
    {
        _viewModel.Usages.CollectionChanged += (_, _) => _taskbarWindow?.RecalculateSize();
        foreach (var usage in _viewModel.Usages)
        {
            usage.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(ProviderUsageViewModel.DisplayMode))
                    _taskbarWindow?.RecalculateSize();
            };
        }
    }

    /// <summary>
    /// 按当前 ConfigService.Settings 中 ShowInTaskbar / ShowTrayTooltip 两个开关，同步两个 overlay window 的运行实例状态。
    /// 由 _configService.ConfigChanged 触发（包一层 Dispatcher.Invoke 兼容跨线程），
    /// 也由 OnStartup 在首次初始化时调用一次，确保启动时配置已生效。
    /// </summary>
    private void SyncOverlayWindowsFromSettings()
    {
        var s = _configService.Settings;

        // 任务栏窗口
        if (s.ShowInTaskbar && _taskbarWindow == null)
            InitializeTaskbarWindow();
        else if (!s.ShowInTaskbar && _taskbarWindow != null)
            DisposeTaskbarWindow();

        // 托盘悬浮窗
        if (s.ShowTrayTooltip && _trayTooltipWindow == null)
            InitializeTrayTooltip();
        else if (!s.ShowTrayTooltip && _trayTooltipWindow != null)
            DisposeTrayTooltip();
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

        var settingsWindow = new Views.SettingsWindow(_viewModel, _configService);
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
        // FileLogger.Stop() 自洽：内部已完成队列排空 + 兜底，无需再单独 Flush，也消除了 Stop/Flush 顺序依赖。
        FileLogger.Stop();
        base.OnExit(e);
    }
}
