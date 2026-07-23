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

    // req-028：每 1s 触发一次的全局 DispatcherTimer，用来刷新各 Provider 卡的 5h 倒计时 + 到时自动刷新。
    // 单例复用；启动时由 MainViewModel 构造函数 Start，资沅销毁由 App.xaml.cs OnExit 调用 Stop。
    private System.Windows.Threading.DispatcherTimer? _fiveHourCountdownTimer;
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
            _configService.Save();
            UsageMonitor.App.Helpers.ThemeManager.Apply(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
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
        }
    }
    private Helpers.SettingsSection _currentSection = Helpers.SettingsSection.General;

    /// <summary>
    /// req-073：设置窗口左侧导航项列表（分组标题 + 可点击项混合）。
    /// <para>按「通用 / 显示 / 高级」三组分组，为 req-103/104/097 预留导航项（当前注释掉，后续迭代启用）。</para>
    /// </summary>
    public IReadOnlyList<Helpers.SettingsNavigationItem> SettingsNavigationItems { get; } = new List<Helpers.SettingsNavigationItem>
    {
        // ===== 通用 =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("通用"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.General, "常规设置", "通用"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Plugins, "插件管理", "通用"),

        // ===== 显示 =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("显示"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Taskbar, "任务栏显示", "显示"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Tray, "悬浮窗", "显示"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.ColorTier, "色阶", "显示"),

        // ===== 高级 =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("高级"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Security, "安全", "高级"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.Diagnostics, "诊断日志", "高级"),

        // ===== 个性化（req-103/104/097 已启用） =====
        Helpers.SettingsNavigationItem.CreateGroupHeader("个性化"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.CardOrder, "卡片排序", "个性化"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.ChartOrder, "图表顺序", "个性化"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.MultiProgress, "多进度条", "个性化"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.CardCharts, "卡片图表与数据组", "个性化"),
        Helpers.SettingsNavigationItem.CreateItem(Helpers.SettingsSection.TaskbarMiniChart, "任务栏迷你图表", "个性化"),
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

    // =====================================================================
    // REQ-103 卡片排序功能
    // =====================================================================

    /// <summary>
    /// req-103：卡片排序设置页的列表项集合（按当前配置顺序排列）。
    /// </summary>
    public ObservableCollection<Helpers.CardOrderItem> CardOrderItems { get; } = new();

    /// <summary>
    /// req-103：恢复默认卡片顺序命令（按插件加载顺序）。
    /// </summary>
    public IRelayCommand ResetCardOrderCommand { get; private set; } = null!;

    /// <summary>
    /// req-104：保存多进度条字段选择命令。
    /// </summary>
    public IRelayCommand SaveMultiProgressFieldsCommand { get; private set; } = null!;

    /// <summary>
    /// req-103：刷新 CardOrderItems 集合（从 EnabledUsages 同步）。
    /// </summary>
    public void RefreshCardOrderItems()
    {
        CardOrderItems.Clear();
        foreach (var vm in EnabledUsages)
        {
            CardOrderItems.Add(new Helpers.CardOrderItem
            {
                ProviderId = vm.ProviderId,
                DisplayName = vm.DisplayName,
            });
        }
    }

    /// <summary>
    /// req-103：保存当前 CardOrderItems 顺序到配置。
    /// </summary>
    public void SaveCardOrder()
    {
        _configService.Settings.ProviderCardOrder = CardOrderItems.Select(x => x.ProviderId).ToList();
        _configService.Save();
        // 触发 RebuildEnabledUsages 按新顺序重新排列
        RebuildEnabledUsages();
    }

    // =====================================================================
    // REQ-104 多进度条与数字多排显示
    // =====================================================================

    /// <summary>
    /// req-104：多进度条设置页的字段选择项集合（按 Provider 分组）。
    /// </summary>
    public ObservableCollection<Helpers.MultiProgressFieldItem> MultiProgressFieldItems { get; } = new();
    
        // =====================================================================
        // req-107 B6 演进：卡片图表与数据组（增删/排序）
        // =====================================================================
    
        /// <summary>req-107 B6 演进：设置窗口“卡片图表与数据组”分区——有 Card 声明的 Provider 列表（providerId）。</summary>
        public ObservableCollection<string> CardChartConfigProviders { get; } = new();

        /// <summary>req-109：当前选中 Provider 下的账号列表（AccountId）。无 Accounts 配置时仅含 "default"。</summary>
        public ObservableCollection<string> CardChartConfigAccounts { get; } = new();

        /// <summary>当前选中的 Provider（用于卡片图表与数据组分区）。null = 未选择。</summary>
        private string? _selectedCardChartProviderId;
        public string? SelectedCardChartProviderId
        {
            get => _selectedCardChartProviderId;
            set { if (_selectedCardChartProviderId != value) { _selectedCardChartProviderId = value; ReloadCardChartAccounts(); ReloadCardChartConfigItems(); OnPropertyChanged(); } }
        }

        /// <summary>req-109：当前选中的账号（用于卡片图表与数据组分区）。null = 未选择（自动选第一项）。</summary>
        private string? _selectedCardChartConfigAccountId;
        public string? SelectedCardChartConfigAccountId
        {
            get => _selectedCardChartConfigAccountId;
            set { if (_selectedCardChartConfigAccountId != value) { _selectedCardChartConfigAccountId = value; ReloadCardChartConfigItems(); RefreshCardMiniChartItems(); OnPropertyChanged(); } }
        }

        /// <summary>req-109：当前选中的卡片 ID（用于 Mini 图表配置的 3 段 key）。默认 "default-card"。</summary>
        public string SelectedCardChartConfigCardId { get; set; } = "default-card";

        /// <summary>req-109：当前选中 Provider 的图表配置项集合（绑定到 UI 的列表）。</summary>
        public ObservableCollection<CardChartConfigItem> CardChartConfigItems { get; } = new();

        /// <summary>req-109：当前选中 Provider 的 Mini 图表配置项集合（任务栏 Mini 同构）。</summary>
        public ObservableCollection<MiniChartConfigItem> CardMiniChartItems { get; } = new();

        /// <summary>从 <c>_pluginManager.Plugins</c> 扫描有 Card 声明的 Provider，自动选中第一个。</summary>
        public void RefreshCardChartConfigProviders()
        {
            var existing = CardChartConfigProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in _pluginManager.Plugins)
            {
                var pid = plugin.Provider.ProviderId;
                var card = plugin.Provider.Card;
                if (card == null || card.Charts.Count == 0) continue;
                seen.Add(pid);
                if (!existing.Contains(pid)) CardChartConfigProviders.Add(pid);
            }
            for (int i = CardChartConfigProviders.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(CardChartConfigProviders[i])) CardChartConfigProviders.RemoveAt(i);
            }
            if (SelectedCardChartProviderId == null || !seen.Contains(SelectedCardChartProviderId))
            {
                SelectedCardChartProviderId = CardChartConfigProviders.FirstOrDefault();
            }
            else
            {
                ReloadCardChartAccounts();
                ReloadCardChartConfigItems();
            }
            // req-109：Mini 图表列表同步刷新（同上下文）
            if (CardMiniChartItems.Count == 0)
            {
                RefreshCardMiniChartItems();
            }
        }

        /// <summary>req-109：根据当前选中 Provider 从 ConfigService 拉账号列表。无 Accounts 配置时回退 ["default"]（向后兼容）。</summary>
        public void ReloadCardChartAccounts()
        {
            CardChartConfigAccounts.Clear();
            var pid = SelectedCardChartProviderId;
            if (string.IsNullOrEmpty(pid)) return;
            var accounts = _configService?.GetAccounts(pid);
            if (accounts != null && accounts.Count > 0)
            {
                foreach (var a in accounts) CardChartConfigAccounts.Add(a.AccountId);
            }
            else
            {
                CardChartConfigAccounts.Add("default");
            }
            // 默认选中第一项
            if (string.IsNullOrEmpty(SelectedCardChartConfigAccountId) || !CardChartConfigAccounts.Contains(SelectedCardChartConfigAccountId))
            {
                SelectedCardChartConfigAccountId = CardChartConfigAccounts.FirstOrDefault();
            }
            ReloadCardChartConfigItems();
            // req-109：Mini 图表项同步刷新
            RefreshCardMiniChartItems();
        }
    
        /// <summary>根据当前选中 Provider 重新填充 <see cref="CardChartConfigItems"/>（读取 defaults.json + AccountCustomization 合并）。</summary>
        public void ReloadCardChartConfigItems()
        {
            CardChartConfigItems.Clear();
            var pid = SelectedCardChartProviderId;
            if (string.IsNullOrEmpty(pid)) return;
            var plugin = _pluginManager.Plugins.FirstOrDefault(p => string.Equals(p.Provider.ProviderId, pid, StringComparison.OrdinalIgnoreCase));
            if (plugin == null) return;
            var card = plugin.Provider.Card;
            if (card == null) return;
            var effective = _configService.GetEffectiveAccountCustomization(pid, SelectedCardChartConfigAccountId ?? "default");
    
            // 图表顺序：VisibleCharts 非 null 时用其顺序，否则用 defaults.json 声明顺序
            var orderedCharts = (effective.VisibleCharts != null)
                ? card.Charts.Where(c => effective.VisibleCharts.Contains(c.ChartId)).ToList()
                : card.Charts.ToList();
            if (effective.VisibleCharts != null)
            {
                foreach (var id in effective.VisibleCharts)
                {
                    var c = card.Charts.FirstOrDefault(x => x.ChartId == id);
                    if (c != null && !orderedCharts.Contains(c)) orderedCharts.Add(c);
                }
            }
    
            foreach (var chart in orderedCharts)
            {
                var visibleDataGroups = effective.VisibleDataGroups != null && effective.VisibleDataGroups.TryGetValue(chart.ChartId, out var vdg) ? vdg : null;
                var dataGroupOrders = effective.DataGroupOrders != null && effective.DataGroupOrders.TryGetValue(chart.ChartId, out var dgo) ? dgo : null;
                var orderedGroups = (visibleDataGroups != null)
                    ? chart.DataGroups.Where(g => visibleDataGroups.Contains(g.Id)).ToList()
                    : chart.DataGroups.ToList();
                if (visibleDataGroups != null)
                {
                    foreach (var id in visibleDataGroups)
                    {
                        var g = chart.DataGroups.FirstOrDefault(x => x.Id == id);
                        if (g != null && !orderedGroups.Contains(g)) orderedGroups.Add(g);
                    }
                }
                var item = new CardChartConfigItem
                {
                    ChartId = chart.ChartId,
                    ChartKind = chart.Kind.ToString(),
                    Title = chart.ChartId,
                    IsVisible = effective.VisibleCharts == null || effective.VisibleCharts.Contains(chart.ChartId),
                };
                foreach (var g in orderedGroups)
                {
                    item.DataGroups.Add(new DataGroupConfigItem
                    {
                        DataGroupId = g.Id,
                        DisplayName = g.Id,
                        IsVisible = visibleDataGroups == null || visibleDataGroups.Contains(g.Id),
                    });
                }
                // req-105：加载该图表的 Tooltip 显示字段（null 集合 = 沿用默认）
                if (effective.VisibleTooltipFields != null && effective.VisibleTooltipFields.TryGetValue(chart.ChartId, out var tipFields) && tipFields != null)
                {
                    foreach (var f in tipFields) item.TooltipFields.Add(f);
                }
                CardChartConfigItems.Add(item);
            }
        }
    
        /// <summary>req-107 B6 演进：把当前 <see cref="CardChartConfigItems"/> 一次性写回 ConfigService。</summary>
        public void SaveCardChartConfig()
        {
            var pid = SelectedCardChartProviderId;
            if (string.IsNullOrEmpty(pid)) return;
            var config = new AccountCustomization
            {
                VisibleCharts = CardChartConfigItems.Where(c => c.IsVisible).Select(c => c.ChartId).ToList(),
                VisibleDataGroups = CardChartConfigItems.ToDictionary(
                    c => c.ChartId,
                    c => (List<string>?)c.DataGroups.Where(g => g.IsVisible).Select(g => g.DataGroupId).ToList()),
                // req-105：每张图表的 Tooltip 显示字段（仅保存非空的）
                VisibleTooltipFields = CardChartConfigItems
                    .Where(c => c.TooltipFields.Count > 0)
                    .ToDictionary(c => c.ChartId, c => (List<string>?)c.TooltipFields.ToList()),
            };
            _configService.SetCardChartConfiguration(pid, config, accountId: SelectedCardChartConfigAccountId ?? "default");
        }
    
        /// <summary>req-107 B6 演进：将指定图表项上移一位。</summary>
        public void MoveCardChartUp(CardChartConfigItem? item)
        {
            if (item == null) return;
            var i = CardChartConfigItems.IndexOf(item);
            if (i > 0) CardChartConfigItems.Move(i, i - 1);
        }
    
        /// <summary>req-107 B6 演进：将指定图表项下移一位。</summary>
        public void MoveCardChartDown(CardChartConfigItem? item)
        {
            if (item == null) return;
            var i = CardChartConfigItems.IndexOf(item);
            if (i >= 0 && i < CardChartConfigItems.Count - 1) CardChartConfigItems.Move(i, i + 1);
        }
    
        /// <summary>req-107 B6 演进：将指定数据组项上移一位（自动从 CardChartConfigItems 中查找所属图表）。</summary>
    public void MoveDataGroupUp(DataGroupConfigItem? item)
    {
        if (item == null) return;
        foreach (var chart in CardChartConfigItems)
        {
            var i = chart.DataGroups.IndexOf(item);
            if (i > 0) { chart.DataGroups.Move(i, i - 1); return; }
            if (i >= 0) return;
        }
    }

    /// <summary>req-107 B6 演进：将指定数据组项下移一位（自动从 CardChartConfigItems 中查找所属图表）。</summary>
    public void MoveDataGroupDown(DataGroupConfigItem? item)
    {
        if (item == null) return;
        foreach (var chart in CardChartConfigItems)
        {
            var i = chart.DataGroups.IndexOf(item);
            if (i >= 0)
            {
                if (i < chart.DataGroups.Count - 1) chart.DataGroups.Move(i, i + 1);
                return;
            }
        }
    }

    /// <summary>req-107 B6 演进：RelayCommand 包装——便于 XAML 命令绑定。</summary>
    public CommunityToolkit.Mvvm.Input.IRelayCommand<CardChartConfigItem> MoveCardChartUpCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<CardChartConfigItem>(MoveCardChartUp);
    public CommunityToolkit.Mvvm.Input.IRelayCommand<CardChartConfigItem> MoveCardChartDownCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<CardChartConfigItem>(MoveCardChartDown);
    public CommunityToolkit.Mvvm.Input.IRelayCommand<DataGroupConfigItem> MoveDataGroupUpCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<DataGroupConfigItem>(MoveDataGroupUp);
    public CommunityToolkit.Mvvm.Input.IRelayCommand<DataGroupConfigItem> MoveDataGroupDownCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<DataGroupConfigItem>(MoveDataGroupDown);

    /// <summary>req-105：切换某图表的 Tooltip 字段（含/不含）。参数=(图表, 字段名)。</summary>
    public void ToggleTooltipField(CardChartConfigItem? chart, string? fieldName)
    {
        if (chart == null || string.IsNullOrEmpty(fieldName)) return;
        if (chart.TooltipFields.Contains(fieldName)) chart.TooltipFields.Remove(fieldName);
        else chart.TooltipFields.Add(fieldName);
    }
    public CommunityToolkit.Mvvm.Input.IRelayCommand<object> ToggleTooltipFieldCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<object>(args =>
        {
            if (args is not object[] arr || arr.Length < 2) return;
            ToggleTooltipField(arr[0] as CardChartConfigItem, arr[1] as string);
        });

    /// <summary>req-109：刷新 Mini 图表配置项（从 plugin 声明 + effective accountCustomization 合并）。</summary>
    public void RefreshCardMiniChartItems()
    {
        CardMiniChartItems.Clear();
        var pid = SelectedCardChartProviderId;
        if (string.IsNullOrEmpty(pid)) return;
        var accountId = SelectedCardChartConfigAccountId ?? "default";
        var plugin = _pluginManager.Plugins.FirstOrDefault(p => string.Equals(p.Provider.ProviderId, pid, StringComparison.OrdinalIgnoreCase));
        if (plugin == null) return;
        var card = plugin.Provider.Taskbar;
        if (card == null) return;
        var eff = _configService.GetEffectiveAccountCustomization(pid, accountId, SelectedCardChartConfigCardId ?? "default-card");
        foreach (var mini in card.MiniCharts)
        {
            bool visible = eff.VisibleMiniCharts == null || eff.VisibleMiniCharts.Contains(mini.ChartId);
            CardMiniChartItems.Add(new MiniChartConfigItem
            {
                ChartId = mini.ChartId,
                Kind = mini.Kind.ToString(),
                ProviderId = pid,
                AccountId = accountId,
                IsVisible = visible,
            });
        }
    }

    /// <summary>req-109：Mini 图表上移 / 下移（按 Provider 粒度）。</summary>
    public void MoveMiniChartUp(MiniChartConfigItem? item)
    {
        if (item == null) return;
        var i = CardMiniChartItems.IndexOf(item);
        if (i > 0) CardMiniChartItems.Move(i, i - 1);
    }
    public void MoveMiniChartDown(MiniChartConfigItem? item)
    {
        if (item == null) return;
        var i = CardMiniChartItems.IndexOf(item);
        if (i >= 0 && i < CardMiniChartItems.Count - 1) CardMiniChartItems.Move(i, i + 1);
    }
    public CommunityToolkit.Mvvm.Input.IRelayCommand<MiniChartConfigItem> MoveMiniChartUpCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<MiniChartConfigItem>(MoveMiniChartUp);
    public CommunityToolkit.Mvvm.Input.IRelayCommand<MiniChartConfigItem> MoveMiniChartDownCommand
        => new CommunityToolkit.Mvvm.Input.RelayCommand<MiniChartConfigItem>(MoveMiniChartDown);

    /// <summary>req-109：把 <see cref="CardMiniChartItems"/> 写回 ConfigService（3 段 key）。</summary>
    public void SaveMiniChartConfig()
    {
        var pid = SelectedCardChartProviderId;
        if (string.IsNullOrEmpty(pid)) return;
        var config = new AccountCustomization
        {
            VisibleMiniCharts = CardMiniChartItems.Where(m => m.IsVisible).Select(m => m.ChartId).ToList(),
        };
        _configService.SetMiniChartConfiguration(pid, config,
            accountId: SelectedCardChartConfigAccountId ?? "default",
            cardId: SelectedCardChartConfigCardId ?? "default-card");
    }

    /// <summary>
    /// req-109：返回指定 Provider 的有效可见 Mini 图表 ID 列表（供 TaskbarWindow 渲染端过滤）。
    /// <para>null = 未配置（全部可见，向后兼容）；空集合 = 用户关闭了该 Provider 的全部 Mini 图表。</para>
    /// </summary>
    public List<string>? GetEffectiveVisibleMiniCharts(string providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return null;
        var eff = _configService.GetEffectiveAccountCustomization(providerId, "default", "default-card");
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
    // REQ-098 任务栏迷你图表 SDK 完善
    // =====================================================================

    /// <summary>
    /// req-098：设置窗口“任务栏迷你图表” Tab 的列表项集合。每个 Provider 一项。
    /// <para>由 <see cref="RefreshTaskbarMiniChartOptions"/> 从 <c>_pluginManager.Plugins</c>
    /// 同步生成；只对 SupportedMiniCharts 非空的插件生成（纯 API Key 模式插件不入表）。</para>
    /// </summary>
    public ObservableCollection<TaskbarMiniChartProviderViewModel> TaskbarMiniChartOptions { get; } = new();

    /// <summary>
    /// req-098：刷新 <see cref="TaskbarMiniChartOptions"/> 集合。
    /// <para>遍历 <c>_pluginManager.Plugins</c>，对每个 <c>SupportedMiniCharts</c> 非空的插件
    /// 创建一个 <see cref="TaskbarMiniChartProviderViewModel"/>。已存在的不重建（保留用户修改）。</para>
    /// </summary>
    public void RefreshTaskbarMiniChartOptions()
    {
        var existing = TaskbarMiniChartOptions.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in _pluginManager.Plugins)
        {
            var provider = plugin.Provider;
            var supportedCharts = provider.SupportedMiniCharts ?? Array.Empty<Core.Plugins.MiniChart.MiniChartKind>();
            if (supportedCharts.Count == 0) continue;
            seen.Add(provider.ProviderId);
            if (existing.TryGetValue(provider.ProviderId, out var kept))
            {
                // 保留既有 VM（用户的修改不被清空）
                continue;
            }
            TaskbarMiniChartOptions.Add(new TaskbarMiniChartProviderViewModel(
                provider.ProviderId,
                provider.DisplayName,
                supportedCharts,
                provider.MiniChartDataTypes ?? Array.Empty<Core.Plugins.MiniChart.MiniChartContentKind>(),
                _configService));
        }
        // 移除已卸载的 Provider 对应项
        for (int i = TaskbarMiniChartOptions.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(TaskbarMiniChartOptions[i].ProviderId))
                TaskbarMiniChartOptions.RemoveAt(i);
        }
    }

    /// <summary>
    /// req-104：刷新 MultiProgressFieldItems 集合（从 EnabledUsages 同步）。
    /// </summary>
    public void RefreshMultiProgressFieldItems()
    {
        MultiProgressFieldItems.Clear();
        foreach (var vm in EnabledUsages)
        {
            var provider = vm.Provider;
            if (provider == null) continue;

            // 添加进度条字段
            if (vm.DeclarativeBars?.Bars != null)
            {
                foreach (var bar in vm.DeclarativeBars.Bars)
                {
                    var isSelected = IsProgressFieldSelected(vm.ProviderId, bar.Label);
                    MultiProgressFieldItems.Add(new Helpers.MultiProgressFieldItem
                    {
                        ProviderId = vm.ProviderId,
                        ProviderDisplayName = vm.DisplayName,
                        FieldName = bar.Label,
                        FieldDisplayName = bar.Label,
                        IsSelected = isSelected,
                        FieldType = "Progress"
                    });
                }
            }

            // 添加数字网格字段
            if (vm.DeclarativeNumber?.Items != null)
            {
                foreach (var item in vm.DeclarativeNumber.Items)
                {
                    var isSelected = IsMetricFieldSelected(vm.ProviderId, item.Label);
                    MultiProgressFieldItems.Add(new Helpers.MultiProgressFieldItem
                    {
                        ProviderId = vm.ProviderId,
                        ProviderDisplayName = vm.DisplayName,
                        FieldName = item.Label,
                        FieldDisplayName = item.Label,
                        IsSelected = isSelected,
                        FieldType = "Metric"
                    });
                }
            }
        }
    }

    /// <summary>
    /// req-104：检查指定 Provider 的进度条字段是否被选中。
    /// </summary>
    private bool IsProgressFieldSelected(string providerId, string fieldName)
    {
        if (!_configService.Settings.SelectedProgressFields.TryGetValue(providerId, out var selectedFields))
            return true; // 默认全部选中
        return selectedFields.Contains(fieldName);
    }

    /// <summary>
    /// req-104：检查指定 Provider 的数字网格字段是否被选中。
    /// </summary>
    private bool IsMetricFieldSelected(string providerId, string fieldName)
    {
        if (!_configService.Settings.SelectedMetricFields.TryGetValue(providerId, out var selectedFields))
            return true; // 默认全部选中
        return selectedFields.Contains(fieldName);
    }

    /// <summary>
    /// req-104：保存多进度条字段选择到配置。
    /// </summary>
    public void SaveMultiProgressFields()
    {
        // 按 Provider 分组保存进度条字段
        var progressGroups = MultiProgressFieldItems
            .Where(x => x.FieldType == "Progress")
            .GroupBy(x => x.ProviderId);
        foreach (var group in progressGroups)
        {
            var selectedFields = group.Where(x => x.IsSelected).Select(x => x.FieldName).ToList();
            if (selectedFields.Count > 0)
                _configService.Settings.SelectedProgressFields[group.Key] = selectedFields;
            else
                _configService.Settings.SelectedProgressFields.Remove(group.Key);
        }

        // 按 Provider 分组保存数字网格字段
        var metricGroups = MultiProgressFieldItems
            .Where(x => x.FieldType == "Metric")
            .GroupBy(x => x.ProviderId);
        foreach (var group in metricGroups)
        {
            var selectedFields = group.Where(x => x.IsSelected).Select(x => x.FieldName).ToList();
            if (selectedFields.Count > 0)
                _configService.Settings.SelectedMetricFields[group.Key] = selectedFields;
            else
                _configService.Settings.SelectedMetricFields.Remove(group.Key);
        }

        _configService.Save();
        // 触发 ConfigChanged 事件，ProviderUsageViewModel 会重新过滤
    }

    // =====================================================================
    // REQ-097 卡片图表顺序用户可调整
    // =====================================================================

    /// <summary>
    /// req-097：图表顺序设置页的列表项集合（按 Provider 分组）。
    /// </summary>
    public ObservableCollection<Helpers.ChartOrderItem> ChartOrderItems { get; } = new();

    /// <summary>
    /// req-097：恢复默认图表顺序命令（按插件声明顺序）。
    /// </summary>
    public IRelayCommand ResetChartOrderCommand { get; private set; } = null!;

    /// <summary>
    /// req-097：刷新 ChartOrderItems 集合（从 EnabledUsages 同步）。
    /// </summary>
    public void RefreshChartOrderItems()
    {
        ChartOrderItems.Clear();
        foreach (var vm in EnabledUsages)
        {
            var provider = vm.Provider;
            if (provider == null) continue;

            // 获取用户自定义顺序，若无则使用插件声明顺序（req-107 B6：优先 Card.Charts，ChartKindExtractor 已做 DeclarativeChartKind→CardChartKind 映射）
            var chartOrder = GetProviderChartOrder(vm.ProviderId, ChartKindExtractor.ExtractDeclaredChartKinds(provider));
            foreach (var chartKind in chartOrder)
            {
                ChartOrderItems.Add(new Helpers.ChartOrderItem
                {
                    ProviderId = vm.ProviderId,
                    ProviderDisplayName = vm.DisplayName,
                    ChartKind = chartKind
                });
            }
        }
    }

    /// <summary>
    /// req-097：获取指定 Provider 的图表顺序（用户自定义优先，回退到插件声明）。
    /// </summary>
    private IReadOnlyList<CardChartKind> GetProviderChartOrder(string providerId, IReadOnlyList<CardChartKind> supportedCharts)
        // req-099 B1：图表顺序解析已抽离到 DisplayModule。
        => _displayModule.GetChartOrder(providerId, supportedCharts);

    /// <summary>
    /// req-097：保存当前 ChartOrderItems 顺序到配置。
    /// </summary>
    public void SaveChartOrder()
    {
        // 按 Provider 分组保存图表顺序
        var groups = ChartOrderItems.GroupBy(x => x.ProviderId);
        foreach (var group in groups)
        {
            var chartOrder = group.Select(x => x.ChartKind).ToList();
            _configService.Settings.ProviderChartOrder[group.Key] = chartOrder;
        }
        _configService.Save();
        // 触发 ConfigChanged 事件，MainViewModel 会重新排序图表
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
    // req-026 环形图中心数字「按 Provider 独立启用」设置
    // =====================================================================

    /// <summary>req-026：环形图中心数字「每个 Provider × 支持的 metric」勾选状态集合。
    /// <para>绑定到设置窗口 Tab "环形图中心"，每行一个 Provider，每行内多 CheckBox（每个支持的 metric 一个）。
    /// 勾选状态写回 <c>AppSettings.ProviderEnabledRingChartMetrics[providerId]</c>。</para></summary>
    public ObservableCollection<ProviderRingChartMetricGroup> ProviderRingChartMetricGroups { get; } = new();

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
