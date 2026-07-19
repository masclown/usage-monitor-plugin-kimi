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
    /// <summary>
    /// Win32 API：释放由 Bitmap.GetHicon() 分配的 GDI icon 句柄。
    /// req-068 F-07：修复 LoadTrayIconFromLogo 中 hIcon 泄漏问题。
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

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
    // req-053：托盘 tooltip 节流——Windows NotifyIcon.Text 频繁更新会被系统忽略，限制每秒最多更新一次
    private DateTime _lastTrayTooltipUpdateUtc = DateTime.MinValue;

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
        // req-009：加载热力图色阶（按 ProviderId 独立色阶表），让首次渲染就拿到正确颜色。
        UsageMonitor.App.Helpers.HeatMapTierScale.ApplyConfig(_configService.Settings.ProviderHeatMapTiers);
        // req-065 B4：BrowserLoginService 已去静态化，不再需要 RegisterConfigService。
        // 登录时由 PluginConfigWindow 创建 BrowserLoginService 实例并传入 ConfigService。

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

        // req-066 A8：从插件装配 HeatMapTiers 默认色阶（AppSettings 不再硬编码 minimax 默认值）
        foreach (var plugin in _pluginManager.Plugins)
        {
            var pid = plugin.Provider.ProviderId;
            if (plugin.Provider.HeatMapTiers != null && plugin.Provider.HeatMapTiers.Count > 0
                && !_configService.Settings.ProviderHeatMapTiers.ContainsKey(pid))
            {
                _configService.Settings.ProviderHeatMapTiers[pid] =
                    new List<HeatMapTierConfig>(plugin.Provider.HeatMapTiers);
            }
        }

        // 创建用量历史持久化仓库（SQLite，%AppData%/UsageMonitor/history.db）
        _historyRepository = UsageHistoryRepository.CreateDefault();
        _historyRepository.EnsureSchema();

        // req-021：启动时清理历史 token=0 错误数据（MiniMax Only）。仅在首次或距上次清理 >30 天时执行。
        // OnStartup 是 void，清理是 I/O 操作 → 用 fire-and-forget 异步启动，不阻塞 UI 启动流程。
        try
        {
            var lastCleaned = _configService.Settings.LastCleanedZeroTokensAt;
            var shouldClean = lastCleaned == null || (DateTime.Now - lastCleaned.Value).TotalDays > 30;
            if (shouldClean)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var deleted = await _historyRepository.CleanHistoricalZeroTokenDataAsync();
                        if (deleted > 0)
                        {
                            await _historyRepository.RecomputeDailyAggregatesAsync();
                        }
                        Dispatcher.Invoke(() =>
                        {
                            _configService.Settings.LastCleanedZeroTokensAt = DateTime.Now;
                            _configService.Save();
                        });
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Error("App", "req-021 historical token=0 cleanup inner failed", ex);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("App", "req-021 historical token=0 cleanup scheduling failed", ex);
        }

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

        _refreshService = new RefreshService(_pluginManager, _configService, _historyStore, _historyRepository);

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

        // req-012：启动时检测新卸载的 Provider → 弹批量对话框（一次选"删/保"，记录到 UninstalledProviderChoices）。
        // 必须在 SyncOverlayWindowsFromSettings 之后（_historyRepository 已就绪）、ShowMainWindow 之前（避免窗口闪烁）。
        CheckUninstalledPluginsOnStartup();

        // 显示主窗口
        ShowMainWindow();
    }

    /// <summary>
    /// req-012：启动时检测本次新卸载的 Provider，与历史已卸载 Provider 区分（看 UninstalledProviderChoices 字典）。
    /// <para>
    /// 流程：
    /// <list type="number">
    ///   <item><description>对比当前已安装 plugin ID 集合与 <c>LastKnownInstalledPluginIds</c>，差集 = 新卸载</description></item>
    ///   <item><description>剔除 <c>UninstalledProviderChoices</c> 中已记录过选择的（不重复询问）</description></item>
    ///   <item><description>弹 MessageBox.YesNo，询问是否删除这些 Provider 的历史数据</description></item>
    ///   <item><description>用户选择后写入 <c>UninstalledProviderChoices</c>；选"是"则调 <c>DeleteProviderDataAsync</c> 清库</description></item>
    ///   <item><description>更新 <c>LastKnownInstalledPluginIds</c> 并 Save</description></item>
    /// </list>
    /// 首次启动时 <c>LastKnownInstalledPluginIds</c> 为空 list → 不会误弹对话框（removedIds 为空）。
    /// </para>
    /// </summary>
    private void CheckUninstalledPluginsOnStartup()
    {
        try
        {
            var currentIds = _pluginManager.Plugins
                .Select(p => p.Provider.ProviderId)
                .ToList();
            var lastKnown = _configService.Settings.LastKnownInstalledPluginIds ?? new List<string>();
            var removedIds = lastKnown
                .Except(currentIds, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            // 剔除已经记录过选择的（决策 5：一次性问，不重复）
            var notAsked = removedIds
                .Where(id => !_configService.Settings.UninstalledProviderChoices.ContainsKey(id))
                .ToList();

            if (notAsked.Count == 0)
            {
                // 即便无需弹窗，也要更新 LastKnownInstalledPluginIds 反映本次已知列表（让下次启动能对比）
                _configService.Settings.LastKnownInstalledPluginIds = currentIds;
                _configService.Save();
                return;
            }

            var msg = "检测到以下插件已被卸载：\n  - "
                + string.Join("\n  - ", notAsked)
                + "\n\n是否删除它们的历史数据？\n（选\u201c否\u201d则保留数据，下次启动不再询问）";
            var result = MessageBox.Show(msg, "插件数据清理",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            var choice = result == MessageBoxResult.Yes ? "deleted" : "kept";
            foreach (var id in notAsked)
            {
                _configService.Settings.UninstalledProviderChoices[id] = choice;
                if (choice == "deleted" && _historyRepository != null)
                {
                    // 同步删除历史数据（fire-and-forget：DeleteProviderDataAsync 内部 try/catch + 日志）
                    _ = _historyRepository.DeleteProviderDataAsync(id);
                }
            }

            // 更新 LastKnownInstalledPluginIds 反映本次已知列表，并落盘
            _configService.Settings.LastKnownInstalledPluginIds = currentIds;
            _configService.Save();
        }
        catch (Exception ex)
        {
            FileLogger.Error("App", "CheckUninstalledPluginsOnStartup failed", ex);
        }
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
            Icon = LoadTrayIconFromLogo(), // req-032：单 logo 统一
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        // req-028：托盘 tooltip 不能依赖 NotifyIcon.Text 改变事件（Win32 NotifyIcon 不提供该事件）。
        // 退而求其次：鼠标移动到托盘图标上时（同 req-004 的 IsCursorInTrayArea 区域）
        // 立即重新计算 5h 倒计时并刷新 NotifyIcon.Text，避免等下一次 RefreshAll 周期才更新。
        _notifyIcon.MouseMove += (_, _) =>
        {
            if (_notifyIcon == null) return;
            try
            {
                var cursorPos = System.Windows.Forms.Cursor.Position;
                if (!IsCursorInTrayArea(cursorPos)) return;
                var counterVm = _viewModel.Usages?.FirstOrDefault(vm => vm.HasFiveHourCountdown);
                // req-053：直接读取已计算的 FiveHourCountdownText，不再重新调用 RefreshFiveHourCountdownText
                var countdown = counterVm?.FiveHourCountdownText ?? "00:00:00";
                // 托盘 tooltip Text 最大约 63 char（Windows 限制），拼接 "Provider/用量/倒计时" 形式。
                if (counterVm != null)
                    _notifyIcon.Text = $"{counterVm.DisplayName} {counterVm.UsagePercentage:0}% · 5h:{countdown}";
                else
                    _notifyIcon.Text = "UsageMonitor";
            }
            catch (Exception ex)
            {
                FileLogger.Error("App", $"MouseMove tooltip refresh failed: {ex.Message}", ex);
            }
        };

        // req-016：订阅主题变化，刷新托盘图标（双主题适配）
        Helpers.ThemeManager.ThemeChanged += (_, _) =>
        {
            if (_notifyIcon != null)
            {
                try { _notifyIcon.Icon = LoadTrayIconFromLogo(); }
                catch (Exception ex) { FileLogger.Error("App", $"Refresh tray icon failed: {ex.Message}", ex); }
            }
        };

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
    /// req-016：从项目 Logo PNG 构造托盘 <see cref="System.Drawing.Icon"/>。
    /// <para>
    /// PNG 不是 Windows 原生托盘格式（NotifyIcon 期望 .ico），所以走 Bitmap.GetHicon + Icon.FromHandle 路径。
    /// 该 Icon 仅作过渡使用；高分屏可能略糊。后续如需清晰图标，准备多尺寸 .ico 后替换本方法。
    /// </para>
    /// </summary>
    private static System.Drawing.Icon LoadTrayIconFromLogo()
    {
        try
        {
            var path = Helpers.LogoProvider.GetLogoPath();
            using var bmp = new System.Drawing.Bitmap(path);
            var hIcon = bmp.GetHicon();
            try
            {
                // Icon.FromHandle 不接管 hicon 释放 → Clone 出独立 Icon
                var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                return icon;
            }
            finally
            {
                // req-068 F-07：显式释放原始 GDI handle，避免每次主题切换泄漏一个句柄
                DestroyIcon(hIcon);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("App", $"LoadTrayIconFromLogo() failed: {ex.Message}", ex);
            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// 初始化托盘悬浮窗 + 鼠标悬停检测定时器。
    /// 幂等：已存在则跳过；开关关闭时不创建。
    /// </summary>
    /// <summary>
    /// req-053：刷新托盘 tooltip 文本，确保倒计时实时更新。
    /// <para>直接读取 VM 已计算好的 FiveHourCountdownText（由全局 timer 每秒更新），
    /// 不再重新调用 RefreshFiveHourCountdownText 避免 _next5hResetAt 为 null 时覆盖为 "00:00:00"。</para>
    /// </summary>
    private void RefreshTrayTooltipText()
    {
        if (_notifyIcon == null) return;
        // 节流：每秒最多更新一次
        var now = DateTime.UtcNow;
        if ((now - _lastTrayTooltipUpdateUtc).TotalMilliseconds < 1000) return;
        _lastTrayTooltipUpdateUtc = now;

        try
        {
            var counterVm = _viewModel.Usages?.FirstOrDefault(vm => vm.HasFiveHourCountdown);
            // req-053：直接读取已计算的 FiveHourCountdownText（由 OnFiveHourCountdownTick 每秒更新）
            var countdown = counterVm?.FiveHourCountdownText ?? "00:00:00";
            if (counterVm != null)
                _notifyIcon.Text = $"{counterVm.DisplayName} {counterVm.UsagePercentage:0}% 5h:{countdown}";
            else
                _notifyIcon.Text = "UsageMonitor";
        }
        catch (Exception ex)
        {
            FileLogger.Error("App", $"RefreshTrayTooltipText failed: {ex.Message}", ex);
        }
    }

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
            // req-053：鼠标在托盘区域时，每秒刷新托盘 tooltip 的倒计时文本
            RefreshTrayTooltipText();
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
                // req-028：托盘 tooltip 加上 5h 倒计时。格式：用量 X% · 5h 倒计时 HH:mm:ss
                // 多 Provider 场景只取第一个有 5h 字段的 VM（MiniMax）。
                var lines = e.Usages
                    .Where(u => u.IsSuccess)
                    .Select(u => u.GetShortDisplayText())
                    .ToList();
                var counterVm = _viewModel.Usages?.FirstOrDefault(vm => vm.HasFiveHourCountdown);
                if (counterVm != null)
                {
                    lines.Add($"5h 倒计时 {counterVm.FiveHourCountdownText}");
                }
                var tooltip = string.Join("\n", lines);
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
        _taskbarWindow = new Views.TaskbarWindow(_viewModel, _configService, _taskbarHelper, _refreshService);
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
        // req-022：全局默认或每 Provider 覆盖变化时，刷新所有 Provider 的 DisplayMode（resolver 重算）+ 重排 taskbar 尺寸。
        _viewModel.TaskbarModeChanged += (_, _) =>
        {
            foreach (var usage in _viewModel.Usages)
            {
                var resolved = UsageMonitor.App.Helpers.TaskbarModeResolver.Resolve(
                    _configService.Settings, usage.ProviderId);
                if (usage.DisplayMode != resolved) usage.DisplayMode = resolved;
            }
            _taskbarWindow?.RecalculateSize();
        };
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
                // req-012：传入 _configService，让 HistoryViewModel 能按 PluginEnabled 过滤 Provider 列表
                //        并订阅 ConfigChanged 实现"设置页取消勾选 → 历史窗口立即移除"的实时联动。
                var vm = new ViewModels.HistoryViewModel(_configService, _historyRepository, installed);
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
        // req-028：停止 5h 倒计时 timer（避免 DispatcherTimer 持续持有 root 引用）
        _viewModel?.StopFiveHourCountdownTimer();
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
