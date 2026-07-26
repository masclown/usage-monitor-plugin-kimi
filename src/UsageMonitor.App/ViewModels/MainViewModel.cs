using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.App.Controls;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.Services;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 主窗口ViewModel - 管理用量数据显示和交互逻辑
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;
    // req-069 F-10：刷新服务依赖接口而非具体类（可注入 mock IRefreshService）。
    private readonly IRefreshService _refreshService;
    // req-099 B2：数据访问统一走 IDataModule（数据刷新保存模块）。
    private readonly UsageMonitor.Core.Modules.IDataModule _dataModule;
    // req-099 B1：显示模块——拥有卡片集合并封装装配 / 渲染路由 / 启用过滤 / 图表顺序逻辑（激进抽离）。
    private readonly UsageMonitor.App.Services.Display.DisplayModule _displayModule;
    private UsageMonitor.App.App? _hostAppRef;

    /// <summary>
    /// req-091-005：MainViewModel 暴露 App 引用，用于调用 <c>App.TriggerReLogin</c>。
    /// 在 <c>App.OnStartup</c> 创建 MainViewModel 后注入。
    /// </summary>
    public UsageMonitor.App.App? HostApp
    {
        get => _hostAppRef;
        set => _hostAppRef = value;
    }

    // req-028：每 1s 触发一次的全局 DispatcherTimer，用来刷新各 Provider 卡的重置倒计时 + 到时自动刷新（Provider 无关，能力标志驱动）。
    // 单例复用；启动时由 MainViewModel 构造函数 Start，资源销毁由 App.xaml.cs OnExit 调用 Stop。
    private System.Windows.Threading.DispatcherTimer? _resetCountdownTimer;
    // req-028："上一次自动刷新触发"时间，防止系统时间回退 / 重复 tick 造成连续多次触发 RefreshProviderAsync。
    private DateTime _lastAutoRefreshUtc = DateTime.MinValue;

    /// <summary>供主窗口和设置窗口复用的全局配置服务。</summary>
    public ConfigService ConfigService => _configService;

    // req-099 B1：卡片集合的实际拥有者已抽离到 DisplayModule。以下三个属性委托返回模块内的同一集合实例，
    // 既保留主窗口 / 设置窗口 / 任务栏既有 {Binding Usages/EnabledUsages/PluginItems} 不变，
    // 又让装配 / 渲染 / 过滤逻辑集中在 DisplayModule 管理（激进抽离，MainViewModel 不再直接持有集合）。
    /// <summary>各服务商的用量显示列表（全量，包含被禁用的项，用于切换时保留状态）</summary>
    public ObservableCollection<ProviderUsageViewModel> Usages => _displayModule.Usages;

    /// <summary>仅展示已启用插件的用量卡片（主窗口 ItemsControl 实际绑定此集合）</summary>
    public ObservableCollection<ProviderUsageViewModel> EnabledUsages => _displayModule.EnabledUsages;

    /// <summary>插件列表</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems => _displayModule.PluginItems;

    /// <summary>
    /// 分发准备：刷新所有卡片的 Provider 图标（委托 DisplayModule）。
    /// <para>供 App 在启动 favicon 预取完成后回调，使运行时抓取的图标即时显示。</para>
    /// </summary>
    public void RefreshProviderIcons() => _displayModule.RefreshIcons();

    /// <summary>
    /// req-016：当前主题对应的项目 Logo（用于 MainWindow.Icon 绑定）。
    /// <para>
    /// 订阅 <see cref="UsageMonitor.App.Helpers.ThemeManager.ThemeChanged"/> 事件实现实时刷新。
    /// </para>
    /// </summary>
    public System.Windows.Media.ImageSource? CurrentLogoSource
    {
        get => _currentLogoSource;
        private set { _currentLogoSource = value; OnPropertyChanged(); }
    }
    private System.Windows.Media.ImageSource? _currentLogoSource;

    /// <summary>
    /// 设置页“用量色阶” Tab 的编辑上下文（延迟初始化，首次访问时创建）。
    /// 供 SettingsWindow 的 DataContext 拿。
    /// </summary>
    public TierListEditorViewModel TierEditor
    {
        get
        {
            _tierEditor ??= new TierListEditorViewModel(this);
            return _tierEditor;
        }
    }
    private TierListEditorViewModel? _tierEditor;

    /// <summary>
    /// S2：卡片管理页 ViewModel（延迟初始化，首次访问时创建）。
    /// <para>三级折叠结构：账号 → 图表 → 数据组 + tooltip 字段多选。</para>
    /// </summary>
    public CardManageViewModel CardManage
    {
        get
        {
            _cardManage ??= new CardManageViewModel(_pluginManager, _configService);
            return _cardManage;
        }
    }
    private CardManageViewModel? _cardManage;

    /// <summary>
    /// S4：任务栏迷你图表页 ViewModel（延迟初始化，首次访问时创建）。
    /// <para>三级折叠结构：账号 → mini 图表 → 数据组，与卡片管理页同构。</para>
    /// </summary>
    public MiniChartManageViewModel MiniChartManage
    {
        get
        {
            _miniChartManage ??= new MiniChartManageViewModel(_pluginManager, _configService);
            return _miniChartManage;
        }
    }
    private MiniChartManageViewModel? _miniChartManage;

    /// <summary>
    /// req-011：设置页“热力图色阶” Tab 的编辑上下文（延迟初始化，首次访问时创建）。
    /// </summary>
    public HeatMapTierListEditorViewModel HeatMapTierEditor
    {
        get
        {
            _heatMapTierEditor ??= new HeatMapTierListEditorViewModel(this);
            return _heatMapTierEditor;
        }
    }
    private HeatMapTierListEditorViewModel? _heatMapTierEditor;

    /// <summary>刷新间隔（秒）</summary>
    public int RefreshInterval
    {
        get => _configService.Settings.RefreshIntervalSeconds;
        set
        {
            _configService.Settings.RefreshIntervalSeconds = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// req-072 U-18：上次刷新时间（HH:mm:ss 格式），用于底部状态栏显示。
    /// </summary>
    public string LastRefreshTime
    {
        get => _lastRefreshTime;
        private set { _lastRefreshTime = value; OnPropertyChanged(); }
    }
    private string _lastRefreshTime = "--:--:--";

    /// <summary>
    /// req-072 U-18：刷新进度（0-100），用于底部状态栏显示。
    /// </summary>
    public int RefreshProgress
    {
        get => _refreshProgress;
        private set { _refreshProgress = value; OnPropertyChanged(); }
    }
    private int _refreshProgress;

    /// <summary>
    /// req-072 U-18：错误计数，用于底部状态栏显示。
    /// </summary>
    public int ErrorCount
    {
        get => _errorCount;
        private set { _errorCount = value; OnPropertyChanged(); }
    }
    private int _errorCount;

    /// <summary>
    /// req-072 U-19：空状态判断（EnabledUsages 是否为空），用于主窗口空状态显示。
    /// </summary>
    public bool IsEmpty => !EnabledUsages.Any();

    /// <summary>
    /// req-110 P1-5：空态分层引导文案——按"无插件 → 插件未启用 → 无账号 → 账号未启用"逐层定位，
    /// 引导用户完成"安装插件 → 创建账号 → 配置凭据"的账号为中心接入流程；随 IsEmpty 一起在卡片集合变化时刷新。
    /// </summary>
    public string EmptyStateHint
    {
        get
        {
            try
            {
                var plugins = _pluginManager.Plugins;
                if (plugins.Count == 0)
                    return "未发现任何插件，请将插件声明包放入 plugins 目录后重启程序";
                var enabledPlugins = plugins.Where(p => p.IsEnabled).ToList();
                if (enabledPlugins.Count == 0)
                    return "插件已安装但未启用，请在设置 → 插件管理中启用插件";
                var hasAccount = enabledPlugins.Any(p => _configService.GetAccounts(p.Provider.ProviderId).Count > 0);
                if (!hasAccount)
                    return "插件已启用但还没有账号，请在设置 → 插件管理中为插件添加账号并配置登录态 / API Key";
                return "账号已创建但未启用，请在设置 → 插件管理中启用账号";
            }
            catch
            {
                return "请在设置 → 插件管理中完成插件与账号配置";
            }
        }
    }

    /// <summary>是否启用任务栏显示</summary>
    public bool ShowInTaskbar
    {
        get => _configService.Settings.ShowInTaskbar;
        set
        {
            _configService.Settings.ShowInTaskbar = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>是否开机自启</summary>
    public bool AutoStart
    {
        get => _configService.Settings.AutoStart;
        set
        {
            _configService.Settings.AutoStart = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// req-fix-关闭最小化设置：关闭主窗口时是否最小化到托盘（默认 true）。
    /// <para>勾选 → 关闭主窗口静默隐藏到托盘；取消勾选 → 关闭主窗口直接完全退出程序。
    /// MainWindow.OnClosing 读取此配置决定关闭行为，不再弹提示窗。</para>
    /// </summary>
    public bool MinimizeToTray
    {
        get => _configService.Settings.MinimizeToTray;
        set
        {
            if (_configService.Settings.MinimizeToTray == value) return;
            _configService.Settings.MinimizeToTray = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>是否启用托盘悬浮窗</summary>
    public bool ShowTrayTooltip
    {
        get => _configService.Settings.ShowTrayTooltip;
        set
        {
            _configService.Settings.ShowTrayTooltip = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// req-022：任务栏显示的全局默认模式（当某 Provider 没有单独覆盖时使用）。
    /// <para>
    /// 设置修改后立即持久化到 config.json，并触发 ConfigChanged 让 TaskbarWindow 实时刷新。
    /// </para>
    /// </summary>
    public TaskbarDisplayMode GlobalTaskbarMode
    {
        get => _configService.Settings.GlobalTaskbarMode;
        set
        {
            if (_configService.Settings.GlobalTaskbarMode == value) return;
            _configService.Settings.GlobalTaskbarMode = value;
            _configService.Save();
            OnPropertyChanged();
            // req-022：通知 TaskbarWindow 重新计算每 Provider 的 effective mode
            RaiseTaskbarModeChanged();
        }
    }

    /// <summary>
    /// req-022：任务栏显示模式变更事件。订阅方（如 TaskbarWindow）收到后重新计算尺寸并刷新显示。
    /// </summary>
    public event EventHandler? TaskbarModeChanged;

    /// <summary>触发 <see cref="TaskbarModeChanged"/> 事件。</summary>
    private void RaiseTaskbarModeChanged()
    {
        try { TaskbarModeChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Error("MainViewModel",
                $"RaiseTaskbarModeChanged threw: {ex.Message}", ex);
        }
    }

    /// <summary>全局任务栏模式下拉框的友好文本 + key 映射（用于 ComboBox 绑定）</summary>
    public IReadOnlyList<KeyValuePair<TaskbarDisplayMode, string>> GlobalTaskbarModeOptions { get; } = new[]
    {
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.Text, "文字"),
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.MiniLineChart, "折线图"),
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.RingChart, "圆环图"),
    };

    /// <summary>托盘悬浮窗关闭延迟（毫秒）。req-095：范围 100-5000，超出自动钳制。</summary>
    public int TrayTooltipHideDelayMs
    {
        get => _configService.Settings.TrayTooltipHideDelayMs;
        set
        {
            // req-095：硬编码 100ms（避免闪烁）~ 5000ms（5 秒）。
            var v = Math.Clamp(value, 100, 5000);
            _configService.Settings.TrayTooltipHideDelayMs = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>托盘悬浮窗触发区域宽度（像素，屏幕右下角向左延伸）。下限 20 避免设为 0 后无法触发。</summary>
    public int TrayTriggerWidth
    {
        get => _configService.Settings.TrayTriggerWidth;
        set
        {
            var v = Math.Max(20, value);
            _configService.Settings.TrayTriggerWidth = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>托盘悬浮窗触发区域高度（像素，工作区底部向下延伸）。下限 10 避免设为 0 后无法触发。</summary>
    public int TrayTriggerHeight
    {
        get => _configService.Settings.TrayTriggerHeight;
        set
        {
            var v = Math.Max(10, value);
            _configService.Settings.TrayTriggerHeight = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>历史数据保留点数</summary>
    public int HistoryPointCount
    {
        get => _configService.Settings.HistoryPointCount;
        set
        {
            var v = value <= 0 ? 60 : value;
            _configService.Settings.HistoryPointCount = v;
            _dataModule.MaxPoints = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>圆环图警告阈值（百分比）</summary>
    public int RingChartWarningThreshold
    {
        get => _configService.Settings.RingChartWarningThreshold;
        set
        {
            _configService.Settings.RingChartWarningThreshold = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>圆环图危险阈值（百分比）</summary>
    public int RingChartDangerThreshold
    {
        get => _configService.Settings.RingChartDangerThreshold;
        set
        {
            _configService.Settings.RingChartDangerThreshold = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>外观主题（深色 / 浅色）。设置时写入配置、保存并即时应用换肤。</summary>
    public ThemeMode ThemeMode
    {
        get => _configService.Settings.Theme;
        set
        {
            if (_configService.Settings.Theme == value) return;
            _configService.Settings.Theme = value;
            // req-115：旧深/浅入口显式切换时清空主题包选择，回到内置主题语义
            _configService.Settings.ThemeId = "";
            _configService.Save();
            UsageMonitor.App.Helpers.ThemeManager.Apply(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(SelectedThemeId));
        }
    }

    /// <summary>是否深色主题（供设置窗口分段开关的 RadioButton 双向绑定）。</summary>
    public bool IsDarkTheme
    {
        get => _configService.Settings.Theme == ThemeMode.Dark;
        set { if (value) ThemeMode = ThemeMode.Dark; }
    }

    /// <summary>是否浅色主题。</summary>
    public bool IsLightTheme
    {
        get => _configService.Settings.Theme == ThemeMode.Light;
        set { if (value) ThemeMode = ThemeMode.Light; }
    }

    /// <summary>刷新命令</summary>
    /// <summary>
        /// 主窗口顶部"刷新"按钮绑定的命令（刷新所有已启用 Provider）。
        /// 使用 AsyncRelayCommand：执行期间自动提供 <c>IsRunning</c> 属性，供 req-025 旋转动画触发。
        /// </summary>
        public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>保存设置命令</summary>
    public IRelayCommand SaveSettingsCommand { get; }

    // =====================================================================
    // req-073 设置窗口导航重构：左侧导航 + 全局保存栏
    // =====================================================================

    /// <summary>
    /// req-073：设置窗口当前选中的导航分区。默认 <see cref="Helpers.SettingsSection.General"/>。
    /// <para>由设置窗口左侧导航 ListBox 双向绑定；切换时 ContentControl 通过
    /// <see cref="Helpers.SettingsSectionSelector"/> 选择对应 DataTemplate。</para>
    /// </summary>
    public Helpers.SettingsSection CurrentSection
    {
        get => _currentSection;
        set
        {
            if (_currentSection == value) return;
            _currentSection = value;
            OnPropertyChanged();
            // Phase 2 修复：切换到卡片管理 / 迷你图表页时刷新已构造实例（账号增删后管理页不再陈旧）。
            // 仅对已构造实例调用 Reload()，不触发懒构造。
            if (value == Helpers.SettingsSection.CardManage)
                _cardManage?.Reload();
            else if (value == Helpers.SettingsSection.TaskbarMiniChart)
                _miniChartManage?.Reload();
        }
    }
    private Helpers.SettingsSection _currentSection = Helpers.SettingsSection.General;

    /// <summary>
    /// req-073：设置窗口左侧导航项列表（分组标题 + 可点击项混合）。
    /// <para>S3 重构：已移除「任务栏显示 / 卡片排序 / 图表顺序 / 多进度条 / 卡片图表与数据组」5 个旧分区导航项。</para>
    /// </summary>
    public IReadOnlyList<Helpers.SettingsNavigationItem> SettingsNavigationItems { get; } = new List<Helpers.SettingsNavigationItem>
    {
        // ===== 通用 =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("通用"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.General, "常规设置", "通用"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Plugins, "插件管理", "通用"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.CardManage, "卡片管理", "通用"),

        // ===== 显示 =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("显示"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.TaskbarMiniChart, "任务栏迷你图表", "显示"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Tray, "悬浮窗", "显示"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.ColorTier, "色阶", "显示"),

        // ===== 高级 =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("高级"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Security, "安全", "高级"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Diagnostics, "诊断日志", "高级"),
    };

    /// <summary>
    /// req-073：设置窗口底部「有未保存修改」提示的可见性标记。
    /// <para>
    /// 当前实现为轻量方案：设置窗口打开期间任何属性 setter 触发 Save 后该标记即清除；
    /// 由于现有各设置项 setter 均已即时持久化（调用 <c>_configService.Save()</c>），
    /// 该属性主要为后续「延迟保存」语义预留——当某分区改为「编辑后不立即写盘」时，
    /// 由对应编辑器 ViewModel 将其置 true，底部保存栏即显示提示。
    /// </para>
    /// </summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            if (_hasUnsavedChanges == value) return;
            _hasUnsavedChanges = value;
            OnPropertyChanged();
        }
    }
    private bool _hasUnsavedChanges;

    /// <summary>
    /// req-073：设置窗口底部「保存」按钮命令——调用 <see cref="ConfigService.Save"/> 持久化全部配置，
    /// 并清除 <see cref="HasUnsavedChanges"/> 标记。保存结果（成功/失败）由设置窗口 code-behind 读取
    /// <see cref="ConfigService.LastSaveError"/> 后决定提示与是否关闭窗口。
    /// </summary>
    public IRelayCommand SaveAllSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// req-073：设置窗口底部「取消」按钮命令——仅关闭窗口，不写盘。
    /// 实际关闭动作由设置窗口 code-behind 订阅 <see cref="RequestCloseSettings"/> 事件执行。
    /// </summary>
    public IRelayCommand CancelSettingsCommand { get; private set; } = null!;

    /// <summary>
    /// req-073：请求关闭设置窗口事件。SaveAllSettingsCommand / CancelSettingsCommand 触发，
    /// 由 SettingsWindow code-behind 订阅并执行 <see cref="System.Windows.Window.Close"/>。
    /// <para>事件参数为 <c>bool</c>：<c>true</c> 表示「保存后关闭」，<c>false</c> 表示「取消关闭」。</para>
    /// </summary>
    public event EventHandler<bool>? RequestCloseSettings;

    /// <summary>req-073：触发 <see cref="RequestCloseSettings"/> 事件。</summary>
    private void RaiseRequestCloseSettings(bool saved)
    {
        try { RequestCloseSettings?.Invoke(this, saved); }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Error("MainViewModel",
                $"RaiseRequestCloseSettings threw: {ex.Message}", ex);
        }
    }


    
    /// <summary>
    /// req-109：返回指定 Provider 的有效可见 Mini 图表 ID 列表（供 TaskbarWindow 渲染端过滤）。
    /// <para>null = 未配置（全部可见，向后兼容）；空集合 = 用户关闭了该 Provider 的全部 Mini 图表。</para>
    /// <para>Phase 2 修复：增加 accountId/cardId 可选参数，避免任务栏读取硬编码 "default" 导致有账号用户的配置静默失效。</para>
    /// </summary>
    public List<string>? GetEffectiveVisibleMiniCharts(string providerId, string accountId = "default", string cardId = "default-card")
    {
        if (string.IsNullOrEmpty(providerId)) return null;
        var eff = _configService.GetEffectiveAccountCustomization(providerId, accountId, cardId);
        return eff.VisibleMiniCharts;
    }

    /// <summary>
    /// req-105：返回指定图表的有效 Tooltip 显示字段（供卡片 Tooltip 渲染端消费）。
    /// <para>优先用户配置的 <c>VisibleTooltipFields[chartId]</c>（非 null 时）；否则回退 defaults.json 声明的 <c>chart.Tooltip.Fields</c>。</para>
    /// </summary>
    public IReadOnlyList<string> GetEffectiveTooltipFields(string providerId, string chartId)
    {
        if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(chartId)) return Array.Empty<string>();
        var eff = _configService.GetEffectiveAccountCustomization(providerId, "default", "default-card");
        if (eff.VisibleTooltipFields != null && eff.VisibleTooltipFields.TryGetValue(chartId, out var userFields) && userFields != null)
            return userFields;
        // 回退：defaults.json 声明的 chart.Tooltip.Fields
        var plugin = _pluginManager.Plugins.FirstOrDefault(p => string.Equals(p.Provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        var chart = plugin?.Provider.Card?.Charts.FirstOrDefault(c => c.ChartId == chartId);
        return chart?.Tooltip?.Fields ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    // =====================================================================
    // REQ-003 环形图增强设置
    // =====================================================================

    /// <summary>REQ-003：环形图中心数字切换顺序（绑定到 SettingsWindow 的 ListBox）。</summary>
    public ObservableCollection<string> RingChartMetricOrder { get; } = new();

    /// <summary>REQ-003：sticky 秒数（鼠标离开后回默认）。</summary>
    public double RingChartStickySeconds
    {
        get => _configService.Settings.RingChartStickySeconds;
        set
        {
            var v = Math.Max(0, value);
            if (Math.Abs(_configService.Settings.RingChartStickySeconds - v) < 0.001) return;
            _configService.Settings.RingChartStickySeconds = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>REQ-003：切换动画毫秒数。0 = 禁用。</summary>
    public int RingChartSwitchAnimationMs
    {
        get => _configService.Settings.RingChartSwitchAnimationMs;
        set
        {
            var v = Math.Max(0, value);
            if (_configService.Settings.RingChartSwitchAnimationMs == v) return;
            _configService.Settings.RingChartSwitchAnimationMs = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>REQ-003：把 metric 顺序中选中项上移一位。</summary>
    public IRelayCommand MoveRingMetricUpCommand { get; private set; } = null!;

    /// <summary>REQ-003：把 metric 顺序中选中项下移一位。</summary>
    public IRelayCommand MoveRingMetricDownCommand { get; private set; } = null!;

    /// <summary>REQ-003：恢复默认 metric 顺序（覆盖设置 + 同步 ListBox + 落盘）。</summary>
    public IRelayCommand ResetRingMetricOrderCommand { get; private set; } = null!;

    // =====================================================================
    // REQ-004 触发区域 RectInt
    // =====================================================================

    /// <summary>REQ-004：触发区域 X（屏幕坐标）。</summary>
    public int TriggerRectX
    {
        get => _configService.Settings.TrayTooltipTriggerRect.X;
        set
        {
            var r = _configService.Settings.TrayTooltipTriggerRect.With(x: value);
            r = ClampRect(r);
            if (r == _configService.Settings.TrayTooltipTriggerRect) return;
            _configService.Settings.TrayTooltipTriggerRect = r;
            _configService.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TriggerRectY));
            OnPropertyChanged(nameof(TriggerRectWidth));
            OnPropertyChanged(nameof(TriggerRectHeight));
        }
    }

    /// <summary>REQ-004：触发区域 Y（屏幕坐标）。</summary>
    public int TriggerRectY
    {
        get => _configService.Settings.TrayTooltipTriggerRect.Y;
        set
        {
            var r = _configService.Settings.TrayTooltipTriggerRect.With(y: value);
            r = ClampRect(r);
            if (r == _configService.Settings.TrayTooltipTriggerRect) return;
            _configService.Settings.TrayTooltipTriggerRect = r;
            _configService.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TriggerRectX));
            OnPropertyChanged(nameof(TriggerRectWidth));
            OnPropertyChanged(nameof(TriggerRectHeight));
        }
    }

    /// <summary>REQ-006：触发区域宽度（≥10）。</summary>
    public int TriggerRectWidth
    {
        get => _configService.Settings.TrayTooltipTriggerRect.Width;
        set
        {
            var r = _configService.Settings.TrayTooltipTriggerRect.With(width: Math.Max(10, value));
            r = ClampRect(r);
            if (r == _configService.Settings.TrayTooltipTriggerRect) return;
            _configService.Settings.TrayTooltipTriggerRect = r;
            _configService.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TriggerRectX));
            OnPropertyChanged(nameof(TriggerRectY));
            OnPropertyChanged(nameof(TriggerRectHeight));
        }
    }

    /// <summary>REQ-006：触发区域高度（≥10）。</summary>
    public int TriggerRectHeight
    {
        get => _configService.Settings.TrayTooltipTriggerRect.Height;
        set
        {
            var r = _configService.Settings.TrayTooltipTriggerRect.With(height: Math.Max(10, value));
            r = ClampRect(r);
            if (r == _configService.Settings.TrayTooltipTriggerRect) return;
            _configService.Settings.TrayTooltipTriggerRect = r;
            _configService.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TriggerRectX));
            OnPropertyChanged(nameof(TriggerRectY));
            OnPropertyChanged(nameof(TriggerRectWidth));
        }
    }

    /// <summary>REQ-004：进入"在屏幕上调整"蒙版模式（调用方由 App.xaml.cs 注入）。</summary>
    public IRelayCommand EditTriggerAreaCommand { get; private set; } = null!;

    /// <summary>REQ-004：恢复默认触发区域（右下方 240×120）。</summary>
    public IRelayCommand ResetTriggerAreaCommand { get; }

    /// <summary>REQ-006：使用完整 VirtualScreen 夹回 RectInt，允许触发区域覆盖任务栏和负坐标副屏。</summary>
    private RectInt ClampRect(RectInt r)
    {
        try
        {
            var left = (int)Math.Round(SystemParameters.VirtualScreenLeft);
            var top = (int)Math.Round(SystemParameters.VirtualScreenTop);
            var right = (int)Math.Round(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth);
            var bottom = (int)Math.Round(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);
            return r.ClampToScreen(left, top, right, bottom);
        }
        catch
        {
            var fallback = System.Windows.Forms.SystemInformation.VirtualScreen;
            return fallback.Width > 0 && fallback.Height > 0
                ? r.ClampToScreen(fallback.Left, fallback.Top, fallback.Right, fallback.Bottom)
                : r.ClampToScreen(0, 0, 1920, 1080);
        }
    }

    /// <summary>REQ-004：使用方注入“在屏幕上调整”蒙版打开回调（在 App.xaml.cs 里设置）。</summary>
    public Action? OpenTriggerOverlayAction { get; set; }

}
