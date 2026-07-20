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
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 单个服务商的用量显示模型
/// </summary>
public class ProviderUsageViewModel : INotifyPropertyChanged
{
    private string _providerId = string.Empty;
    private string _displayName = string.Empty;
    private string _statusText = "未查询";
    private double _usagePercentage;
    private string _usedText = "--";
    private string _totalText = "--";
    private string _remainingText = "--";
    private string _lastUpdateText = "--";
    private bool _isEnabled = true;
    private bool _isError;
    private string? _errorMessage;
    private TaskbarDisplayMode _displayMode = TaskbarDisplayMode.Text;
    private CardChartKind _cardChartKind = CardChartKind.None;
    private IReadOnlyList<double> _historyValues = Array.Empty<double>();
    // req-008：余额快照重构为多列布局，使用 ObservableCollection<BalanceItem> 取代旧的 5 个拼接字段。
    // 拆为集合的好处是：插件只需提供 BalanceItems 集合即可在 XAML 中动态生成列与分隔线，
    // 不再受限于拼接文本（"12,345 / 678,901（5h窗口剩余）"）这种单列布局。
    private readonly System.Collections.ObjectModel.ObservableCollection<UsageMonitor.Core.Models.BalanceItem> _balanceItems = new();
    private Action? _openConfigAction;
    private Func<Task>? _refreshCardAction;
    // 卡片多进度条与订阅档位相关字段
    private string _subscriptionTitle = "Token Plan 订阅";
    private bool _isSubscriptionActive;
    private double _primaryBarPercent;
    private double _weeklyBarPercent;
    private string _primaryResetText = "--";
    private string _weeklyResetText = "--";
    // req-028：5h 重置的精确时刻（来自 mm_5hResetAt extras）。null 表示该 Provider 无 5h 字段（不应出现在 5h 倒计时语境）。
    private DateTime? _next5hResetAt;
    private string _fiveHourCountdownText = "00:00:00";
    private bool _fiveHourAutoRefreshTriggered;
    private string _videoQuotaText = "--";
    private string _videoWeeklyText = "--";
    private double _remainingCredits;
    private double _videoIntervalPercent;
    private double _videoWeeklyPercent;
    private IReadOnlyList<string> _renderKinds = Array.Empty<string>();
    private bool _show5hBar = true;
    private bool _showWeeklyBar = true;
    private bool _showVideo5hBar = true;
    private bool _showVideoWeeklyBar = true;
    // 卡片图表多选与折线/热力图数据相关字段
    private IReadOnlyList<CardChartKind> _cardChartKinds = Array.Empty<CardChartKind>();
    private IReadOnlyList<double> _cardLineValues = Array.Empty<double>();
    private double _cardLineMax = 100;
    // MiniMax DOM 抓取模式标记：为 true 时折线图使用「每日 Token」，不被历史用量百分比覆盖。
    private bool _isDomExtractMode;
    // req-007：折线图完整化字段。SupportsPeriodSwitch=true 的插件（仅 MiniMax）会启用周期切换按钮。
    private IReadOnlyList<string> _dates = Array.Empty<string>();
    private IReadOnlyList<string>? _extraTooltipLines;
    private bool _supportsPeriodSwitch;
    private string _currentPeriod = UsageMonitor.App.Controls.ChartPeriods.Week;
    private bool _isLoading;
    // req-072 U-05：卡片详情展开状态（默认折叠）
    private bool _isDetailExpanded;
    // req-007：缓存 MiniMax DOM 抓取到的「每日 Token」完整数据，供 PeriodChanged 重新切片。
    // 完整数据按日期升序，最多 168 天（MiniMax usage_summary 接口上限）。
    private IReadOnlyList<long> _fullDailyValues = Array.Empty<long>();
    private IReadOnlyList<string> _fullDailyDates = Array.Empty<string>();
    // req-034 修复：完整缓存命中率数据，供 SliceCardLineByPeriod 按周期切片
    private IReadOnlyList<double> _fullDailyCacheHitPercents = Array.Empty<double>();
    // req-026：当前 Provider 启用的环形图中心 metric key 列表，绑定到 RingChartControl.EnabledMetrics。
    private IReadOnlyList<string> _enabledRingChartMetrics = Array.Empty<string>();

    /// <summary>
    /// 创建用量显示 VM。
    /// </summary>
    /// <param name="openConfigAction">
    /// 点击卡片上"⚙ 设置"按钮时触发的回调。
    /// 不传时点击按钮不会有反应（插件没配置项的情况）。
    /// </param>
    /// <param name="refreshCardAction">
    /// 点击卡片右上角"⟳ 刷新"按钮时触发的回调（仅刷新本卡片对应服务商）。
    /// 不传时刷新按钮为禁用态。
    /// </param>
    public ProviderUsageViewModel(Action? openConfigAction = null, Func<Task>? refreshCardAction = null)
    {
        _openConfigAction = openConfigAction;
        _refreshCardAction = refreshCardAction;
        ConfigCommand = new RelayCommand(OpenConfig, () => _openConfigAction != null);
        RefreshCardCommand = new AsyncRelayCommand(RefreshCardAsync, () => _refreshCardAction != null);
    }

    /// <summary>
    /// 供 MainViewModel 在创建时注入的 ConfigService。
    /// 负责把 PluginConfigWindow 中改的 4 个进度条可见性开关同步到当前 VM 的属性。
    /// </summary>
    public UsageMonitor.Core.Services.ConfigService? ConfigService { get; private set; }

    /// <summary>
    /// 主 VM 装配时调用一次：注入 ConfigService 并首次从最新配置读取 4 个进度条开关。
    /// 之后订阅 ConfigChanged 事件，配置变更时自动重新读取。
    /// </summary>
    public void AttachConfigService(UsageMonitor.Core.Services.ConfigService? configService)
    {
        if (ConfigService != null)
            ConfigService.ConfigChanged -= OnConfigChanged;
        ConfigService = configService;
        if (configService != null)
        {
            configService.ConfigChanged += OnConfigChanged;
            ReloadBarToggles();
        }
    }

    /// <summary>
    /// 配置变更时重新读取 MiniMax ProviderConfig 中 4 个进度条开关并通知属性变更。
    /// </summary>
    private void OnConfigChanged(object? sender, EventArgs e)
    {
        ReloadBarToggles();
    }

    /// <summary>
    /// 从当前 ConfigService 拉取 MiniMax 的 ProviderConfig，按 key 取 Show5hBar 等字段。
    /// 缺省时维持属性当前值（首次初始化为 true）。
    /// </summary>
    private void ReloadBarToggles()
    {
        if (ConfigService == null || string.IsNullOrEmpty(_providerId)) return;
        var cfg = ConfigService.Settings.ProviderConfigs.Values
            .FirstOrDefault(p => p.ProviderId == _providerId);
        if (cfg == null) return;
        Show5hBar = ReadBool(cfg, "Show5hBar", _show5hBar);
        ShowWeeklyBar = ReadBool(cfg, "ShowWeeklyBar", _showWeeklyBar);
        ShowVideo5hBar = ReadBool(cfg, "ShowVideo5hBar", _showVideo5hBar);
        ShowVideoWeeklyBar = ReadBool(cfg, "ShowVideoWeeklyBar", _showVideoWeeklyBar);
    }

    /// <summary>读取 boolean 配置项，缺省时使用 currentDefault。</summary>
    private static bool ReadBool(ProviderConfig cfg, string key, bool currentDefault)
    {
        var raw = cfg.GetValue(key);
        if (string.IsNullOrWhiteSpace(raw)) return currentDefault;
        return bool.TryParse(raw, out var b) ? b : currentDefault;
    }

    /// <summary>
    /// 主窗口中卡片右上角"⚙ 设置"按钮绑定的命令（调用构造时传入的回调）。
    /// </summary>
    public RelayCommand ConfigCommand { get; }

    /// <summary>
    /// 主窗口中卡片右上角"⟳ 刷新"按钮绑定的命令（仅刷新本卡片对应服务商）。
    /// 使用 AsyncRelayCommand：执行期间自动禁用按钮，避免重复点击导致并发请求。
    /// </summary>
    public IAsyncRelayCommand RefreshCardCommand { get; }

    /// <summary>
    /// 主窗口卡片点击设置按钮时调用外部逻辑（打开 PluginConfigWindow）。
    /// </summary>
    private void OpenConfig()
    {
        _openConfigAction?.Invoke();
    }

    /// <summary>
    /// 触发构造时注入的"刷新本卡片"回调；未注入时返回已完成任务，不做任何事。
    /// </summary>
    private Task RefreshCardAsync() => _refreshCardAction?.Invoke() ?? Task.CompletedTask;

    public string ProviderId { get => _providerId; set { _providerId = value; OnPropertyChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }

    /// <summary>Provider 图标的文件路径，用于在卡片、任务栏、悬浮窗中显示 logo</summary>
    public string? IconPath { get => _iconPath; set { _iconPath = value; OnPropertyChanged(); } }
    private string? _iconPath;

    /// <summary>
    /// 根据 ProviderId 解析对应的图标文件路径。
    /// 图标文件通过 csproj Content 项复制到输出目录的 Assets/Providers/ 下。
    /// SVG 格式 WPF 不原生支持，返回 null 跳过。
    /// </summary>
    public static string? ResolveIconPath(string providerId)
    {
        // ProviderId -> 图标文件名（不含扩展名）
        var name = providerId.ToLowerInvariant() switch
        {
            "minimax" => "minimax",
            "deepseek" => "deepseek",
            "mimo" => "mimo",
            "kimi" => "kimi",
            "volcengine" => "volcengine",
            "zhipu" => "zhipu",
            "ollama" => "ollama",
            "openrouter" => "openrouter",
            "openai" => "openai",
            "anthropic" => "anthropic",
            "step" => null,       // SVG 格式，WPF 不原生支持，暂跳过
            "siliconflow" => "siliconflow",
            _ => null
        };
        if (name == null) return null;

        // 根据实际文件扩展名构造文件路径
        var ext = name switch
        {
            "minimax" => ".ico",
            "deepseek" => ".png",
            "mimo" => ".jpg",
            "kimi" => ".ico",
            "volcengine" => ".png",
            "zhipu" => ".png",
            "ollama" => ".png",
            "openrouter" => ".ico",
            "openai" => ".png",
            "anthropic" => ".ico",
            "siliconflow" => ".png",
            _ => ".png"
        };

        // 图标文件通过 csproj Content 项复制到输出目录，使用文件路径加载
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(basePath, "Assets", "Providers", $"{name}{ext}");
        return File.Exists(filePath) ? filePath : null;
    }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public double UsagePercentage { get => _usagePercentage; set { _usagePercentage = value; OnPropertyChanged(); } }
    public string UsedText { get => _usedText; set { _usedText = value; OnPropertyChanged(); } }
    public string TotalText { get => _totalText; set { _totalText = value; OnPropertyChanged(); } }
    public string RemainingText { get => _remainingText; set { _remainingText = value; OnPropertyChanged(); } }
    public string LastUpdateText { get => _lastUpdateText; set { _lastUpdateText = value; OnPropertyChanged(); } }
    public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    public bool IsError { get => _isError; set { _isError = value; OnPropertyChanged(); } }
    public string? ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
    /// <summary>req-008：余额快照多列布局数据源（默认 4 项：累计 / 峰值 / 活跃 / 积分余额）。
    /// 由 <c>UpdateBalanceFromExtra</c> 组装，插件可通过 <see cref="IUsageProvider.BalanceItems"/> 覆盖/追加/隐藏默认项。
    /// XAML 端用 ItemsControl 横向拼接 + 1px 竖向分隔符。</summary>
    public System.Collections.ObjectModel.ObservableCollection<UsageMonitor.Core.Models.BalanceItem> BalanceItems => _balanceItems;

    /// <summary>订阅档位胶囊文案：已订阅返回具体档位名，未订阅或未抓到返回默认占位</summary>
    public string SubscriptionTitle { get => _subscriptionTitle; set { _subscriptionTitle = value; OnPropertyChanged(); } }

    /// <summary>是否已订阅（按后端 combo_id 是否存在判断）</summary>
    public bool IsSubscriptionActive { get => _isSubscriptionActive; set { _isSubscriptionActive = value; OnPropertyChanged(); } }

    /// <summary>5h 限额进度条已使用百分比（0-100）</summary>
    public double PrimaryBarPercent { get => _primaryBarPercent; set { _primaryBarPercent = value; OnPropertyChanged(); } }

    /// <summary>周限额进度条已使用百分比（0-100）</summary>
    public double WeeklyBarPercent { get => _weeklyBarPercent; set { _weeklyBarPercent = value; OnPropertyChanged(); } }

    /// <summary>5h 限额重置剩余文案（"2 小时 21 分钟后重置"）</summary>
    public string PrimaryResetText { get => _primaryResetText; set { _primaryResetText = value; OnPropertyChanged(); } }

    /// <summary>req-051：当前圆环图显示的 metric 名称（如"5h 用量"、"周用量"等）。</summary>
    public string CurrentMetricName { get => _currentMetricName; set { _currentMetricName = value; OnPropertyChanged(); OnPropertyChanged(nameof(TaskbarToolTipText)); } }
    private string _currentMetricName = "5h 用量";

    /// <summary>req-051：任务栏圆环图 tooltip 文本（Provider 名字 + 数据名字 + 重置倒计时）。</summary>
    public string TaskbarToolTipText
    {
        get
        {
            var resetInfo = string.IsNullOrEmpty(FiveHourCountdownText) ? "" : $"\n重置：{FiveHourCountdownText}";
            return $"{DisplayName}\n{CurrentMetricName}{resetInfo}";
        }
    }

    /// <summary>
    /// req-028：根据 <see cref="Next5hResetAt"/> 重新计算 <see cref="FiveHourCountdownText"/>。
    /// <para>由 MainViewModel 的全局每秒 timer 调用；不需要 INPC 子订阅。
    /// 返回当前倒计时 + 是否已超过 0（”到点了“），供 MainViewModel 决定是否调 <c>RefreshProviderAsync</c>。</para>
    /// </summary>
    /// <param name="now">外部传“当时”便于测试；不传则用 <see cref="DateTime.Now"/>。</param>
    /// <returns>(剩余时长, 是否≤0) 剩余&gt;0时返回 remaining+false；小于等于0或 null 时返回 (Zero, true)。</returns>
    public (TimeSpan remaining, bool isElapsed) RefreshFiveHourCountdownText(DateTime? now = null)
    {
        var current = now ?? DateTime.Now;
        var target = _next5hResetAt;
        if (target == null)
        {
            FiveHourCountdownText = "00:00:00";
            return (TimeSpan.Zero, true);
        }
        var remaining = target.Value - current;
        if (remaining <= TimeSpan.Zero)
        {
            FiveHourCountdownText = "00:00:00";
            return (TimeSpan.Zero, true);
        }
        FiveHourCountdownText = UsageMonitor.App.Helpers.CountdownFormatter.Format(remaining);
        return (remaining, false);
    }

    /// <summary>req-028：MainViewModel 检查到“到点了”时调用，标记本次窗口已经触发；到下个新 mm_5hResetAt 出现时被重置。</summary>
    public void MarkFiveHourAutoRefreshTriggered() => _fiveHourAutoRefreshTriggered = true;

    /// <summary>req-028：检查本 Provider 是否应该被自动刷新（倒计时≤0 且本窗口未触发过）。</summary>
    public bool ShouldTriggerFiveHourAutoRefresh()
        => _next5hResetAt.HasValue
           && _next5hResetAt.Value <= DateTime.Now
           && !_fiveHourAutoRefreshTriggered;

    /// <summary>
    /// req-028：5h 重置精确时刻（来自 mm_5hResetAt extras）。
    /// <para>用于计算托盘/悬浮窗倒计时 <see cref="FiveHourCountdownText"/>，以及到时自动刷新的判定。
    /// 由 <c>UpdateFromMiniMaxDom</c> 写入；为 null 时表示该 Provider 不参与 5h 倒计时。</para>
    /// </summary>
    public DateTime? Next5hResetAt
    {
        get => _next5hResetAt;
        set
        {
            if (_next5hResetAt == value) return;
            _next5hResetAt = value;
            _fiveHourAutoRefreshTriggered = false; // 新值出现意味着倒计时重置，允许下次再次到达 0 时触发
            OnPropertyChanged();
            OnPropertyChanged(nameof(FiveHourCountdownText));
            OnPropertyChanged(nameof(HasFiveHourCountdown));
        }
    }

    /// <summary>
    /// req-028：5h 倒计时显示文本（HH:mm:ss 形式）。
    /// <para>由 MainViewModel 装配的每 1 秒 <c>DispatcherTimer</c> 刷新（直接调用
    /// <see cref="RefreshFiveHourCountdownText"/>）。托盘/悬浮窗均可绑此属性。</para>
    /// </summary>
    public string FiveHourCountdownText
    {
        get => _fiveHourCountdownText;
        set { if (_fiveHourCountdownText == value) return; _fiveHourCountdownText = value; OnPropertyChanged(); OnPropertyChanged(nameof(TaskbarToolTipText)); }
    }

    /// <summary>req-028：是否存在有效 5h 重置时间（用于按需显隐倒计时 UI）。</summary>
    public bool HasFiveHourCountdown => _next5hResetAt.HasValue;

    /// <summary>周限额重置剩余文案（"5 天 2 小时后重置"）</summary>
    public string WeeklyResetText { get => _weeklyResetText; set { _weeklyResetText = value; OnPropertyChanged(); } }

    /// <summary>视频赠送 5h 维度已用/总额（"0/3"）</summary>
    public string VideoQuotaText { get => _videoQuotaText; set { _videoQuotaText = value; OnPropertyChanged(); } }

    /// <summary>视频赠送 周维度已用/总额（"0/21"）</summary>
    public string VideoWeeklyText { get => _videoWeeklyText; set { _videoWeeklyText = value; OnPropertyChanged(); } }

    /// <summary>剩余积分余额</summary>
    public double RemainingCredits { get => _remainingCredits; set { _remainingCredits = value; OnPropertyChanged(); } }

    /// <summary>视频赠送 5h 维度已用百分比（0-100），分母为 0 时为 0。</summary>
    public double VideoIntervalPercent { get => _videoIntervalPercent; set { _videoIntervalPercent = value; OnPropertyChanged(); } }

    /// <summary>视频赠送 周 维度已用百分比（0-100），分母为 0 时为 0。</summary>
    public double VideoWeeklyPercent { get => _videoWeeklyPercent; set { _videoWeeklyPercent = value; OnPropertyChanged(); } }

    /// <summary>用户设置：5h 限额进度条是否在卡片中显示（默认 true）。</summary>
    public bool Show5hBar { get => _show5hBar; set { _show5hBar = value; OnPropertyChanged(); } }

    /// <summary>用户设置：周限额进度条是否在卡片中显示（默认 true）。</summary>
    public bool ShowWeeklyBar { get => _showWeeklyBar; set { _showWeeklyBar = value; OnPropertyChanged(); } }

    /// <summary>用户设置：视频赠送 5h 进度条是否在卡片中显示（默认 true）。</summary>
    public bool ShowVideo5hBar { get => _showVideo5hBar; set { _showVideo5hBar = value; OnPropertyChanged(); } }

    /// <summary>用户设置：视频赠送 周 进度条是否在卡片中显示（默认 true）。</summary>
    public bool ShowVideoWeeklyBar { get => _showVideoWeeklyBar; set { _showVideoWeeklyBar = value; OnPropertyChanged(); } }

    /// <summary>该插件声明的渲染能力集合，供 XAML 决定是否呈现特定段落。</summary>
    public IReadOnlyList<string> RenderKinds
    {
        get => _renderKinds;
        set
        {
            _renderKinds = value ?? Array.Empty<string>();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 任务栏显示模式（影响任务栏窗口中的呈现样式）
    /// </summary>
    public TaskbarDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayModeText));
        }
    }

    /// <summary>用于下拉框显示的友好文本</summary>
    public string DisplayModeText => DisplayMode switch
    {
        TaskbarDisplayMode.Text => "文字",
        TaskbarDisplayMode.MiniLineChart => "折线图",
        TaskbarDisplayMode.RingChart => "圆环图",
        _ => "文字"
    };

    /// <summary>
    /// 主窗口卡片中展示的图表类型（None=仅进度条）。遗留的「单选」属性，仅为向后兼容保留；
    /// 新逻辑一律使用多选集合 <see cref="CardChartKinds"/> 驱动卡片图表区显隐。
    /// </summary>
    public CardChartKind CardChartKind
    {
        get => _cardChartKind;
        set
        {
            if (_cardChartKind == value) return;
            _cardChartKind = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 主窗口卡片中展示的图表类型「集合」（多选，空集合=仅进度条）。
    /// 由 PluginItemViewModel 负责持久化，这里驱动卡片图表区的显隐与各图表控件的按需叠加显示。
    /// </summary>
    public IReadOnlyList<CardChartKind> CardChartKinds
    {
        get => _cardChartKinds;
        set
        {
            _cardChartKinds = value ?? Array.Empty<CardChartKind>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCardChart));
        }
    }

    /// <summary>是否显示卡片图表区（多选集合非空时为 true）。</summary>
    public bool HasCardChart => _cardChartKinds != null && _cardChartKinds.Count > 0;

    /// <summary>
    /// 卡片折线图的数据序列。非 MiniMax（或 MiniMax API 模式）时跟随 <see cref="HistoryValues"/>（历史用量百分比，0-100）；
    /// MiniMax DOM 模式下由 <see cref="UpdateMiniMaxCharts"/> 替换为「每日 Token 用量」序列。
    /// </summary>
    public IReadOnlyList<double> CardLineValues
    {
        get => _cardLineValues;
        set { _cardLineValues = value ?? Array.Empty<double>(); OnPropertyChanged(); }
    }

    /// <summary>卡片折线图的 Y 轴最大值。用量百分比场景为 100；每日 Token 场景为区间最大值（自适应）。</summary>
    public double CardLineMax
    {
        get => _cardLineMax;
        set { _cardLineMax = value <= 0 ? 100 : value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 卡片热力图单元集合（GitHub 贡献图风格）。目前由 MiniMax DOM 模式按「每日 Token 用量」填充；
    /// 其它插件暂无逐日日历数据，保持为空（选中热力图时不显示内容）。
    /// </summary>
    public ObservableCollection<YearHeatMapCell> HeatMapCells { get; } = new();

    /// <summary>
    /// 历史已用百分比数据点（用于折线图绘制，0-100 数值）
    /// </summary>
    public IReadOnlyList<double> HistoryValues
    {
        get => _historyValues;
        set
        {
            _historyValues = value ?? Array.Empty<double>();
            OnPropertyChanged();
            // 非 MiniMax DOM 模式：卡片折线图跟随历史用量百分比（0-100）。
            // DOM 模式下折线图改用每日 Token，不能被历史百分比覆盖。
            if (!_isDomExtractMode)
            {
                CardLineValues = _historyValues;
                CardLineMax = 100;
                // req-007：非 DOM 模式无日期数据，清空 X 轴标签 / 周期切换 / tooltip 扩展。
                Dates = Array.Empty<string>();
                SupportsPeriodSwitch = false;
                ExtraTooltipLines = null;
            }
        }
    }

    // =====================================================================
    // req-007：折线图完整化属性与切换处理
    // =====================================================================

    /// <summary>折线图 X 轴日期标签（按数据点顺序）。</summary>
    public IReadOnlyList<string> Dates
    {
        get => _dates;
        set { _dates = value ?? Array.Empty<string>(); OnPropertyChanged(); }
    }

    /// <summary>折线图 hover tooltip 的扩展文本行（按换行拼接展示）。</summary>
    public IReadOnlyList<string>? ExtraTooltipLines
    {
        get => _extraTooltipLines;
        set { _extraTooltipLines = value; OnPropertyChanged(); }
    }

    // ============== REQ-083 SDK v2 新增可选属性（委托给 Provider） ==============

    /// <summary>
    /// Provider 注入的"V2 度量进度条组"数据（REQ-083）。
    /// 返回 null 时主窗口 <c>ChartCardTemplateSelector</c> 自动回退到旧 CardLimitBarsTemplate。
    /// </summary>
    public UsageMonitor.Core.Models.MetricBarData? CardMetricBarData => Provider?.CardMetricBarData;

    /// <summary>
    /// Provider 注入的"V2 度量数字网格"数据（REQ-083）。
    /// 返回 null 时主窗口 <c>ChartCardTemplateSelector</c> 自动回退到旧 CardBalanceTemplate。
    /// </summary>
    public UsageMonitor.Core.Models.MetricGridData? CardMetricGridData => Provider?.CardMetricGridData;

    /// <summary>
    /// Provider 注入的"V2 TooltipContent 生成委托"（REQ-083）。
    /// 返回 null 时主窗口沿用旧 ExtraTooltipLines 拼接逻辑。
    /// </summary>
    public System.Func<int, UsageMonitor.Core.Models.TooltipContent>? LineTooltipProvider => Provider?.LineTooltipProvider;

    /// <summary>req-034 修复：缓存命中率（0-100），供折线图 tooltip 显示。负值表示无数据。</summary>
    public double CacheHitPercent
    {
        get => _cacheHitPercent;
        set { _cacheHitPercent = value; OnPropertyChanged(); }
    }
    private double _cacheHitPercent = -1;

    /// <summary>req-034 修复：每独立的缓存命中率集合（与 CardLineValues 等长）。</summary>
    public IReadOnlyList<double> DailyCacheHitPercents
    {
        get => _dailyCacheHitPercents;
        set { _dailyCacheHitPercents = value ?? Array.Empty<double>(); OnPropertyChanged(); }
    }
    private IReadOnlyList<double> _dailyCacheHitPercents = Array.Empty<double>();

    /// <summary>插件是否声明支持周期切换（req-007）。为 true 时卡片折线图右上角显示「近 7 天 / 近 30 天」按钮。</summary>
    public bool SupportsPeriodSwitch
    {
        get => _supportsPeriodSwitch;
        set { _supportsPeriodSwitch = value; OnPropertyChanged(); }
    }

    /// <summary>当前周期（req-007）。</summary>
    public string CurrentPeriod
    {
        get => _currentPeriod;
        set
        {
            var v = value ?? UsageMonitor.App.Controls.ChartPeriods.Week;
            if (_currentPeriod == v) return;
            _currentPeriod = v;
            OnPropertyChanged();
        }
    }

    /// <summary>是否处于加载态（req-007）。为 true 时控件半透明 + 中央“加载中...”文字 + 按钮变灰。</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// req-072 U-05：卡片详情展开状态（默认折叠）。
    /// 用于 Expander 绑定，控制限额/余额/图表等次要信息的显示。
    /// </summary>
    public bool IsDetailExpanded
    {
        get => _isDetailExpanded;
        set
        {
            if (_isDetailExpanded == value) return;
            _isDetailExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// req-026：当前 Provider 启用的环形图中心 metric key 集合。
    /// <para>绑定到 RingChartControl.EnabledMetrics，由 <c>MainViewModel.BuildProviderRingChartMetricGroups</c>
    /// 在用户勾选变更时通过 <c>SyncProviderEnabledMetricsToVm</c> 同步。null / 空集合表示“全部启用”，
    /// 控件会沿用旧行为不显灰。</para>
    /// </summary>
    public IReadOnlyList<string> EnabledRingChartMetrics
    {
        get => _enabledRingChartMetrics;
        set
        {
            if (ReferenceEquals(_enabledRingChartMetrics, value)) return;
            if (value == null) value = Array.Empty<string>();
            _enabledRingChartMetrics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnabledRingChartMetricsOrNull));
        }
    }

    /// <summary>
    /// req-026：XAML 绑定辅助——把 null 转成"全部启用"占位的 null（让 RingChartControl 默认启灰判断）。
    /// <para>vs <see cref="EnabledRingChartMetrics"/>：返回 null 时让控件认为"未配置"，保留旧行为。</para>
    /// </summary>
    public IReadOnlyList<string>? EnabledRingChartMetricsOrNull
    {
        get => _enabledRingChartMetrics.Count == 0 ? null : _enabledRingChartMetrics;
        set { /* 仅供 XAML setter 调用，实际写入见 EnabledRingChartMetrics */ }
    }

    /// <summary>
    /// 插件提供者引用（req-007）：为当前 VM 关联的插件 provider，用于 SetPeriodAsync 调用。
    /// 在 MainViewModel 装配时注入，刷新流程不会修改。
    /// </summary>
    public UsageMonitor.Core.Plugins.IUsageProvider? Provider
    {
        get => _provider;
        set
        {
            _provider = value;
            // REQ-083：Provider 变更时通知 V2 数据属性，让 ChartCardTemplateSelector 重新选模板。
            OnPropertyChanged();
            OnPropertyChanged(nameof(CardMetricBarData));
            OnPropertyChanged(nameof(CardMetricGridData));
            OnPropertyChanged(nameof(LineTooltipProvider));
        }
    }
    private UsageMonitor.Core.Plugins.IUsageProvider? _provider;

    /// <summary>
    /// req-007：处理 MiniLineChartControl.PeriodChanged 事件。
    /// <para>
    /// 1) 调插件 <c>SetPeriodAsync</c>（默认 no-op，仅记 log）；2) 切换 <see cref="IsLoading"/>=true；
    /// 3) 按 <see cref="CurrentPeriod"/> 在已缓存的 <c>_fullDailyValues</c>/<c>_fullDailyDates</c> 上重新切片到
    ///    <see cref="CardLineValues"/> 与 <see cref="Dates"/>；4) 关 IsLoading。
    /// 之所以不调 GetUsageAsync：usage_summary 返回的是最多 168 天的历史数据，周期切换不需要重新拉接口。
    /// </para>
    /// </summary>
    public void HandlePeriodChanged(string period)
    {
        CurrentPeriod = period;
        IsLoading = true;
        try
        {
            // 通知插件（默认 no-op：MiniMax 重写为记录 period；其他插件保持原状）
            var provider = Provider;
            if (provider != null)
            {
                _ = provider.SetPeriodAsync(period);
            }

            // 按周期切片缓存的完整数据到折线图
            SliceCardLineByPeriod(period);
        }
        catch (Exception ex)
        {
            // req-031：捕获切片/插件异常，避免冒泡到 UI 线程导致闪退
            UsageMonitor.Core.Services.FileLogger.Warn("ProviderUsageViewModel",
                $"HandlePeriodChanged({period}) failed for {ProviderId}: {ex.Message}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// req-007：根据 period 在 <c>_fullDailyValues</c> 与 <c>_fullDailyDates</c> 上切片并写入
    /// <see cref="CardLineValues"/> / <see cref="CardLineMax"/> / <see cref="Dates"/>。
    /// <para>
    /// 窗口取后 N 天（N = <see cref="UsageMonitor.App.Controls.ChartPeriods.ToDays"/>）；
    /// 数据不足时使用全部；空数据时清空。
    /// </para>
    /// </summary>
    private void SliceCardLineByPeriod(string period)
    {
        var values = _fullDailyValues;
        var dates = _fullDailyDates;
        if (values == null || values.Count == 0)
        {
            CardLineValues = Array.Empty<double>();
            CardLineMax = 1;
            Dates = Array.Empty<string>();
            return;
        }

        var days = UsageMonitor.App.Controls.ChartPeriods.ToDays(period);
        var take = Math.Min(days, values.Count);
        var start = values.Count - take;

        var sliced = new double[take];
        for (int i = 0; i < take; i++) sliced[i] = values[start + i];

        CardLineValues = sliced;

        // Y 轴最大值：取窗口内最大值（至少 1，避免全零平线）
        double max = 0;
        for (int i = 0; i < take; i++) if (sliced[i] > max) max = sliced[i];
        CardLineMax = max > 0 ? max : 1;

        // Dates：仅在「完整数据提供日期」时同步切片；其他插件 Dates 为空。
        if (dates != null && dates.Count == values.Count && take > 0)
        {
            var slicedDates = new string[take];
            for (int i = 0; i < take; i++) slicedDates[i] = dates[start + i];
            Dates = slicedDates;
        }
        else
        {
            Dates = Array.Empty<string>();
        }

        // req-034 修复：缓存命中率同步切片（与 values/dates 同逻辑，取后 take 个）
        var fullCacheHit = _fullDailyCacheHitPercents;
        if (fullCacheHit != null && fullCacheHit.Count == values.Count && take > 0)
        {
            var slicedCacheHit = new double[take];
            for (int i = 0; i < take; i++) slicedCacheHit[i] = fullCacheHit[start + i];
            DailyCacheHitPercents = slicedCacheHit;
        }
        else
        {
            DailyCacheHitPercents = Array.Empty<double>();
        }
    }

    /// <summary>
    /// 根据 UsageInfo 更新显示数据
    /// </summary>
    public void UpdateFromUsage(UsageInfo usage)
    {
        IsError = !usage.IsSuccess;
        ErrorMessage = usage.ErrorMessage;
        LastUpdateText = usage.LastUpdated.ToString("HH:mm:ss");

        if (!usage.IsSuccess)
        {
            StatusText = "查询失败";

            // MiniMax 插件错误引导：根据错误类型显示不同提示
            if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase))
            {
                var msg = usage.ErrorMessage ?? "";

                // 1. 完全未配置 Key（提示中包含"Token Plan 订阅 Key"或"填写"）
                if (msg.Contains("Token Plan 订阅 Key") || msg.Contains("填写"))
                {
                    StatusText = "请进入设置界面来配置 Token Plan 订阅 Key";
                }
                // 2. 1004 鉴权失败 / login fail
                else if (msg.Contains("1004") || msg.Contains("login fail"))
                {
                    StatusText = "Token Plan 订阅 Key 无效，请进入设置检查";
                }
                // 3. 其他错误：显示通用引导
                else
                {
                    StatusText = "请进入设置界面来配置 MiniMax 的权限";
                }
            }

            // req-008：失败场景也要把余额快照重置为 4 个默认占位项，避免显示上一次成功的旧值。
            UpdateBalanceFromExtra(usage.Extra ?? new Dictionary<string, object>());
            return;
        }

        UsagePercentage = usage.GetUsagePercentage();

        // MiniMax 通过 DOM/网页 API 抓取的用量：走专用渲染分支。
        if (usage.Extra != null && usage.Extra.TryGetValue("domExtract", out var deFlag)
            && deFlag is bool deb && deb)
        {
            UpdateFromMiniMaxDom(usage);
            return;
        }

        // 走到这里说明不是 MiniMax DOM 抓取数据：回到「历史用量百分比」折线图模式。
        _isDomExtractMode = false;
        CardLineValues = _historyValues;
        CardLineMax = 100;

        if (usage.TotalAmount > 0)
        {
            UsedText = $"{usage.UsedAmount:F2} {usage.Unit}";
            TotalText = $"{usage.TotalAmount:F2} {usage.Unit}";
            RemainingText = $"{usage.GetRemainingAmount():F2} {usage.Unit}";
            StatusText = $"{usage.GetUsagePercentage():F1}% 已使用";
        }
        else if (usage.UsedTokens > 0)
        {
            UsedText = FormatTokens(usage.UsedTokens);
            TotalText = usage.TotalTokens > 0 ? FormatTokens(usage.TotalTokens) : "不限";
            RemainingText = usage.TotalTokens > 0 ? FormatTokens(usage.GetRemainingTokens()) : "--";
            StatusText = usage.TotalTokens > 0
                ? $"{usage.GetUsagePercentage():F1}% 已使用"
                : $"已用 {UsedText}";
        }
        else
        {
            StatusText = "暂无数据";
        }

        // 解析 Extra 中由 MiniMaxBalanceFetcher 填入的余额/账单快照
        // 其他插件不需处理（Extra 为空字典）
        if (usage.Extra != null)
            UpdateBalanceFromExtra(usage.Extra);
    }

    /// <summary>
    /// 渲染 MiniMax 通过 DOM/网页 API 抓取的用量到卡片。
    /// 数据来源：MiniMaxDomExtractor 写入 UsageInfo.Extra 的 mm_* 键
    /// （5h/周/视频的用量百分比、重置时间、订阅档位、积分、调用汇总）。
    /// </summary>
    private void UpdateFromMiniMaxDom(UsageInfo usage)
    {
        var extra = usage.Extra!;
        _isDomExtractMode = true;

        // 小工具：容错读取 double / long / string（Extra 值为 object 装箱）。
        double D(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToDouble(v) : -1;
        long L(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToInt64(v) : 0;
        string S(string k) => extra.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

        // 1. 渲染需求（mm_render_kinds）传给 XAML，用于控制“订阅胶囊/5h/周进度条/汇总面板”是否呈现。
        if (extra.TryGetValue("mm_render_kinds", out var rk) && rk is IEnumerable<string> kinds)
        {
            RenderKinds = kinds.ToArray();
        }
        else if (rk is IEnumerable<object> kindsObj)
        {
            // JsonElement 数组等也会实现 IEnumerable，兼容转换。
            RenderKinds = kindsObj.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToArray();
        }
        else
        {
            RenderKinds = Array.Empty<string>();
        }

        // 2. 订阅档位胶囊。
        IsSubscriptionActive = extra.TryGetValue("mm_subscriptionActive", out var sa) && sa is bool sab && sab;
        var subTitle = S("mm_subscriptionTitle");
        SubscriptionTitle = !string.IsNullOrWhiteSpace(subTitle)
            ? $"Token Plan · {subTitle}"
            : "Token Plan 订阅";

        // 3. 5h 限额进度条（主进度条，绿色主题）。
        var p5 = D("mm_5hUsedPercent");
        PrimaryBarPercent = p5 >= 0 ? Math.Min(100, p5) : 0;
        UsagePercentage = PrimaryBarPercent; // 遗留逻辑：保留卡片顶部主进度条的取值
        PrimaryResetText = BuildRemainText(extra, "mm_5hResetAt");
        // req-028：同时保存 mm_5hResetAt 原始 DateTime，供托盘/悬浮窗倒计时 + 到时自动刷新。
        Next5hResetAt = extra.TryGetValue("mm_5hResetAt", out var ra) && ra is DateTime radt ? radt : null;

        // 4. 周限额进度条。
        var pw = D("mm_weeklyUsedPercent");
        WeeklyBarPercent = pw >= 0 ? Math.Min(100, pw) : 0;
        WeeklyResetText = BuildRemainText(extra, "mm_weeklyResetAt");

        // 5. 状态行（保留 StatusText，包含 5h + 周概要）。
        StatusText = (PrimaryBarPercent > 0 || WeeklyBarPercent > 0)
            ? $"5h 已用 {PrimaryBarPercent:0}% · 本周 已用 {WeeklyBarPercent:0}%"
            : "已登录";

        // 6. 视频赠送 5h + 周。
        var v5Used = L("mm_videoIntervalUsed");
        var v5Total = L("mm_videoIntervalTotal");
        VideoQuotaText = v5Total > 0 ? $"{v5Used}/{v5Total}" : "--";
        VideoIntervalPercent = v5Total > 0 ? Math.Min(100, 100.0 * v5Used / v5Total) : 0;
        var vwUsed = L("mm_videoWeeklyUsed");
        var vwTotal = L("mm_videoWeeklyTotal");
        VideoWeeklyText = vwTotal > 0 ? $"{vwUsed}/{vwTotal}" : "--";
        VideoWeeklyPercent = vwTotal > 0 ? Math.Min(100, 100.0 * vwUsed / vwTotal) : 0;

        // 7. 卡片顶一行的 “已使用 / 总额度 / 剩余额度” 仍保留供未适配卡片主题的插件使用；
        //    本插件额外叠加为信息性文本，不会被三列 UI 误读。
        UsedText = PrimaryBarPercent > 0 ? $"{PrimaryBarPercent:0}%" : "--";
        TotalText = "100%";
        RemainingText = $"{Math.Max(0, 100 - PrimaryBarPercent):0}%";

        // 8. 汇总面板（req-008：多列布局的 BalanceItem 集合）。
        // 默认 4 项：累计 / 峰值 / 活跃 / 积分余额。每项 Value / Detail 由 mm_* 字段填充。
        // 插件可通过 provider.BalanceItems 按 Label 覆盖同名项 / 追加额外项 / 隐藏默认项。
        var credits = D("mm_remainingCredits");
        RemainingCredits = credits;
        var totalTokens = S("mm_totalTokens");
        var activeDays = L("mm_activeDays");
        var totalDays = L("mm_totalDays");
        var mostActive = S("mm_mostActiveDay"); // 格式 "2026-07-01 (552.49M)"
        // req-047：提取排名百分比（mm_rankingPercent），用于显示"前X%"
        double? rankingPercent = null;
        if (extra.TryGetValue("mm_rankingPercent", out var rp) && rp is double rpVal && rpVal > 0)
            rankingPercent = rpVal;
        // 订阅到期时间（仅在已订阅时拼接）
        DateTime? subscriptionEnd = null;
        if (IsSubscriptionActive && extra.TryGetValue("mm_subscriptionEndTime", out var se) && se is DateTime sed)
            subscriptionEnd = sed;

        RebuildBalanceItems(totalTokens, mostActive, activeDays, totalDays, credits, subscriptionEnd, rankingPercent);

        // 9. 折线图 / 热力图：用「每日 Token 用量」填充卡片图表数据。
        UpdateMiniMaxCharts(extra);
    }

    /// <summary>
    /// req-008：组装余额快照多列数据。先拼默认 4 项（累计 / 峰值 / 活跃 / 积分余额），
    /// 再按 <c>Label</c> 与插件 <see cref="IUsageProvider.BalanceItems"/> 合并：同名项插件胜出，
    /// 未匹配项追加在默认项之后。插件项 <c>IsVisible=false</c> 可隐藏默认项。
    /// </summary>
    /// <param name="rankingPercent">req-047：用量排名百分比，非空时显示"前X%"作为累计的 Detail。</param>
    private void RebuildBalanceItems(
        string totalTokens, string mostActive, long activeDays, long totalDays,
        double credits, DateTime? subscriptionEnd, double? rankingPercent)
    {
        // 默认 4 项
        var defaults = new List<UsageMonitor.Core.Models.BalanceItem>
        {
            // req-047：累计项的 Detail 显示排名（"前X%"），无排名时不显示
            new() {
                Label = "累计",
                Value = string.IsNullOrEmpty(totalTokens) ? "--" : totalTokens,
                Detail = rankingPercent.HasValue ? $"前{rankingPercent.Value:0}%" : null
            },
            new() { Label = "峰值", Value = ExtractMostActiveToken(mostActive), Detail = ExtractMostActiveDate(mostActive) },
            new() { Label = "活跃", Value = totalDays > 0 ? $"{activeDays}/{totalDays}天" : "--" },
            new()
            {
                Label = "积分余额",
                Value = credits > 0 ? $"{credits:N0}" : "暂无积分",
                Detail = subscriptionEnd.HasValue ? $"续期至 {subscriptionEnd.Value:yyyy-MM-dd}" : null
            }
        };

        // 插件覆盖/追加
        var provider = Provider;
        if (provider != null)
        {
            foreach (var pluginItem in provider.BalanceItems)
            {
                var existing = defaults.FirstOrDefault(d => string.Equals(d.Label, pluginItem.Label, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // 覆盖默认：保留默认项的 INPC 引用（同 Label），仅刷值
                    if (!string.IsNullOrEmpty(pluginItem.Value)) existing.Value = pluginItem.Value;
                    if (pluginItem.Detail != null) existing.Detail = pluginItem.Detail;
                    if (!pluginItem.IsVisible) existing.IsVisible = false;
                }
                else
                {
                    defaults.Add(new UsageMonitor.Core.Models.BalanceItem
                    {
                        Label = pluginItem.Label,
                        Value = pluginItem.Value ?? "--",
                        Detail = pluginItem.Detail,
                        IsVisible = pluginItem.IsVisible
                    });
                }
            }
        }

        // 标记末项并写入集合：IsLast 决定 XAML 端是否显示该列右侧的 1px 分隔线。
        for (int i = 0; i < defaults.Count; i++) defaults[i].IsLast = (i == defaults.Count - 1);
        _balanceItems.Clear();
        foreach (var item in defaults) _balanceItems.Add(item);
        OnPropertyChanged(nameof(BalanceItems));
    }

    /// <summary>从 <c>mm_mostActiveDay</c> 字符串中提取数值部分（如"2026-07-01 (552.49M)" → "552.49M"）。</summary>
    private static string ExtractMostActiveToken(string? mostActive)
    {
        if (string.IsNullOrEmpty(mostActive)) return "--";
        var open = mostActive.IndexOf('(');
        var close = mostActive.IndexOf(')');
        if (open < 0 || close <= open) return "--";
        return mostActive.Substring(open + 1, close - open - 1).Trim();
    }

    /// <summary>从 <c>mm_mostActiveDay</c> 字符串中提取日期部分（如"2026-07-01 (552.49M)" → "2026-07-01"）。</summary>
    private static string? ExtractMostActiveDate(string? mostActive)
    {
        if (string.IsNullOrEmpty(mostActive)) return null;
        var open = mostActive.IndexOf('(');
        if (open <= 0) return null;
        return mostActive.Substring(0, open).Trim();
    }

    /// <summary>
    /// 用 MiniMax DOM 抓取的「每日 Token 用量」填充卡片折线图与热力图数据。
    /// <para>
    /// 数据来源：<c>mm_dailyTokenValues</c>（每日 token，按日期升序）与 <c>mm_dailyTokenDates</c>
    /// （对应 yyyy-MM-dd，可能缺省）。折线图取当前周期窗口内的趋势（req-007：默认 7 天，可切换为 30 天），
    /// Y 轴自适应为窗口内最大值；热力图为每个 token&gt;0 的日期生成一个单元（颜色按相对峰值的强度分三档），
    /// 缺日期时按「最后一个点=今天」向前推断日历。完整数据同时缓存到 <c>_fullDailyValues</c> 与
    /// <c>_fullDailyDates</c>，供 <see cref="HandlePeriodChanged"/> 按新周期重新切片。
    /// </para>
    /// </summary>
    private void UpdateMiniMaxCharts(Dictionary<string, object> extra)
    {
        // 读取每日 token 数值序列（Extra 内存直传，值为 List<long>；兼容其它可枚举形态）。
        var values = ReadLongList(extra, "mm_dailyTokenValues");
        var dates = ReadStringList(extra, "mm_dailyTokenDates");
        // req-034 修复：读取每独立的缓存命中率（提前读取，供热力图和折线图共用）
        var dailyCacheHitPercents = ReadDoubleList(extra, "mm_dailyCacheHitPercents");

        // req-007：缓存完整数据，供 PeriodChanged 重新切片。values 在这里就已经是完整升序序列。
        _fullDailyValues = values;
        _fullDailyDates = dates;
        // req-034 修复：缓存完整缓存命中率数据，供 SliceCardLineByPeriod 按周期切片
        _fullDailyCacheHitPercents = dailyCacheHitPercents;

        // req-007：把插件声明的周期切换能力、tooltip 扩展行推到 UI。
        SupportsPeriodSwitch = false; // 占位默认值，循环结束后会被插件实际声明覆盖。
        var provider = Provider;
        if (provider != null)
        {
            SupportsPeriodSwitch = provider.SupportsPeriodSwitch;
            ExtraTooltipLines = provider.ExtraTooltipLines;
        }

        // 折线图：按当前周期（默认 7d）取窗口内数据，Y 轴自适应为该区间最大值（至少 1，避免除零/全零全平）。
        if (values.Count >= 2)
        {
            var days = UsageMonitor.App.Controls.ChartPeriods.ToDays(CurrentPeriod);
            var take = Math.Min(days, values.Count);
            var start = values.Count - take;
            var recent = new List<double>(take);
            for (int i = start; i < values.Count; i++) recent.Add(values[i]);
            CardLineValues = recent;
            double max = 0;
            foreach (var r in recent) if (r > max) max = r;
            CardLineMax = max > 0 ? max : 1;

            // req-007：X 轴 Dates 同步到窗口（仅在 dates 与 values 同长度时）。
            // ReadStringList 永远返回非 null List，dates 本地变量也不需要 null 检查。
            if (dates.Count == values.Count)
            {
                var slicedDates = new string[take];
                for (int i = 0; i < take; i++) slicedDates[i] = dates[start + i];
                Dates = slicedDates;

                // req-034 修复：每独立的缓存命中率同步切片
                if (dailyCacheHitPercents.Count == values.Count)
                {
                    var slicedCacheHit = new double[take];
                    for (int i = 0; i < take; i++) slicedCacheHit[i] = dailyCacheHitPercents[start + i];
                    DailyCacheHitPercents = slicedCacheHit;
                }
                else
                {
                    DailyCacheHitPercents = Array.Empty<double>();
                }
            }
            else
            {
                Dates = Array.Empty<string>();
                DailyCacheHitPercents = Array.Empty<double>();
            }
        }
        else
        {
            CardLineValues = Array.Empty<double>();
            CardLineMax = 1;
            Dates = Array.Empty<string>();
        }

        // 热力图（req-009 + req-021）：为每个 token 日期生成单元，背景色按 token 绝对值走 HeatMapTierScale 选档；
        // token<=0 显式归一为“无用量”色（#f3f4f6），避免热力图色阶表后续变更导致浅红误着色。
        // 静态冻结 brush（跨线程安全）。
        var zeroBrush = FreezeBrush(0xF3, 0xF4, 0xF6);
        HeatMapCells.Clear();
        if (values.Count > 0)
        {
            // 从 extras 读缓存命中率（mm_cacheHitPercent）；为负或缺失时 ComparisonText 留空。
            double cacheHitPercent = -1;
            if (extra.TryGetValue("mm_cacheHitPercent", out var chp) && chp != null)
            {
                try { cacheHitPercent = Convert.ToDouble(chp); }
                catch { cacheHitPercent = -1; }
            }
            // req-034 修复：存储缓存命中率供折线图 tooltip 使用（属性 setter 会同时更新字段并触发 INPC）
            CacheHitPercent = cacheHitPercent;

            for (int i = 0; i < values.Count; i++)
            {
                var token = values[i];
                // 日期：优先用真实 date；缺失时按「最后一个点=今天」向前推。
                string day = i < dates.Count && !string.IsNullOrEmpty(dates[i])
                    ? dates[i]
                    : DateTime.Today.AddDays(-(values.Count - 1 - i)).ToString("yyyy-MM-dd");
                // 与原逻辑保留 percent（为兼容 ProgressToBrush 等历史用法）—— 选 token 绝对值 1.5% 为峰值。
                long peak = 0;
                foreach (var v in values) if (v > peak) peak = v;
                if (peak <= 0) peak = 1;
                double percent = Math.Min(100.0, 100.0 * token / peak);

                // req-021：token<=0 强制显 “无用量”色（与色阶表首档颜色一致，但与未来修改脱耦）。
                var bgBrush = token > 0
                    ? UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush(token, "MiniMax")
                    : zeroBrush;

                // req-034 修复：使用每独立的缓存命中率
                double dayCacheHit = i < dailyCacheHitPercents.Count ? dailyCacheHitPercents[i] : -1;

                HeatMapCells.Add(new YearHeatMapCell
                {
                    Day = day,
                    Percent = percent,
                    Token = token, // req-009：供 RecolorHeatMapCells 重算背景色
                    Background = bgBrush,
                    ValueText = token > 0 ? FormatTokens(token) : "--",
                    Unit = "",
                    ComparisonText = dayCacheHit >= 0
                        ? $"缓存命中 {dayCacheHit:0.00}%"
                        : string.Empty
                });
            }
        }
    }

    /// <summary>从 Extra 读取 List&lt;long&gt;（兼容 List&lt;long&gt; 与其它可枚举装箱），失败返回空列表。</summary>
    private static List<long> ReadLongList(Dictionary<string, object> extra, string key)
    {
        var result = new List<long>();
        if (!extra.TryGetValue(key, out var v) || v == null) return result;
        if (v is List<long> ll) return ll;
        if (v is string) return result; // string 也是 IEnumerable，先排除
        if (v is System.Collections.IEnumerable en)
        {
            foreach (var item in en)
            {
                if (item == null) continue;
                if (long.TryParse(item.ToString(), out var n)) result.Add(n);
            }
        }
        return result;
    }

    /// <summary>从 Extra 读取 List&lt;string&gt;（兼容 List&lt;string&gt; 与其它可枚举装箱），失败返回空列表。</summary>
    private static List<string> ReadStringList(Dictionary<string, object> extra, string key)
    {
        var result = new List<string>();
        if (!extra.TryGetValue(key, out var v) || v == null) return result;
        if (v is List<string> ls) return ls;
        if (v is string) return result;
        if (v is System.Collections.IEnumerable en)
        {
            foreach (var item in en)
                result.Add(item?.ToString() ?? "");
        }
        return result;
    }

    /// <summary>req-034 修复：从 Extra 读取 List&lt;double&gt;（兼容 List&lt;double&gt; 与其它可枚举装箱），失败返回空列表。</summary>
    private static List<double> ReadDoubleList(Dictionary<string, object> extra, string key)
    {
        var result = new List<double>();
        if (!extra.TryGetValue(key, out var v) || v == null) return result;
        if (v is List<double> ld) return ld;
        if (v is System.Collections.IEnumerable en)
        {
            foreach (var item in en)
            {
                if (item is double d) result.Add(d);
                else if (item != null && double.TryParse(item.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    result.Add(parsed);
            }
        }
        return result;
    }

    // req-009：旧按百分比的 3 档映射（HeatLow / HeatMid / HeatHigh）已被 HeatMapTierScale 取代，
    // 后者按 token 绝对值分档（默认 MiniMax 6 档：0/20M/100M/200M/300M）。
    // 所有调用点（UpdateMiniMaxCharts / RecolorHeatMapCells）已改为走 HeatMapTierScale.ResolveBrush。
    // 旧字段（HeatLow/Mid/High）已删除，避免误用。

    /// <summary>创建并冻结一个 SolidColorBrush（frozen 后可安全跨线程绑定到 UI）。</summary>
    private static System.Windows.Media.Brush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 根据 Extra 中的 DateTime 重置时间生成“X 小时 X 分钟后重置”形式的文案，
    /// 与用量页前端 o(remains_time) 逻辑保持一致。
    /// </summary>
    /// <param name="extra">Extra 字典</param>
    /// <param name="key">超时字段名（"mm_5hResetAt" / "mm_weeklyResetAt"）</param>
    private static string BuildRemainText(Dictionary<string, object> extra, string key)
    {
        if (!extra.TryGetValue(key, out var v) || v is not DateTime dt) return "--";
        var diff = dt - DateTime.Now;
        if (diff.TotalMinutes < 0) return "即将重置";

        // 参考 JS 函数 o(remains_time)：1 天以上显示"X 天 后重置"，1 小时以上显示"X 小时 Y 分钟后重置"，否则只显示分钟。
        if (diff.TotalDays >= 1)
        {
            var d = (int)Math.Floor(diff.TotalDays);
            var h = diff.Hours;
            return h > 0 ? $"{d} 天 {h} 小时后重置" : $"{d} 天后重置";
        }
        if (diff.TotalHours >= 1)
        {
            var h = (int)Math.Floor(diff.TotalHours);
            var m = diff.Minutes;
            return m > 0 ? $"{h} 小时 {m} 分钟后重置" : $"{h} 小时后重置";
        }
        var mins = Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes));
        return $"{mins} 分钟后重置";
    }

    /// <summary>
    /// req-008：把账户余额/账单抓取器（MiniMaxBalanceFetcher）的结果组装到余额快照的 BalanceItems 集合。
    /// <para>
    /// 场景区分：<c>balanceFetcherStatus</c> 非空时说明本次抓取过，组装 4 个默认项 + 1 个"账户余额"追加项；
    /// 非 MiniMax / 余额抓取未启用时直接组装 4 个默认占位项。
    /// 不再使用 <c>HasBalanceInfo</c> 折叠控制（需求要求"模块永远显示"）。
    /// </para>
    /// </summary>
    private void UpdateBalanceFromExtra(Dictionary<string, object> extra)
    {
        // 1) 总是先拼默认 4 项（累计 / 峰值 / 活跃 / 积分余额）—— 非 MiniMax 场景下值都是 "--" 占位。
        var defaultItems = new List<UsageMonitor.Core.Models.BalanceItem>
        {
            new() { Label = "累计", Value = "--" },
            new() { Label = "峰值", Value = "--" },
            new() { Label = "活跃", Value = "--" },
            new() { Label = "积分余额", Value = "暂无积分" }
        };

        if (extra == null || extra.Count == 0)
        {
            ReplaceBalanceItems(defaultItems);
            return;
        }

        // 2) 状态指示：balanceFetcherStatus - 仅 MiniMax 余额抓取场景会写入。
        var status = extra.TryGetValue("balanceFetcherStatus", out var sObj) ? sObj?.ToString() : null;
        if (string.IsNullOrEmpty(status))
        {
            // 非余额抓取场景（普通刷新 / API 模式），直接用默认项。
            ReplaceBalanceItems(defaultItems);
            return;
        }

        // 3) MiniMax 余额抓取场景：组装 1 个追加项"账户余额"，由数据源决定 Value。
        string value;
        string? detail = null;
        if (extra.TryGetValue("accountIntervalRemaining", out var irObj) &&
            extra.TryGetValue("accountIntervalTotal", out var itObj))
        {
            var remain = Convert.ToInt64(irObj);
            var total = Convert.ToInt64(itObj);
            value = $"{remain:N0} / {total:N0}";
            detail = "5h窗口剩余";
        }
        else if (extra.TryGetValue("accountWeeklyRemaining", out var wrObj) &&
                 extra.TryGetValue("accountWeeklyTotal", out var wtObj))
        {
            var remain = Convert.ToInt64(wrObj);
            var total = Convert.ToInt64(wtObj);
            value = $"{remain:N0} / {total:N0}";
            detail = "周窗口剩余";
        }
        else if (status == "no_cookie")
        {
            value = "未登录";
        }
        else
        {
            value = "暂不可用";
        }

        // 拼接额外上下文（重置时间 / 快照文件名 / 错误消息），多行以换行分隔。
        var sb = new System.Text.StringBuilder(detail);
        if (extra.TryGetValue("accountIntervalEndAt", out var endObj) && endObj is DateTime endAt)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"重置于 {endAt:HH:mm}");
        }
        if (extra.TryGetValue("balancePageSnapshotPath", out var pathObj))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"快照: {Path.GetFileName(pathObj?.ToString() ?? "")}");
        }
        if (status == "no_cookie" && extra.TryGetValue("balanceFetcherMessage", out var msgObj))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(msgObj);
        }

        defaultItems.Add(new UsageMonitor.Core.Models.BalanceItem
        {
            Label = "账户余额",
            Value = value,
            Detail = sb.Length > 0 ? sb.ToString() : null
        });

        ReplaceBalanceItems(defaultItems);
    }

    /// <summary>req-008：原子替换 BalanceItems 集合并通知 UI 刷新。同时标记末项 <c>IsLast=true</c> 让 XAML 隐藏其右侧分隔线。</summary>
    private void ReplaceBalanceItems(IEnumerable<UsageMonitor.Core.Models.BalanceItem> items)
    {
        var list = items as IList<UsageMonitor.Core.Models.BalanceItem> ?? items.ToList();
        _balanceItems.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            list[i].IsLast = (i == list.Count - 1);
            _balanceItems.Add(list[i]);
        }
        OnPropertyChanged(nameof(BalanceItems));
    }

    private static string FormatTokens(long count)
    {
        if (count < 0) return "不限";
        if (count >= 1_000_000) return $"{count / 1_000_000.0:F2}M";
        if (count >= 1_000) return $"{count / 1_000.0:F2}K";
        return count.ToString();
    }

    /// <summary>
    /// 重发出本 VM 所有进度条 Percent 属性的 PropertyChanged。
    /// 供全局色阶变更后让 XAML 上的 PercentToBrushConverter 重新取色使用。
    /// </summary>
    public void RefreshAllPercentProperties()
    {
        OnPropertyChanged(nameof(PrimaryBarPercent));
        OnPropertyChanged(nameof(WeeklyBarPercent));
        OnPropertyChanged(nameof(VideoIntervalPercent));
        OnPropertyChanged(nameof(VideoWeeklyPercent));
    }

    /// <summary>
    /// 重着色本卡片的"每日 Token"热力图单元（按当前 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale"/> 色阶）。
    /// <para>
    /// req-009：按每个 cell 缓存的 <c>Token</c> 走 HeatMapTierScale.ResolveBrush 重算背景；
    /// providerId 用本卡片的 <c>ProviderId</c>（如 "MiniMax"），未声明时走通用 4 档兑底。
    /// </para>
    /// </summary>
    public void RecolorHeatMapCells()
    {
        var pid = ProviderId;
        foreach (var cell in HeatMapCells)
        {
            cell.Background = UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush(cell.Token, pid);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 插件列表项视图模型 - 包含插件信息、启用状态和配置命令
/// </summary>
public class PluginItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private TaskbarDisplayMode _displayMode = TaskbarDisplayMode.Text;
    private readonly ConfigService _configService;
    private readonly IUsageProvider _provider;

    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    /// <summary>任务栏显示模式（与 ProviderUsageViewModel.DisplayMode 同步）</summary>
    public TaskbarDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayModeText));
        }
    }

    /// <summary>显示模式下拉框的友好文本</summary>
    public string DisplayModeText => DisplayMode switch
    {
        TaskbarDisplayMode.Text => "文字",
        TaskbarDisplayMode.MiniLineChart => "折线图",
        TaskbarDisplayMode.RingChart => "圆环图",
        _ => "文字"
    };

    /// <summary>所有可选模式（用于 ComboBox 绑定）</summary>
    public IReadOnlyList<KeyValuePair<TaskbarDisplayMode, string>> AvailableDisplayModes { get; } = new[]
    {
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.Text, "文字"),
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.MiniLineChart, "折线图"),
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.RingChart, "圆环图"),
    };

    private IReadOnlyList<CardChartKind> _cardChartKinds = Array.Empty<CardChartKind>();

    /// <summary>
    /// 卡片图表类型「集合」（多选）。设置时写入配置并保存；由 MainViewModel 订阅同步到对应的 ProviderUsageViewModel。
    /// </summary>
    public IReadOnlyList<CardChartKind> CardChartKinds
    {
        get => _cardChartKinds;
        set
        {
            _cardChartKinds = value ?? Array.Empty<CardChartKind>();
            _configService.SetProviderCardChartKinds(ProviderId, _cardChartKinds);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 初始化卡片图表多选（仅设置字段并通知，不触发持久化）。供启动装配时回显已保存/迁移的选择，
    /// 避免逐插件重复写盘（迁移得到的值会随后续任一配置变更一并持久化）。
    /// </summary>
    public void InitCardChartKinds(IReadOnlyList<CardChartKind> kinds)
    {
        _cardChartKinds = kinds ?? Array.Empty<CardChartKind>();
        OnPropertyChanged(nameof(CardChartKinds));
    }

    /// <summary>插件定义的配置字段列表</summary>
    public IReadOnlyList<ConfigField> ConfigFields { get; set; } = Array.Empty<ConfigField>();

    /// <summary>打开配置对话框的命令</summary>
    public IRelayCommand ConfigCommand { get; }

    /// <summary>
    /// 创建插件列表项视图模型
    /// </summary>
    public PluginItemViewModel(IUsageProvider provider, ConfigService configService)
    {
        _provider = provider;
        _configService = configService;
        ConfigFields = provider.ConfigFields;
        ConfigCommand = new RelayCommand(OpenConfigDialog);
    }

    /// <summary>
    /// 打开插件配置对话框（公共，可被主窗口卡片上的"⚙ 设置"按钮复用）
    /// </summary>
    public void OpenConfigDialog()
    {
        var config = _configService.GetProviderConfig(ProviderId, _provider);
        // 读取该 Provider 当前已选的卡片图表集合（含旧单选迁移），传入配置窗口用于回显 + 预览
        var currentCharts = _configService.GetProviderCardChartKinds(ProviderId);
        // 通用登录配置：只要插件声明了 LoginConfig（不限于 MiniMax），配置窗口就显示"获取登录态"按钮
        // req-065 B4：传递 ConfigService 给 PluginConfigWindow，用于 BrowserLoginService 实例化
        var configWindow = new Views.PluginConfigWindow(
            DisplayName, ConfigFields, config, _provider.LoginConfig,
            _provider.SupportedCardCharts, currentCharts, _configService);
        configWindow.Owner = System.Windows.Application.Current.Windows
            .OfType<Window>().FirstOrDefault(w => w.IsActive);

        if (configWindow.ShowDialog() == true)
        {
            _configService.UpdateProviderConfig(ProviderId, config);
            // 持久化并同步卡片图表多选（setter 内部写配置 + Save + 通知）
            CardChartKinds = configWindow.SelectedCardChartKinds;
            // 检查保存是否真的成功（ConfigService.Save 失败时会被 catch 吞掉）
            if (!string.IsNullOrEmpty(_configService.LastSaveError))
            {
                System.Windows.MessageBox.Show(
                    $"配置保存失败：{_configService.LastSaveError}\n\n可能是磁盘满、权限不足或文件被占用。\n配置在本次会话有效，但重启后可能丢失。",
                    "保存失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 主窗口ViewModel - 管理用量数据显示和交互逻辑
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;
    private readonly RefreshService _refreshService;
    private readonly UsageHistoryStore _historyStore;
    // req-028：每 1s 触发一次的全局 DispatcherTimer，用来刷新各 Provider 卡的 5h 倒计时 + 到时自动刷新。
    // 单例复用；启动时由 MainViewModel 构造函数 Start，资沅销毁由 App.xaml.cs OnExit 调用 Stop。
    private System.Windows.Threading.DispatcherTimer? _fiveHourCountdownTimer;
    // req-028："上一次自动刷新触发"时间，防止系统时间回退 / 重复 tick 造成连续多次触发 RefreshProviderAsync。
    private DateTime _lastAutoRefreshUtc = DateTime.MinValue;

    /// <summary>供主窗口和设置窗口复用的全局配置服务。</summary>
    public ConfigService ConfigService => _configService;

    /// <summary>各服务商的用量显示列表（全量，包含被禁用的项，用于切换时保留状态）</summary>
    public ObservableCollection<ProviderUsageViewModel> Usages { get; } = new();

    /// <summary>仅展示已启用插件的用量卡片（主窗口 ItemsControl 实际绑定此集合）</summary>
    public ObservableCollection<ProviderUsageViewModel> EnabledUsages { get; } = new();

    /// <summary>插件列表</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems { get; } = new();

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

    /// <summary>托盘悬浮窗关闭延迟（毫秒）</summary>
    public int TrayTooltipHideDelayMs
    {
        get => _configService.Settings.TrayTooltipHideDelayMs;
        set
        {
            var v = Math.Max(0, value);
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
            _historyStore.MaxPoints = v;
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

    public MainViewModel(PluginManager pluginManager, ConfigService configService, RefreshService refreshService, UsageHistoryStore? historyStore = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _refreshService = refreshService;
        _historyStore = historyStore ?? new UsageHistoryStore();
        _historyStore.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);

                // req-072 U-18：RefreshCommand 执行时更新 LastRefreshTime / RefreshProgress / ErrorCount
                RefreshCommand = new AsyncRelayCommand(async () =>
                {
                    RefreshProgress = 0;
                    ErrorCount = 0;
                    try
                    {
                        await refreshService.RefreshAllAsync();
                        LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
                        RefreshProgress = 100;
                        // 统计错误数量
                        ErrorCount = EnabledUsages.Count(u => u.IsError);
                    }
                    catch
                    {
                        ErrorCount++;
                    }
                });
        SaveSettingsCommand = new RelayCommand(() => _configService.Save());

        // req-016：初始化主窗口 Logo + 订阅主题切换事件
        // req-032：单 logo 模式，加载一次即可（不再订阅 ThemeChanged 切换 logo）
        CurrentLogoSource = UsageMonitor.App.Helpers.LogoProvider.LoadLogo();

        // REQ-003：环形图 metric 顺序从设置同步到 ListBox 集合；提供上下移动 + 恢复默认三个命令
        // 使用 RelayCommand<int> 泛型版本（列表索引），CommunityToolkit.Mvvm 8.x 的非泛型 RelayCommand 仅接 Action。
        SyncRingChartMetricOrderFromConfig();
        // req-026：环形图中心数字选择项集合从插件 Support + Config 计算
        BuildProviderRingChartMetricGroups();
        MoveRingMetricUpCommand = new RelayCommand<string>(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            var idx = RingChartMetricOrder.IndexOf(key);
            if (idx <= 0 || idx >= RingChartMetricOrder.Count) return;
            (RingChartMetricOrder[idx - 1], RingChartMetricOrder[idx]) =
                (RingChartMetricOrder[idx], RingChartMetricOrder[idx - 1]);
            PersistRingChartMetricOrder();
        });
        MoveRingMetricDownCommand = new RelayCommand<string>(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            var idx = RingChartMetricOrder.IndexOf(key);
            if (idx < 0 || idx >= RingChartMetricOrder.Count - 1) return;
            (RingChartMetricOrder[idx + 1], RingChartMetricOrder[idx]) =
                (RingChartMetricOrder[idx], RingChartMetricOrder[idx + 1]);
            PersistRingChartMetricOrder();
        });
        ResetRingMetricOrderCommand = new RelayCommand(() =>
        {
            RingChartMetricOrder.Clear();
            foreach (var k in RingChartMetricKeys.DefaultOrder) RingChartMetricOrder.Add(k);
            PersistRingChartMetricOrder();
        });

        // REQ-004/006：触发区域默认重置命令 + 进入蒙版
        ResetTriggerAreaCommand = new RelayCommand(() =>
        {
            var def = ClampRect(RectInt.DefaultBottomRight());
            _configService.Settings.TrayTooltipTriggerRect = def;
            _configService.Save();
            OnPropertyChanged(nameof(TriggerRectX));
            OnPropertyChanged(nameof(TriggerRectY));
            OnPropertyChanged(nameof(TriggerRectWidth));
            OnPropertyChanged(nameof(TriggerRectHeight));
        });
        EditTriggerAreaCommand = new RelayCommand(() => OpenTriggerOverlayAction?.Invoke());

        // 订阅配置变更：当外部（其它入口直接改 Settings、TriggerAreaOverlayWindow 拖拽、程序其它点 Save）修改任意配置时，
        // 通知所有 Settings 派生属性刷新，让 TwoWay 绑定（TextBox、CheckBox 等）拿最新值。
        // 与 App.xaml.cs 中 _configService.ConfigChanged 订阅互不冲突（多订阅者并行接收）。
        _configService.ConfigChanged += OnConfigChangedRefreshSettings;

        // req-028：启动全局每秒 DispatcherTimer，刷新托盘/悬浮窗/卡片的 5h 倒计时 + 到时自动刷新。
        // 必须在 Usages / EnabledUsages / PluginItems 都装配后启动；后续调用连动。
        StartFiveHourCountdownTimer();

        // 初始化插件列表与用量显示
        foreach (var plugin in pluginManager.Plugins)
        {
            // 读取已保存的显示模式
            var savedMode = UsageMonitor.App.Helpers.TaskbarModeResolver.Resolve(
                _configService.Settings, plugin.Provider.ProviderId);
            var savedCardCharts = _configService.GetProviderCardChartKinds(plugin.Provider.ProviderId);
            
            var item = new PluginItemViewModel(plugin.Provider, _configService)
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                Version = plugin.Provider.Version,
                Author = plugin.Provider.Author,
                Description = plugin.Provider.Description,
                IsEnabled = plugin.IsEnabled,
                DisplayMode = savedMode
            };
            // 初始化卡片图表多选（回显已保存/迁移的选择，不触发写盘）
            item.InitCardChartKinds(savedCardCharts);
            // 双向同步：PluginItem 变更时同步到 UsageVM 与配置
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PluginItemViewModel.DisplayMode))
                {
                    ChangeTaskbarMode(item.ProviderId, item.DisplayMode);
                }
                else if (e.PropertyName == nameof(PluginItemViewModel.IsEnabled))
                {
                    // 设置窗口中勾选/取消勾选插件时，同步到配置/插件管理器/用量列表
                    UpdatePluginEnabled(item.ProviderId, item.IsEnabled);
                }
                else if (e.PropertyName == nameof(PluginItemViewModel.CardChartKinds))
                {
                    // 卡片图表多选变更：同步到对应的用量卡片 VM，立即叠加/移除图表
                    var target = Usages.FirstOrDefault(u => u.ProviderId == item.ProviderId);
                    if (target != null) target.CardChartKinds = item.CardChartKinds;
                }
            };
            PluginItems.Add(item);
            
            // 初始化用量显示（传入打开配置的回调 + ConfigService 让 VM 跟踪"仅 x 个进度条"开关变化）
            var usageVm = new ProviderUsageViewModel(
                item.OpenConfigDialog,
                // 卡片右上角"⟳ 刷新"按钮回调：仅刷新当前服务商，复用刷新事件链路更新 UI/托盘。
                () => _refreshService.RefreshProviderAsync(plugin.Provider.ProviderId))
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                IconPath = ProviderUsageViewModel.ResolveIconPath(plugin.Provider.ProviderId),
                IsEnabled = plugin.IsEnabled,
                DisplayMode = savedMode,
                CardChartKinds = savedCardCharts,
                // 立刻写入插件声明的默认渲染能力，避免首次渲染时被"未声明 render_kind"折叠。
                RenderKinds = plugin.Provider.DefaultRenderKinds,
                // req-007：把当前 provider 注入到 VM，供 PeriodChanged → SetPeriodAsync 调用。
                Provider = plugin.Provider,
                // req-007：把"是否支持周期切换"在装配时立即写入，避免首次刷新前 UI 缺位。
                SupportsPeriodSwitch = plugin.Provider.SupportsPeriodSwitch,
                ExtraTooltipLines = plugin.Provider.ExtraTooltipLines
            };
            usageVm.AttachConfigService(_configService);
            Usages.Add(usageVm);
        }

        // 启动时构建一次"已启用"过滤集合
        RebuildEnabledUsages();

        // req-053：启动时同步全局已启用 metric 到所有 Provider，避免重启后显示所有 metric
        // 必须在 Usages 集合构建完成后调用
        SyncGlobalEnabledMetricsToAllProviders();

        // 监听历史数据变化
        _historyStore.ProviderHistoryChanged += OnProviderHistoryChanged;
        _historyStore.HistoryChanged += OnAnyHistoryChanged;

        // 订阅全局用量色阶变更：档位 / 颜色改了之后，强制让所有进度条 XAML 绑定刷新
        // （PercentToBrushConverter 重新走 ResolveBrush → 返回新 Brush），同时重着色卡片热力图单元。
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnUsageTierChanged;

        // req-009：订阅热力图色阶变更（按 token 走 ResolveBrush 重算每个 Cell 的背景色）。
        UsageMonitor.App.Helpers.HeatMapTierScale.TierChanged += OnHeatMapTierChanged;
    }

    /// <summary>
    /// 获取当前生效的档位配置供设置页"用量色阶" Tab 回显使用。
    /// 缺省时返回出厂默认。
    /// </summary>
    public List<UsageMonitor.Core.Models.UsageTierConfig> GetCurrentTierConfigForEditor()
        => _configService.GetEffectiveUsageTierConfig();

    /// <summary>
    /// 把编辑结果仅推到全局色阶（预览，不写盘）。点保存按钮才会落盘。
    /// </summary>
    public void PreviewTierConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig> snapshot)
    {
        UsageMonitor.App.Helpers.UsageTierScale.ApplyConfig(snapshot);
    }

    /// <summary>
    /// 写入内存配置 + 落盘 + 推送全局色阶。
    /// </summary>
    public void SaveTierConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig> snapshot)
    {
        _configService.SetUsageTierConfig(snapshot);
        _configService.Save();
        // Save() 内部已会触发 ConfigChanged，App.OnStartup 里挂的 ApplyConfig 会重新拉一次。
    }

    // =====================================================================
    // req-011：热力图色阶设置项化 UI 相关回调（HeatMapTierListEditorViewModel 调用）
    // =====================================================================

    /// <summary>
    /// 返回已加载的插件列表（providerId → displayName），供设置页"热力图色阶" Tab 的 Provider 下拉框使用。
    /// <para>
    /// "通用默认"是固定的第一项（空字符串 key），表示编辑 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/> 兜底色阶。
    /// 但本期"通用默认"不允许保存（<see cref="SaveHeatMapTierConfig"/> 收到空 key 时直接 return）。
    /// </para>
    /// </summary>
    public System.Collections.Generic.IEnumerable<(string providerId, string displayName)> GetLoadedProviderOptions()
    {
        // req-011：按 plugin.DisplayName 升序排列，与设置页"插件" Tab 顺序一致
        return _pluginManager.Plugins
            .Select(p => (p.Provider.ProviderId, p.Provider.DisplayName))
            .OrderBy(x => x.Item2, System.StringComparer.CurrentCulture);
    }

    /// <summary>
    /// 拉取指定 Provider 的当前生效热力图色阶（<see cref="HeatMapTierConfig"/> 列表），供编辑器回显。
    /// <para>
    /// 优先级（与 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush"/> 一致）：
    /// <list type="number">
    ///   <item><description>providerId 为空 → <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/></description></item>
    ///   <item><description><c>ConfigService.Settings.ProviderHeatMapTiers[providerId]</c> 存在且非空 → 持久化的色阶</description></item>
    ///   <item><description>MiniMax 专用 → <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.MiniMaxDefaults"/> 6 档</description></item>
    ///   <item><description>其他 → <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/> 4 档</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public System.Collections.Generic.List<UsageMonitor.Core.Models.HeatMapTierConfig> GetCurrentHeatMapTiersForEditor(string providerId)
    {
        var key = (providerId ?? string.Empty).Trim();
        // 通用默认：直接返回 GenericDefaults
        if (string.IsNullOrEmpty(key))
            return RuntimeTiersToConfigList(UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults);

        // 已持久化的色阶
        if (_configService.Settings.ProviderHeatMapTiers.TryGetValue(key, out var saved)
            && saved != null && saved.Count > 0)
            return saved.ToList();

        // 兜底：MiniMax 用 6 档，其他用 4 档
        var defaults = string.Equals(key, "MiniMax", System.StringComparison.OrdinalIgnoreCase)
            ? UsageMonitor.App.Helpers.HeatMapTierScale.MiniMaxDefaults
            : UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults;
        return RuntimeTiersToConfigList(defaults);
    }

    /// <summary>把运行时 <see cref="UsageMonitor.App.Helpers.HeatMapTier"/> 列表转 <see cref="HeatMapTierConfig"/>（用于编辑器回显）。</summary>
    private static System.Collections.Generic.List<UsageMonitor.Core.Models.HeatMapTierConfig> RuntimeTiersToConfigList(
        System.Collections.Generic.IEnumerable<UsageMonitor.App.Helpers.HeatMapTier> tiers)
    {
        var result = new System.Collections.Generic.List<UsageMonitor.Core.Models.HeatMapTierConfig>();
        foreach (var t in tiers)
        {
            result.Add(new UsageMonitor.Core.Models.HeatMapTierConfig
            {
                MinTokens = t.MinTokens,
                ColorHex = $"#{t.Color.R:X2}{t.Color.G:X2}{t.Color.B:X2}",
                IsEnabled = t.IsEnabled
            });
        }
        return result;
    }

    /// <summary>
    /// 把编辑结果仅推到全局 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale"/>（预览，不写盘）。
    /// <para>
    /// 为避免污染其他 Provider 的色阶，预览时合并"该 Provider 的预览值 + 其他 Provider 的当前持久化值"
    /// 一起推给 <c>ApplyConfig</c>。空 key 直接 return（通用默认不允许预览）。
    /// </para>
    /// </summary>
    public void PreviewHeatMapTierConfig(string providerId, IReadOnlyList<UsageMonitor.Core.Models.HeatMapTierConfig> snapshot)
    {
        var key = (providerId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key)) return; // 通用默认不允许保存/预览

        // 合并：当前持久化 + 该 Provider 的预览值
        var merged = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IList<UsageMonitor.Core.Models.HeatMapTierConfig>>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _configService.Settings.ProviderHeatMapTiers)
            merged[kv.Key] = kv.Value;
        merged[key] = snapshot.ToList();
        UsageMonitor.App.Helpers.HeatMapTierScale.ApplyConfig(merged);
    }

    /// <summary>
    /// 写入 <see cref="ConfigService"/> + 落盘 + 推送全局色阶。
    /// <para>
    /// 空 key 直接 return（通用默认不允许保存）。"__generic__" 也不允许（防止污染硬编码 GenericDefaults）。
    /// </para>
    /// </summary>
    public void SaveHeatMapTierConfig(string providerId, IReadOnlyList<UsageMonitor.Core.Models.HeatMapTierConfig> snapshot)
    {
        var key = (providerId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key) || key == "__generic__") return;

        _configService.Settings.ProviderHeatMapTiers[key] = snapshot.ToList();
        _configService.Save();
        // Save() 内部已会触发 ConfigChanged，App.OnStartup 里挂的 ApplyConfig 会重新拉一次。
    }

    /// <summary>
    /// 全局色阶变更回调：依次（1）让所有进度条的 Percent 属性发出 PropertyChanged，让 XAML 上的
    /// PercentToBrushConverter 重新解析；（2）重着色卡片热力图单元。
    /// </summary>
    private void OnUsageTierChanged(object? sender, EventArgs e)
    {
        ForceRefreshBars();
        RecolorAllHeatMaps();
    }

    /// <summary>
    /// req-009：热力图色阶变更回调，让所有卡片热力图按新色阶重算每个 Cell 的背景色。
    /// </summary>
    private void OnHeatMapTierChanged(object? sender, EventArgs e)
    {
        RecolorAllHeatMaps();
    }

    /// <summary>
    /// 对所有 ProviderUsageViewModel 的 4 个进度条 Percent 属性发出 PropertyChanged，
    /// 让 PercentToBrushConverter 重新取色。
    /// </summary>
    private void ForceRefreshBars()
    {
        foreach (var vm in Usages)
        {
            vm.RefreshAllPercentProperties();
        }
    }

    /// <summary>
    /// 重着色所有卡片热力图单元（用新色阶刷 Background）。
    /// 历史窗口的热力图由 HistoryViewModel 自行订阅事件负责重着色。
    /// </summary>
    private void RecolorAllHeatMaps()
    {
        foreach (var vm in Usages)
        {
            vm.RecolorHeatMapCells();
        }
    }

    /// <summary>
    /// 当指定 Provider 的历史数据变化时刷新对应 VM
    /// </summary>
    private void OnProviderHistoryChanged(object? sender, string providerId)
    {
        var vm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (vm == null) return;
        vm.HistoryValues = _historyStore.GetHistoryValues(providerId);
    }

    /// <summary>
    /// 当 MaxPoints 等全局设置变化时刷新所有 VM
    /// </summary>
    private void OnAnyHistoryChanged(object? sender, EventArgs e)
    {
        foreach (var vm in Usages)
        {
            vm.HistoryValues = _historyStore.GetHistoryValues(vm.ProviderId);
        }
    }

    /// <summary>
    /// 更新所有用量数据
    /// </summary>
    public void UpdateUsages(IReadOnlyList<UsageInfo> usages)
    {
        foreach (var usage in usages)
        {
            var vm = Usages.FirstOrDefault(u => u.ProviderId == usage.ProviderId);
            vm?.UpdateFromUsage(usage);
        }
    }

    /// <summary>
    /// 根据 Usages 中各 VM 的 IsEnabled 状态重建"已启用"过滤集合，供主窗口 ItemsControl 绑定。
    /// 取消勾选时调用此方法即可让对应卡片立即从主窗口消失。
    /// </summary>
    private void RebuildEnabledUsages()
    {
        EnabledUsages.Clear();
        foreach (var vm in Usages.Where(u => u.IsEnabled))
        {
            EnabledUsages.Add(vm);
        }
        OnPropertyChanged(nameof(EnabledUsages));
    }

    /// <summary>
    /// 更新插件启用状态：同步配置、插件管理器、用量VM，并刷新主窗口卡片集合。
    /// </summary>
    public void UpdatePluginEnabled(string providerId, bool isEnabled)
    {
        _configService.Settings.PluginEnabled[providerId] = isEnabled;

        var plugin = _pluginManager.GetPlugin(providerId);
        if (plugin != null) plugin.IsEnabled = isEnabled;

        var usageVm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (usageVm != null) usageVm.IsEnabled = isEnabled;

        // 重建过滤集合，使主窗口中已禁用插件的卡片立即消失/再次出现
        RebuildEnabledUsages();

        _configService.Save();
    }

    /// <summary>
    /// 修改 Provider 的任务栏显示模式（同步到 Usages + PluginItems + 配置）
    /// </summary>
    public void ChangeTaskbarMode(string providerId, TaskbarDisplayMode mode)
    {
        _configService.Settings.ProviderTaskbarModes[providerId] = mode;

        var usageVm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (usageVm != null && usageVm.DisplayMode != mode)
            usageVm.DisplayMode = mode;

        _configService.Save();
    }

    /// <summary>
    /// 外部 ConfigService 变更时，统一通知所有 Settings 派生属性刷新。
    /// 保证触发区域调试矩形拖动 / 其它入口改配置后，SettingsWindow 中的 TextBox 双向绑定能拿到最新值。
    /// </summary>
    private void OnConfigChangedRefreshSettings(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RefreshInterval));
        OnPropertyChanged(nameof(ShowInTaskbar));
        OnPropertyChanged(nameof(ShowTrayTooltip));
        OnPropertyChanged(nameof(TrayTooltipHideDelayMs));
        OnPropertyChanged(nameof(TrayTriggerWidth));
        OnPropertyChanged(nameof(TrayTriggerHeight));
        OnPropertyChanged(nameof(RingChartWarningThreshold));
        OnPropertyChanged(nameof(RingChartDangerThreshold));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));

        // REQ-003：触发区域、sticky、动画同步
        OnPropertyChanged(nameof(RingChartStickySeconds));
        OnPropertyChanged(nameof(RingChartSwitchAnimationMs));
        SyncRingChartMetricOrderFromConfig();

        // REQ-004：触发区域 4 字段同步
        OnPropertyChanged(nameof(TriggerRectX));
        OnPropertyChanged(nameof(TriggerRectY));
        OnPropertyChanged(nameof(TriggerRectWidth));
        OnPropertyChanged(nameof(TriggerRectHeight));
    }

    /// <summary>REQ-003：把 ConfigService.Settings.RingChartMetricOrder 同步到 ListBox 绑定集合。</summary>
    private void SyncRingChartMetricOrderFromConfig()
    {
        var src = _configService.Settings.RingChartMetricOrder;
        if (src == null || src.Count == 0) return;
        // 简单同步：长度变化或顺序不同时整体重灌；否则保留当前 ListBox 选中状态
        if (RingChartMetricOrder.Count != src.Count)
        {
            RingChartMetricOrder.Clear();
            foreach (var k in src) RingChartMetricOrder.Add(k);
            return;
        }
        for (var i = 0; i < src.Count; i++)
        {
            if (!string.Equals(RingChartMetricOrder[i], src[i], StringComparison.OrdinalIgnoreCase))
            {
                RingChartMetricOrder.Clear();
                foreach (var k in src) RingChartMetricOrder.Add(k);
                return;
            }
        }
    }

    /// <summary>REQ-003：把当前 ListBox 顺序写回 ConfigService 并落盘。</summary>
    private void PersistRingChartMetricOrder()
    {
        _configService.Settings.RingChartMetricOrder = RingChartMetricOrder.ToList();
        _configService.Save();
    }

    /// <summary>
    /// req-026：从 <c>_pluginManager.Plugins</c> + <c>AppSettings.ProviderEnabledRingChartMetrics</c>
    /// 重建 <see cref="ProviderRingChartMetricGroups"/>。
    /// <para>每个启用插件创建一个组（含 ProviderId / DisplayName / AvailableMetrics），组内每个支持的 metric
    /// 一个 <see cref="RingChartMetricChoice"/>，IsEnabled 由 <c>RingChartMetricResolver</c> 解析。
    /// 重建时先清空集合，保证绑定侧有最新状态。</para>
    /// </summary>
    private void BuildProviderRingChartMetricGroups()
    {
        ProviderRingChartMetricGroups.Clear();
        var settings = _configService.Settings;
        foreach (var plugin in _pluginManager.Plugins)
        {
            var supported = plugin.Provider.SupportedRingChartMetrics ?? Array.Empty<string>();
            if (supported.Count == 0) continue;
            var group = new ProviderRingChartMetricGroup
            {
                ProviderId = plugin.Provider.ProviderId,
                ProviderDisplayName = plugin.Provider.DisplayName,
            };
            var enabledList = UsageMonitor.App.Helpers.RingChartMetricResolver
                .GetEnabledMetrics(settings, plugin.Provider.ProviderId);
            foreach (var key in supported)
            {
                group.Metrics.Add(new RingChartMetricChoice
                {
                    Key = key,
                    DisplayName = ResolveRingMetricDisplayName(key),
                    IsEnabled = UsageMonitor.App.Helpers.RingChartMetricResolver.IsMetricEnabled(enabledList, key)
                });
            }
            // 同步订阅：勾选变化时写回 settings + 通知 ProviderUsageViewModel.EnabledRingChartMetrics
            foreach (var m in group.Metrics)
            {
                m.PropertyChanged += (_, _) =>
                {
                    // 收集该 group 当前所有勾选的 key
                    var keys = group.Metrics.Where(x => x.IsEnabled).Select(x => x.Key).ToList();
                    settings.ProviderEnabledRingChartMetrics[group.ProviderId] = keys;
                    _configService.Save();
                    // 触发刷新卡片上的 EnabledMetrics
                    OnPropertyChanged(nameof(ProviderRingChartMetricGroups));
                    SyncProviderEnabledMetricsToVm(group.ProviderId, keys);
                };
            }
            ProviderRingChartMetricGroups.Add(group);
        }
    }

    /// <summary>req-026：把某个 Provider 当前勾选的 metric key 集合同步到对应 ProviderUsageViewModel，
    /// 让卡片上的 RingChartControl 立即刷新。</summary>
    private void SyncProviderEnabledMetricsToVm(string providerId, IReadOnlyList<string> enabledKeys)
    {
        foreach (var vm in Usages)
        {
            if (string.Equals(vm.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                vm.EnabledRingChartMetrics = enabledKeys;
            }
        }
    }

    /// <summary>req-053：把全局已启用 metric 集合同步到所有 ProviderUsageViewModel。</summary>
    public void SyncGlobalEnabledMetricsToAllProviders()
    {
        var globalEnabled = _configService.Settings.GlobalEnabledRingChartMetrics;
        foreach (var vm in Usages)
        {
            vm.EnabledRingChartMetrics = globalEnabled;
        }
    }

    /// <summary>req-026：根据 metric key 解析中文显示名（控件未配置时回退 RingChartMetricKeys 常量名）。</summary>
    private static string ResolveRingMetricDisplayName(string key)
    {
        return key switch
        {
            "Percent" => "已用百分比",
            "Credits" => "积分余额",
            "WeeklyLimit" => "本周限额",
            "RemainingQuota" => "剩余额度",
            "ApiTokenUsed" => "已用 Token",
            _ => key
        };
    }

    /// <summary>
    /// req-016：主题切换事件处理。
    /// <para>
    /// req-032：单 logo 模式后不再需要切换 logo，但保留方法以便未来扩展其他主题切换逻辑。
    /// </para>
    /// </summary>
    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        // req-032：单 logo 不需要按主题重新加载，保持空实现
    }

    // =====================================================================
    // req-028：5h 刷新倒计时（托盘 + 悬浮窗 + 到时自动刷新）
    // =====================================================================

    /// <summary>
    /// req-028：启动全局每秒 <c>DispatcherTimer</c>，刷新所有 Provider 卡片的 5h 倒计时文本 + 到时自动刷新。
    /// <para>
    /// 调用方：MainViewModel 构造函数末尾（确保所有 <see cref="Usages"/> 已装配后再启动）。
    /// 期望与 <c>App.OnExit</c> 配对调 <see cref="StopFiveHourCountdownTimer"/> 避免泄漏。
    /// </para>
    /// </summary>
    public void StartFiveHourCountdownTimer()
    {
        if (_fiveHourCountdownTimer != null) return; // 幂等
        _fiveHourCountdownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _fiveHourCountdownTimer.Tick += OnFiveHourCountdownTick;
        _fiveHourCountdownTimer.Start();
    }

    /// <summary>
    /// req-028：停止全局每秒 timer，App.OnExit 入口调用以避免 <c>DispatcherTimer</c> 引用泄漏。
    /// </summary>
    public void StopFiveHourCountdownTimer()
    {
        if (_fiveHourCountdownTimer == null) return;
        _fiveHourCountdownTimer.Stop();
        _fiveHourCountdownTimer.Tick -= OnFiveHourCountdownTick;
        _fiveHourCountdownTimer = null;
    }

    /// <summary>
    /// req-028：每秒 tick：遍历所有用量 VM 刷新 <see cref="ProviderUsageViewModel.FiveHourCountdownText"/> + 检查是否需要到时自动刷新。
    /// <para>每个 tick 都是 fire-and-forget；调用 <see cref="ProviderUsageViewModel.ShouldTriggerFiveHourAutoRefresh"/> 判断后调
    /// <see cref="RefreshService.RefreshProviderAsync"/>。为防重复触发，已经在 VM 上标记过的不会再被本 tick 选中，
    /// 直到新 <see cref="ProviderUsageViewModel.Next5hResetAt"/> 到来重置该标记。</para>
    /// </summary>
    private void OnFiveHourCountdownTick(object? sender, EventArgs e)
    {
        if (Usages == null) return;
        foreach (var vm in Usages)
        {
            if (vm == null) continue;
            vm.RefreshFiveHourCountdownText(DateTime.Now);
            if (vm.ShouldTriggerFiveHourAutoRefresh())
            {
                vm.MarkFiveHourAutoRefreshTriggered();
                var providerId = vm.ProviderId;
                _ = TriggerProviderAutoRefreshAsync(providerId);
            }
        }
    }

    /// <summary>
    /// req-028：异步触发指定 Provider 的自动刷新（不带用户交互）。
    /// <para>用 <c>_ = </c> fire-and-forget 启动 <see cref="RefreshService.RefreshProviderAsync"/>，
    /// 失败仅写日志（不会弹出错误窗口打扰用户）。</para>
    /// </summary>
    private async Task TriggerProviderAutoRefreshAsync(string providerId)
    {
        try
        {
            UsageMonitor.Core.Services.FileLogger.Info("MainViewModel",
                $"5h 倒计时到 0，自动刷新 Provider={providerId}");
            await _refreshService.RefreshProviderAsync(providerId);
        }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MainViewModel",
                $"5h 自动刷新 Provider={providerId} 抛出异常（容许）", ex);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// req-026：设置窗口"环形图中心" Tab 中单个 Provider 的 metric 勾选状态集合。
/// <para>每行 Provider 对应一个本实例，<see cref="Metrics"/> 列出该 Provider 支持的全部 metric，
/// 勾选态由 <c>AppSettings.ProviderEnabledRingChartMetrics[ProviderId]</c> + 全局默认合并解析。</para>
/// </summary>
public class ProviderRingChartMetricGroup
{
    /// <summary>Provider 唯一标识。</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider 中文显示名。</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>该 Provider 支持的环形图中心 metric 勾选项集合。</summary>
    public ObservableCollection<RingChartMetricChoice> Metrics { get; } = new();
}

/// <summary>
/// req-026：单个环形图中心 metric 的勾选项，对应设置窗口一列 CheckBox。
/// <para>Key 绑定 RingChartControl 的 <c>MetricKey</c>；<see cref="IsEnabled"/> 即是否纳入已启用集合。</para>
/// </summary>
public class RingChartMetricChoice : INotifyPropertyChanged
{
    private bool _isEnabled;

    /// <summary>metric 键（如 <c>"Percent"</c> / <c>"Credits"</c>）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>中文显示名（设置窗口 CheckBox.Content）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>勾选状态（写回 <c>AppSettings.ProviderEnabledRingChartMetrics</c>）。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
