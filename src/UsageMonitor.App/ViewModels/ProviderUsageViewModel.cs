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
/// 单个服务商的用量显示模型
/// </summary>
public class ProviderUsageViewModel : INotifyPropertyChanged
{
    private string _providerId = string.Empty;
    // req-109：每张卡片独立三元组 (Provider, Account, Card)；默认 "default"/"default-card"
    // 保持向后兼容——DisplayModule 未传时退回单卡片语义。
    private string _accountId = "default";
    private string _cardId = "default-card";
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
    private string _subscriptionType = "Token Plan";
    private string _subscriptionTier = "订阅";
    private bool _isSubscriptionActive;
    private double _primaryBarPercent;
    private double _weeklyBarPercent;
    private string _primaryResetText = "--";
    private string _weeklyResetText = "--";
    // req-028：5h 重置的精确时刻（来自 five_hour_reset_at extras）。null 表示该 Provider 无 5h 字段（不应出现在 5h 倒计时语境）。
    private DateTime? _next5hResetAt;
    private string _fiveHourCountdownText = "00:00:00";
    private bool _fiveHourAutoRefreshTriggered;
    // 问题8：周限额重置精确时刻与倒计时文本（任务栏迷你图 tooltip 周数据组展示周限额倒计时）。
    private DateTime? _nextWeeklyResetAt;
    private string _weeklyCountdownText = "00:00:00";
    private string _videoQuotaText = "--";
    private string _videoWeeklyText = "--";
    private double _remainingCredits;
    // Number 图表（数据概览）原始值缓存：供 ResolveFieldDisplay 格式化输出各数据组。
    private string _numberCumulativeText = string.Empty;   // used_tokens_text（如 "5.85B"）
    private double _numberPeakToken = -1;                  // most_active_token（原始数值）
    private long _numberActiveDays;                        // active_days
    private long _numberTotalDays;                         // total_days
    private string _numberMostActiveDay = string.Empty;    // most_active_day（如 "2026-07-01 (552.49M)"，数据概览备注行）
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
    // 声明式富渲染模式标记：为 true 时折线图使用「每日 Token」，不被历史用量百分比覆盖。
    private bool _isDeclarativeRenderMode;
    // req-007：折线图完整化字段。SupportsPeriodSwitch=true 的插件（仅 MiniMax）会启用周期切换按钮。
    private IReadOnlyList<string> _dates = Array.Empty<string>();
    private IReadOnlyList<string>? _extraTooltipLines;
    private bool _supportsPeriodSwitch;
    private string _currentPeriod = UsageMonitor.App.Controls.ChartPeriods.Week;
    private bool _isLoading;
    // req-079 U-33：是否已收到过至少一次数据更新响应（骨架屏判定用）
    private bool _hasReceivedData;
    // req-072 U-05：卡片详情展开状态。req-099 修复（Bug3）：默认改为展开，让卡片首屏即显示
    // 限额/余额/图表全部已填充数据；此前默认折叠只显示 CollapseVisibleParts 声明的区段，
    // 导致 MiniMax 仅显示 5h/周而图表/余额被隐藏，被误认为“数据未显示”。用户仍可点箭头折叠。
    private bool _isDetailExpanded = true;
    // req-007：缓存声明式抓取到的「每日 Token」完整数据，供 PeriodChanged 重新切片。
    // 完整数据按日期升序，长度上限由数据源声明决定。
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
    /// <param name="reLoginAction">
    /// req-091-005：点击卡片「🔑 重新登录」按钮时触发的回调。
    /// 不传时按钮隐藏（用 IUsageProvider.LoginConfig 是否为 null 判定）。
    /// </param>
    public ProviderUsageViewModel(Action? openConfigAction = null, Func<Task>? refreshCardAction = null, Action? reLoginAction = null, string accountId = "default", string cardId = "default-card")
    {
        _openConfigAction = openConfigAction;
        _refreshCardAction = refreshCardAction;
        _reLoginAction = reLoginAction;
        _accountId = string.IsNullOrEmpty(accountId) ? "default" : accountId;
        _cardId = string.IsNullOrEmpty(cardId) ? "default-card" : cardId;
        ConfigCommand = new RelayCommand(OpenConfig, () => _openConfigAction != null);
        RefreshCardCommand = new AsyncRelayCommand(RefreshCardAsync, () => _refreshCardAction != null);
        ReLoginCommand = new RelayCommand(ReLogin, () => _reLoginAction != null);
    }

    /// <summary>req-091-005：手动重新登录回调，由 MainViewModel 装配时注入。</summary>
    private readonly Action? _reLoginAction;

    /// <summary>
    /// req-091-005：点击卡片「🔑 重新登录」按钮时执行的命令。
    /// 委托给 <see cref="_reLoginAction"/>（MainViewModel 在装配时传入），
    /// 由 MainViewModel 内部调用 App 的 TriggerReLogin 流程。
    /// </summary>
    public IRelayCommand ReLoginCommand { get; }

    /// <summary>req-091-005：执行手动重新登录（供 ReLoginCommand 调用）。</summary>
    private void ReLogin()
    {
        _reLoginAction?.Invoke();
    }

    /// <summary>req-091-005：当前卡片是否支持重新登录（仅当插件声明 LoginConfig 时显示按钮）。</summary>
    public bool SupportsReLogin => _reLoginAction != null;

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
            RebuildChartSlots();
        }
    }

    /// <summary>
    /// 配置变更时重新读取 Provider 配置中的标准渲染开关（SDK 契约键）并通知属性变更。
    /// req-104：同时通知 CardMetricBarData/CardMetricGridData 变更以应用字段过滤。
    /// B2：同时刷新卡片标题昵称（设置窗口修改昵称 → UpdateAccount → Save → ConfigChanged → 此处实时生效）。
    /// </summary>
    private void OnConfigChanged(object? sender, EventArgs e)
    {
        ReloadBarToggles();
        // req-104：配置变更时重新过滤多进度条/数字网格字段
        OnPropertyChanged(nameof(CardMetricBarData));
        OnPropertyChanged(nameof(CardMetricGridData));
        // 卡片管理页保存（可见性/顺序/数据组/分界线）后即时重建图表槽位
        RebuildChartSlots();
        // B2：昵称变更后实时刷新卡片标题
        ReloadDisplayNameFromAccount();
    }

    /// <summary>
    /// B2：从 ConfigService 重新读取账号昵称并更新卡片标题。
    /// 账号存在且 UseNickname=true 且昵称非空时显示昵称，否则回退 Provider 显示名。
    /// </summary>
    private void ReloadDisplayNameFromAccount()
    {
        if (ConfigService == null || string.IsNullOrEmpty(_providerId)) return;
        var account = ConfigService.GetAccount(_providerId, AccountIdSafe);
        var targetName = (account is { UseNickname: true } && !string.IsNullOrWhiteSpace(account.Nickname))
            ? account.Nickname.Trim()
            : Provider?.DisplayName;
        if (!string.IsNullOrEmpty(targetName) && DisplayName != targetName)
            DisplayName = targetName;
    }

    /// <summary>
    /// 从当前 ConfigService 拉取本 Provider 配置，按 SDK 标准开关键（StandardConfigFields.Toggle*）读取渲染显隐。
    /// 缺省时维持属性当前值（首次初始化为 true）。
    /// </summary>
    private void ReloadBarToggles()
    {
        if (ConfigService == null || string.IsNullOrEmpty(_providerId)) return;
        var cfg = ConfigService.Settings.ProviderConfigs.Values
            .FirstOrDefault(p => p.ProviderId == _providerId);
        if (cfg == null) return;
        Show5hBar = ReadBool(cfg, StandardConfigFields.ToggleShowFiveHourBar, _show5hBar);
        ShowWeeklyBar = ReadBool(cfg, StandardConfigFields.ToggleShowWeeklyBar, _showWeeklyBar);
        ShowVideo5hBar = ReadBool(cfg, StandardConfigFields.ToggleShowVideoFiveHourBar, _showVideo5hBar);
        ShowVideoWeeklyBar = ReadBool(cfg, StandardConfigFields.ToggleShowVideoWeeklyBar, _showVideoWeeklyBar);
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

    /// <summary>req-109：账号 ID（DisplayModule 3 段路由用）。空字符串时安全返回 "default"。</summary>
    public string AccountIdSafe => string.IsNullOrEmpty(_accountId) ? "default" : _accountId;

    /// <summary>req-109：卡片 ID（DisplayModule 3 段路由用）。空字符串时安全返回 "default-card"。</summary>
    public string CardIdSafe => string.IsNullOrEmpty(_cardId) ? "default-card" : _cardId;
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }

    /// <summary>Provider 图标的文件路径，用于在卡片、任务栏、悬浮窗中显示 logo</summary>
    public string? IconPath { get => _iconPath; set { _iconPath = value; OnPropertyChanged(); } }
    private string? _iconPath;

    /// <summary>
    /// 根据 ProviderId 解析对应的图标文件路径。
    /// <para>
    /// req-069 F-14：移除硬编码 switch 列表，改为约定优于配置 + 插件 <see cref="IUsageProvider.IconPath"/>
    /// 优先。扫描 <c>Assets/Providers/{providerId}.{png|ico|jpg}</c> 任一存在的文件。
    /// </para>
    /// <para>优点：新增 Provider 只需在 Assets/Providers/ 放下对应图标文件，无需改本方法。
    /// SVG 暂不原生支持（需 XAML 转 BitmapImage）。</para>
    /// </summary>
    /// <summary>
    /// 根据 ProviderId 解析对应的图标文件路径（同步、无网络）。
    /// <para>
    /// req-069 F-14：约定优于配置——按 ProviderId 探测图标文件，无需硬编码 switch。
    /// 分发准备：第三方品牌 Logo 不再随包分发（规避商标再分发风险），改由
    /// <see cref="UsageMonitor.App.Services.ProviderIconService"/> 在运行时抓取 favicon 缓存；
    /// 本方法保留静态签名并委托该服务做同步本地解析（用户缓存目录 → 随包资源）。
    /// </para>
    /// </summary>
    public static string? ResolveIconPath(string providerId)
        => UsageMonitor.App.Services.ProviderIconService.ResolveIconPath(providerId);

    /// <summary>
    /// 分发准备：重新解析并刷新本卡片图标（供启动预取 favicon 完成后回填显示）。
    /// <para>在 UI 线程调用；<see cref="IconPath"/> 为 INotifyPropertyChanged 属性，赋值后界面自动重绑。
    /// 仅在解析到新图标且与当前值不同时才赋值，避免无谓的属性通知。</para>
    /// </summary>
    public void RefreshIcon()
    {
        var resolved = ResolveIconPath(ProviderId);
        if (!string.IsNullOrEmpty(resolved) && resolved != IconPath)
            IconPath = resolved;
    }

    /// <summary>
    /// Stage B：按插件 errorGuidance 声明解析失败态引导文案。
    /// <para>规则按声明顺序匹配：错误消息包含任一关键字即命中；空关键字规则为兑底（恒命中）。
    /// 无声明或全部未命中返回 null（宿主保持通用"查询失败"文案）。</para>
    /// </summary>
    /// <param name="errorMessage">本次失败的错误消息（可空）。</param>
    private string? ResolveErrorGuidance(string? errorMessage)
    {
        var rules = Provider?.ErrorGuidance;
        if (rules == null || rules.Count == 0) return null;
        var msg = errorMessage ?? string.Empty;
        foreach (var rule in rules)
        {
            if (rule.MatchKeywords.Count == 0) return rule.Message; // 兑底规则
            foreach (var kw in rule.MatchKeywords)
            {
                if (!string.IsNullOrEmpty(kw) && msg.Contains(kw)) return rule.Message;
            }
        }
        return null;
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

    /// <summary>req-088 Phase2：订阅类型（Token Plan / Coding Plan / Agent Plan / API），卡片左栏。</summary>
    public string SubscriptionType { get => _subscriptionType; set { _subscriptionType = value; OnPropertyChanged(); } }

    /// <summary>req-088 Phase2：订阅档位（如 TokenPlanMax-年度会员），卡片右栏。</summary>
    public string SubscriptionTier { get => _subscriptionTier; set { _subscriptionTier = value; OnPropertyChanged(); } }

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
        // 问题8：周限额倒计时随同一全局 timer 每秒刷新。
        RefreshWeeklyCountdownText(current);
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

    /// <summary>req-028：MainViewModel 检查到“到点了”时调用，标记本次窗口已经触发；到下个新 five_hour_reset_at 出现时被重置。</summary>
    public void MarkFiveHourAutoRefreshTriggered() => _fiveHourAutoRefreshTriggered = true;

    /// <summary>req-028：检查本 Provider 是否应该被自动刷新（倒计时≤0 且本窗口未触发过）。</summary>
    public bool ShouldTriggerFiveHourAutoRefresh()
        => _next5hResetAt.HasValue
           && _next5hResetAt.Value <= DateTime.Now
           && !_fiveHourAutoRefreshTriggered;

    /// <summary>
    /// req-028：5h 重置精确时刻（来自 five_hour_reset_at extras）。
    /// <para>用于计算托盘/悬浮窗倒计时 <see cref="FiveHourCountdownText"/>，以及到时自动刷新的判定。
    /// 由 <c>UpdateFromDeclarativeExtras</c> 写入；为 null 时表示该 Provider 不参与 5h 倒计时。</para>
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
            OnPropertyChanged(nameof(HasResetCountdown));
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
    public bool HasResetCountdown => _next5hResetAt.HasValue;

    /// <summary>问题8：周限额重置精确时刻（来自 weekly_reset_at extras；null = 该 Provider 无周限额倒计时）。</summary>
    public DateTime? NextWeeklyResetAt
    {
        get => _nextWeeklyResetAt;
        set
        {
            if (_nextWeeklyResetAt == value) return;
            _nextWeeklyResetAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WeeklyCountdownText));
        }
    }

    /// <summary>问题8：周限额倒计时文本（HH:mm:ss，由 5h 倒计时同一全局 timer 每秒刷新；无周重置时间时为 00:00:00）。</summary>
    public string WeeklyCountdownText
    {
        get => _weeklyCountdownText;
        set { if (_weeklyCountdownText == value) return; _weeklyCountdownText = value; OnPropertyChanged(); }
    }

    /// <summary>问题8：根据 <see cref="NextWeeklyResetAt"/> 重算 <see cref="WeeklyCountdownText"/>（由 <see cref="RefreshFiveHourCountdownText"/> 驱动）。</summary>
    private void RefreshWeeklyCountdownText(DateTime now)
    {
        var target = _nextWeeklyResetAt;
        WeeklyCountdownText = (target == null || target.Value <= now)
            ? "00:00:00"
            : UsageMonitor.App.Helpers.CountdownFormatter.Format(target.Value - now);
    }

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
    /// req-折叠插件控制：插件声明"卡片折叠状态下仍可见的元素"集合（render_kind key）。
    /// 与 <see cref="RenderKinds"/> 不同的是：本集合控制**折叠态**下哪些元素保持显示，
    /// 展开态下不受影响（仍按 <see cref="RenderKinds"/> 决定可见性）。
    /// <para>由 MainViewModel 装配时从 <c>IUsageProvider.CollapseVisibleParts</c> 注入。
    /// 默认空集合 —— 折叠态下隐藏所有限额/余额/图表。</para>
    /// </summary>
    private IReadOnlyList<string> _collapseVisibleParts = Array.Empty<string>();
    public IReadOnlyList<string> CollapseVisibleParts
    {
        get => _collapseVisibleParts;
        set
        {
            _collapseVisibleParts = value ?? Array.Empty<string>();
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

    // ============ req-091：登录态持续天数 ============

    private int _sessionDurationDays;

    /// <summary>
    /// req-091：当前 Provider 登录态持续天数（0 = 首次登录当天，&gt;0 = 持续 N 天）。
    /// </summary>
    public int SessionDurationDays
    {
        get => _sessionDurationDays;
        set
        {
            if (_sessionDurationDays == value) return;
            _sessionDurationDays = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SessionDurationText));
        }
    }

    /// <summary>
    /// req-091：当前登录态持续天数的显示文本。
    /// <para>
    /// 王晨 16:57 拍板：持续天数为 0（首次登录当天）显示**空值**——不显示"今天"、不显示"0天"、
    /// 不显示倒计时、不显示提醒。UI 用 <c>StringToVisibility</c> 转换器自动隐藏空文本。
    /// </para>
    /// </summary>
    public string? SessionDurationText
    {
        get
        {
            if (_sessionDurationDays <= 0) return null;
            return $"持续 {_sessionDurationDays} 天";
        }
    }

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
            OnPropertyChanged(nameof(EnabledCharts));
        }
    }

    /// <summary>是否显示卡片图表区（多选集合非空时为 true）。</summary>
    public bool HasCardChart => _cardChartKinds != null && _cardChartKinds.Count > 0;

    /// <summary>
    /// req-107 B6 + req-005-019：实际可渲染的卡片图表集合 = 「Provider 声明的 <see cref="IUsageProvider.Card"/> 声明的 Charts」
    /// 与「用户多选 <see cref="CardChartKinds"/>」的交集。声明驱动：Card 存在时按 Card.Charts 提取 CardChartKind（映射 DeclarativeChartKind → CardChartKind）；
    /// 旧插件（无 Card 声明）回退 <see cref="IUsageProvider.SupportedCardCharts"/>（向后兼容）。</summary>
    public IReadOnlyList<CardChartKind> EnabledCharts
    {
        get
        {
            var supported = ChartKindExtractor.ExtractDeclaredChartKinds(Provider);
            if (supported == null || supported.Count == 0) return _cardChartKinds;
            return _cardChartKinds.Where(supported.Contains).ToList();
        }
    }

    /// <summary>
    /// 卡片折线图的数据序列。非 MiniMax（或 MiniMax API 模式）时跟随 <see cref="HistoryValues"/>（历史用量百分比，0-100）；
    /// MiniMax DOM 模式下由 <see cref="UpdateDeclarativeCharts"/> 替换为「每日 Token 用量」序列。
    /// </summary>
    public IReadOnlyList<double> CardLineValues
    {
        get => _cardLineValues;
        // 问题3：同步通知派生属性 EffectiveCardLineValues（XAML 实际绑定），否则周期切片后图表不刷新。
        set { _cardLineValues = value ?? Array.Empty<double>(); OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveCardLineValues)); }
    }

    /// <summary>卡片折线图的 Y 轴最大值。用量百分比场景为 100；每日 Token 场景为区间最大值（自适应）。</summary>
    public double CardLineMax
    {
        get => _cardLineMax;
        // 问题3：同步通知派生属性 EffectiveCardLineMax。
        set { _cardLineMax = value <= 0 ? 100 : value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveCardLineMax)); }
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
            // 非声明式富渲染模式：卡片折线图跟随历史用量百分比（0-100）。
            // 富渲染模式下折线图改用每日 Token，不能被历史百分比覆盖。
            if (!_isDeclarativeRenderMode)
            {
                CardLineValues = _historyValues;
                CardLineMax = 100;
                // req-007：非 DOM 模式无日期数据，清空 X 轴标签 / tooltip 扩展；
                // 问题3：周期切换能力改由声明驱动（Card.Line.Slicer(Period)），不再无条件关闭。
                Dates = Array.Empty<string>();
                SupportsPeriodSwitch = HasDeclaredPeriodSlicer;
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
        // 问题3：同步通知派生属性 EffectiveCardLineDates（XAML 实际绑定），保证周期切片后 X 轴同步刷新。
        set { _dates = value ?? Array.Empty<string>(); OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveCardLineDates)); }
    }

    /// <summary>折线图 hover tooltip 的扩展文本行（按换行拼接展示）。</summary>
    public IReadOnlyList<string>? ExtraTooltipLines
    {
        get => _extraTooltipLines;
        set { _extraTooltipLines = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// req-105：返回指定图表的有效 Tooltip 显示字段（供卡片 Tooltip 渲染端消费）。
    /// <para>三态语义（问题2/3/4）：用户配置非 null → 直接返回（空集合 = 不显示 tooltip）；
    /// 无用户配置 → 回退 defaults.json 声明的 <c>chart.Tooltip.Fields</c>；
    /// 声明也无（null/空）→ 返回 null（= 不过滤，全部显示，向后兼容）。</para>
    /// </summary>
    public IReadOnlyList<string>? GetEffectiveTooltipFieldsForChart(string chartId)
    {
        if (ConfigService == null || string.IsNullOrEmpty(_providerId) || string.IsNullOrEmpty(chartId))
            return null;
        var eff = ConfigService.GetEffectiveAccountCustomization(_providerId, _accountId, _cardId);
        if (eff.VisibleTooltipFields != null && eff.VisibleTooltipFields.TryGetValue(chartId, out var userFields) && userFields != null)
            return userFields;
        // 回退：defaults.json 声明的 chart.Tooltip.Fields（声明缺失或空时返回 null = 不过滤）
        var chart = Provider?.Card?.Charts.FirstOrDefault(c => c.ChartId == chartId);
        var declared = chart?.Tooltip?.Fields;
        return declared is { Count: > 0 } ? declared : null;
    }

    /// <summary>req-105：折线图的有效 Tooltip 显示字段（定位声明中的 Line 图表并读取其配置；null = 不过滤）。</summary>
    public IReadOnlyList<string>? EffectiveLineTooltipFields
    {
        get
        {
            var lineChart = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.Line);
            if (lineChart == null) return null;
            return GetEffectiveTooltipFieldsForChart(lineChart.ChartId);
        }
    }

    /// <summary>req-105：热力图的有效 Tooltip 显示字段（定位声明中的 HeatMap 图表并读取其配置；null = 不过滤）。</summary>
    public IReadOnlyList<string>? EffectiveHeatMapTooltipFields
    {
        get
        {
            var heatMap = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.HeatMap);
            if (heatMap == null) return null;
            return GetEffectiveTooltipFieldsForChart(heatMap.ChartId);
        }
    }

    /// <summary>req-105：热力图主值字段名（声明中首个 Value 角色字段，供控件 TooltipFields 白名单匹配）。</summary>
    public string? HeatMapTooltipValueField
    {
        get
        {
            var heatMap = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.HeatMap);
            return heatMap?.DataGroups.SelectMany(g => g.Fields).FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
        }
    }

    /// <summary>req-105：热力图主值字段中文显示名（「字段名称」虚拟字段勾选时作为标签行）。</summary>
    public string? HeatMapTooltipFieldLabel
        => HeatMapTooltipValueField != null ? TooltipFieldCatalog.GetDisplay(HeatMapTooltipValueField) : null;

    /// <summary>问题10：热力图对比行字段名（声明中第二个 Value 角色字段，如 daily_token_value）。
    /// <para>对比行（每日 Token 用量）仅在该字段被勾选时显示，不再无条件跟随主值字段。</para></summary>
    public string? HeatMapTooltipComparisonField
    {
        get
        {
            var heatMap = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.HeatMap);
            var valueFields = heatMap?.DataGroups.SelectMany(g => g.Fields).Where(f => f.Role == FieldRole.Value).Select(f => f.FieldName).ToList();
            return valueFields is { Count: > 1 } ? valueFields[1] : null;
        }
    }

    /// <summary>req-105：更新折线图 tooltip 扩展行（勾选的非原生字段 → “标签 当前值”行，原生字段 daily_token_value/daily_cache_hit_value 由控件自处理）。
    /// <para>同时通知 <see cref="EffectiveLineTooltipFields"/> 变更，保证设置页勾选变更后控件的 TooltipFields 绑定即时刷新（修复“关闭字段 tooltip 不变”问题）。</para>
    /// </summary>
    private void RefreshLineTooltip()
    {
        ExtraTooltipLines = BuildExtraTooltipLines(EffectiveLineTooltipFields);
        OnPropertyChanged(nameof(EffectiveLineTooltipFields));
        // req-105：热力图 tooltip 字段同步刷新（设置页勾选变更后即时生效）。
        OnPropertyChanged(nameof(EffectiveHeatMapTooltipFields));
        OnPropertyChanged(nameof(HeatMapTooltipValueField));
        OnPropertyChanged(nameof(HeatMapTooltipFieldLabel));
        OnPropertyChanged(nameof(HeatMapTooltipComparisonField));
    }

    /// <summary>req-105：将勾选的 tooltip 字段（排除折线图原生字段）转为扩展文本行。
    /// <para>问题4：严格按用户保存的字段顺序生成——「字段名称」虚拟字段在其位置产生折线主值字段显示名行；
    /// 日期由控件标题逐点自处理；原生值字段 daily_token_value/daily_cache_hit_value 由控件逐点渲染（控件侧同样按字段顺序插入）。</para>
    /// </summary>
    private IReadOnlyList<string>? BuildExtraTooltipLines(IReadOnlyList<string>? fields)
    {
        if (fields == null || fields.Count == 0) return null;
        var lines = new List<string>();
        foreach (var f in fields)
        {
            if (f == TooltipFieldCatalog.DateVirtual) continue;
            if (f == UsageMonitor.Core.Models.UsageFields.DailyTokenValue ||
                f == UsageMonitor.Core.Models.UsageFields.DailyCacheHitValue) continue;
            if (f == TooltipFieldCatalog.FieldNameVirtual)
            {
                // 字段名称虚拟字段：折线主值字段的中文显示名（在用户拖拽的位置产生静态行）。
                var lineChart = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.Line);
                var valueField = lineChart?.DataGroups.SelectMany(g => g.Fields).FirstOrDefault(fd => fd.Role == FieldRole.Value)?.FieldName;
                if (valueField != null) lines.Add(TooltipFieldCatalog.GetDisplay(valueField));
                continue;
            }
            var line = BuildTooltipFieldLine(f);
            if (line != null) lines.Add(line);
        }
        return lines.Count > 0 ? lines : null;
    }

    /// <summary>req-105：单个 tooltip 字段行（“标签 当前值”）；字段无值或不支持时返回 null。</summary>
    private string? BuildTooltipFieldLine(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.FiveHourUsedPercent => $"5h 已用 {PrimaryBarPercent:0}%",
        UsageMonitor.Core.Models.UsageFields.WeeklyUsedPercent => $"本周已用 {WeeklyBarPercent:0}%",
        UsageMonitor.Core.Models.UsageFields.RemainingCredits => _remainingCredits >= 0 ? $"剩余积分 {_remainingCredits:0}" : null,
        UsageMonitor.Core.Models.UsageFields.VideoUsedCount => VideoIntervalPercent > 0 ? $"视频赠送 {VideoIntervalPercent:0}%" : null,
        _ => null
    };

    // ============== REQ-083 SDK v2 新增可选属性（委托给 Provider） ==============

    /// <summary>
    /// req-107 B8 渲染消费方：从插件 <see cref="IUsageProvider.Card"/> 显示声明（defaults.json）构建的声明式进度条数据。
    /// <para>遍历 Card 声明的 Bar 数据组，按 <see cref="FieldReference"/> 字段名经 <see cref="ResolveFieldValue"/> 解析为值，
    /// 动态生成进度条（取代硬编码 5h/周/视频）。无 Card 声明时为 null，宿主回退旧渲染路径。</para>
    /// </summary>
    public UsageMonitor.Core.Models.MetricBarData? DeclarativeBars
    {
        get => _declarativeBars;
        private set { _declarativeBars = value; OnPropertyChanged(); }
    }
    private UsageMonitor.Core.Models.MetricBarData? _declarativeBars;

    /// <summary>req-107 B8：渲染消费方（折线图）——Card 声明含 Line chart 时从 CardLineValues 构建 LineChartData；无声明返回 null。</summary>
    public UsageMonitor.Core.Models.LineChartData? DeclarativeLineChart
    {
        get
        {
            if (Provider?.Card == null) return null;
            if (!Provider.Card.Charts.Any(c => c.Kind == DeclarativeChartKind.Line)) return null;
            var values = _cardLineValues ?? Array.Empty<double>();
            var max = _cardLineMax > 0 ? _cardLineMax : (double?)null;
            return new UsageMonitor.Core.Models.LineChartData(values, max);
        }
    }

    /// <summary>req-107 B8：折线图实际渲染值——优先声明式（Card 声明 Line 时），否则用旧 CardLineValues（向后兼容）。
    /// 绑定 <c>MiniLineChartControl.Values</c> 时单绑定即可，无需 MultiBinding fallback。
    /// <para>**过渡期**优先源：①Card 声明（Line chart） ②<see cref="CardLineValues"/>（旧路径，已被 SliceCardLineByPeriod 填入）。
    /// **最终态**：Card 声明路径 + 周期切片由声明驱动后，将移除 CardLineValues。</para></summary>
    public IReadOnlyList<double> EffectiveCardLineValues => DeclarativeLineChart?.Values ?? _cardLineValues;

    /// <summary>req-107 B8：折线图 X 轴日期——与 <see cref="EffectiveCardLineValues"/> 同源（声明式 / 旧路径）。</summary>
    public IReadOnlyList<string> EffectiveCardLineDates => _dates;

    /// <summary>req-107 B8：折线图 Y 轴上限——优先声明式，否则用旧 CardLineMax。</summary>
    public double EffectiveCardLineMax => DeclarativeLineChart?.MaxValue ?? _cardLineMax;

    /// <summary>req-107 B8：热力图实际单元格——统一从声明式数据（_fullDailyValues/_fullDailyDates/_fullDailyCacheHitPercents）构建。
    /// <para>声明式接管：Card 声明含 HeatMap chart 时，EffectiveHeatMapCells 从全量数据按日生成 YearHeatMapCell，
    /// 含 Day/Percent/Token/Background/ValueText/ComparisonText，YearHeatMapControl 直接绑定。
    /// RecolorHeatMapCells 通过 cell.Token 重算背景色仍兼容（声明式构建时已写入 Token）。</para></summary>
    public System.Collections.ObjectModel.ObservableCollection<UsageMonitor.App.Controls.YearHeatMapCell> EffectiveHeatMapCells
    {
        get
        {
            var dh = DeclarativeHeatMap;
            if (dh != null && HeatMapCells.Count > 0) return HeatMapCells; // 声明式路径由 RebuildDeclarativeHeatMap 填 HeatMapCells
            return HeatMapCells;
        }
    }

    /// <summary>req-107 B8：声明驱动卡片是否有 Card 声明（true 时各图表属性接管渲染；false 回退旧路径）。</summary>
    public bool HasDeclarativeCardCharts => Provider?.Card != null && Provider.Card.Charts.Count > 0;

    /// <summary>req-107 B8：渲染消费方（热力图）——Card 声明含 HeatMap chart 时从 HeatMapCells 构建 HeatMapData；无声明返回 null。</summary>
    public UsageMonitor.Core.Models.HeatMapData? DeclarativeHeatMap
    {
        get
        {
            if (Provider?.Card == null) return null;
            if (!Provider.Card.Charts.Any(c => c.Kind == DeclarativeChartKind.HeatMap)) return null;
            if (HeatMapCells.Count == 0) return null;
            var cells = HeatMapCells.Select(c => c.Percent).ToList();
            return new UsageMonitor.Core.Models.HeatMapData(cells, HeatMapCells.Count, 1, MinValue: 0, MaxValue: 100);
        }
    }

    /// <summary>req-107 B8：渲染消费方（数字图）——Card 声明含 Number chart 时从声明字段取值并构建 MetricGridData；无声明返回 null。</summary>
    public UsageMonitor.Core.Models.MetricGridData? DeclarativeNumber
    {
        get
        {
            if (Provider?.Card == null) return null;
            var numberChart = Provider.Card.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.Number);
            if (numberChart == null) return null;
            var firstGroup = numberChart.DataGroups.FirstOrDefault();
            var valueField = firstGroup?.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
            if (valueField == null) return null;
            var val = ResolveFieldValue(valueField);
            if (!val.HasValue) return null;
            var label = DeclarativeFieldLabel(valueField);
            return new UsageMonitor.Core.Models.MetricGridData(new[]
            {
                new UsageMonitor.Core.Models.MetricGridItem(label, $"{val.Value:0}")
            });
        }
    }

    /// <summary>req-107 B8：从 Card 声明重建声明式进度条（字段标签由 SDK 元数据/i18n 提供，插件零翻译）。</summary>
    private void RebuildDeclarativeBars()
    {
        var card = Provider?.Card;
        var (visibleCharts, visibleDataGroups) = GetEffectiveChartFilters();
        DeclarativeBars = UsageMonitor.App.Services.Display.DeclarativeChartBuilder.BuildMetricBars(
            card, ResolveFieldValue, labelResolver: DeclarativeFieldLabel,
            visibleChartIds: visibleCharts, visibleDataGroupIds: visibleDataGroups);
    }

    /// <summary>req-107 B6 演进：从 ConfigService 拉取当前 Provider 的有效可见图表/数据组，过滤传给 DeclarativeChartBuilder。
    /// <para>null = 沿用 defaults.json 全部可见（未配置）。</para>
    /// </summary>
    private (IReadOnlyCollection<string>? Charts, IReadOnlyDictionary<string, IReadOnlyCollection<string>>? DataGroups) GetEffectiveChartFilters()
    {
        if (ConfigService == null || string.IsNullOrEmpty(_providerId))
            return (null, null);
        var eff = ConfigService.GetEffectiveAccountCustomization(_providerId, _accountId, _cardId);
        if (eff.VisibleCharts == null && (eff.VisibleDataGroups == null || eff.VisibleDataGroups.Count == 0))
            return (null, null);
        var dict = new Dictionary<string, IReadOnlyCollection<string>>();
        if (eff.VisibleDataGroups != null)
        {
            foreach (var kv in eff.VisibleDataGroups)
            {
                if (kv.Value != null) dict[kv.Key] = kv.Value;
            }
        }
        return (eff.VisibleCharts, dict.Count > 0 ? dict : null);
    }

    // =====================================================================
    // 卡片图表槽位列表（统一由 AccountCustomization 驱动的有序图表实例）
    // =====================================================================

    /// <summary>卡片图表区的有序图表槽位集合（声明式插件）。
    /// <para>每个槽位对应 <c>Card.Charts</c> 声明的一个图表实例，可见性/顺序/数据组/折叠分界线
    /// 均由 <c>AccountCustomization</c>（卡片管理页）驱动。非声明式插件时为空（回退旧固定图表栈）。</para>
    /// </summary>
    public ObservableCollection<CardChartSlotViewModel> CardChartSlots { get; } = new();

    /// <summary>是否存在图表槽位（声明式插件且至少一个可见图表时为 true，驱动卡片图表区 ItemsControl 显隐）。</summary>
    public bool HasChartSlots => CardChartSlots.Count > 0;

    /// <summary>上一次槽位结构签名（实例序列+分界线位置），未变时仅原位刷新数据避免控件重建。</summary>
    private string _lastSlotStructureKey = string.Empty;

    /// <summary>
    /// 重建/刷新卡片图表槽位列表（<see cref="CardChartSlots"/>）。
    /// <para>结构（可见实例序列 + 折叠分界线位置）未变时仅原位刷新 Bar/Number 数据，
    /// 避免折线/热力图等控件重建引起闪烁；结构变化时整体重建。</para>
    /// <para>调用时机：配置变更（卡片管理保存）、数据刷新（UpdateFromUsage）、首次装配。</para>
    /// </summary>
    public void RebuildChartSlots()
    {
        // ObservableCollection 绑定 UI，非 UI 线程时调度回 UI 线程。
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(RebuildChartSlots));
            return;
        }

        var card = Provider?.Card;
        if (card == null || card.Charts.Count == 0)
        {
            if (CardChartSlots.Count > 0) CardChartSlots.Clear();
            _lastSlotStructureKey = string.Empty;
            OnPropertyChanged(nameof(HasChartSlots));
            return;
        }

        var eff = (ConfigService != null && !string.IsNullOrEmpty(_providerId))
            ? ConfigService.GetEffectiveAccountCustomization(_providerId, AccountIdSafe, CardIdSafe)
            : new AccountCustomization();

        // req-105：同步折线图 tooltip 扩展行（配置/数值变更后即时生效）。
        RefreshLineTooltip();

        // 问题3：折线图周期切片器由插件声明驱动（Card.Line.Slicer(Period)），配置/数据刷新后同步。
        ApplyDeclaredPeriodSlicer();

        var orderedInstances = ResolveOrderedInstances(card, eff);
        var dividerIndex = eff.CollapseDividerIndex ?? card.CollapseDividerIndex ?? orderedInstances.Count;
        if (dividerIndex < 0) dividerIndex = 0;
        if (dividerIndex > orderedInstances.Count) dividerIndex = orderedInstances.Count;
        // 问题5：分界线处于图表列表中部时插入浅灰色横线槽位（首/尾位置无分界意义不渲染）。
        var showDivider = dividerIndex > 0 && dividerIndex < orderedInstances.Count;
        var expectedSlotCount = orderedInstances.Count + (showDivider ? 1 : 0);

        var structureKey = string.Join("|", orderedInstances) + "@" + dividerIndex;
        if (structureKey == _lastSlotStructureKey && CardChartSlots.Count == expectedSlotCount)
        {
            // 结构未变：仅原位刷新 Bar/Number 数据（折线/热力图经 Owner 级 INPC 自更新；分界线槽位无数据跳过）。
            for (var i = 0; i < CardChartSlots.Count; i++)
            {
                if (CardChartSlots[i].IsDivider) continue;
                RefreshSlotData(CardChartSlots[i], card, eff);
            }
            return;
        }

        _lastSlotStructureKey = structureKey;
        CardChartSlots.Clear();
        var declaredById = card.Charts.ToDictionary(c => c.ChartId, c => c);
        for (var i = 0; i < orderedInstances.Count; i++)
        {
            // 问题5：到达分界位置时先插入折叠分界线槽位（折叠态随下方图表一同隐藏）。
            if (showDivider && i == dividerIndex)
                CardChartSlots.Add(CardChartSlotViewModel.CreateDivider(this, CardChartSlots.Count));
            var instanceId = orderedInstances[i];
            var baseId = StripInstanceSuffix(instanceId);
            if (!declaredById.TryGetValue(baseId, out var decl)) continue;
            var slot = new CardChartSlotViewModel(this, instanceId, baseId, decl.Kind, i, i < dividerIndex);
            RefreshSlotData(slot, card, eff);
            CardChartSlots.Add(slot);
        }
        OnPropertyChanged(nameof(HasChartSlots));
    }

    /// <summary>解析有序图表实例 ID 列表（用户配置优先，回退声明默认顺序）。
    /// <para>用户配置 <c>VisibleCharts</c> 为可见实例的有序列表（不在其中的图表视为隐藏）；
    /// 旧兼容层可能填入 CardChartKind 名（如 "Line"），与声明 chartId 不匹配时回退声明顺序。</para>
    /// </summary>
    private static List<string> ResolveOrderedInstances(CardDeclaration card, AccountCustomization eff)
    {
        var declaredIds = new HashSet<string>(card.Charts.Select(c => c.ChartId), StringComparer.Ordinal);
        var result = new List<string>();
        if (eff.VisibleCharts != null && eff.VisibleCharts.Count > 0)
        {
            foreach (var inst in eff.VisibleCharts)
            {
                if (string.IsNullOrEmpty(inst)) continue;
                if (declaredIds.Contains(StripInstanceSuffix(inst)) && !result.Contains(inst))
                    result.Add(inst);
            }
        }
        // 用户未配置（或旧兼容层填入的类型名全部不匹配 chartId）时，回退声明默认顺序。
        if (result.Count == 0)
        {
            foreach (var c in card.Charts.OrderBy(c => c.DefaultOrder))
                result.Add(c.ChartId);
        }
        return result;
    }

    /// <summary>去除图表实例 ID 的 <c>#n</c> 后缀，返回基础 chartId。</summary>
    private static string StripInstanceSuffix(string instanceId)
    {
        var idx = instanceId.LastIndexOf('#');
        return idx > 0 ? instanceId.Substring(0, idx) : instanceId;
    }

    /// <summary>原位刷新指定槽位的 Bar/Number 数据（结构不变时调用，避免控件重建）。
    /// <para>问题8：空态提示区分两种原因——数据组全部未勾选 vs 数据尚未到达（刷新后自动恢复）。</para></summary>
    private void RefreshSlotData(CardChartSlotViewModel slot, CardDeclaration card, AccountCustomization eff)
    {
        if (slot.Kind == DeclarativeChartKind.Bar)
        {
            slot.BarData = BuildInstanceBars(slot.ChartId, slot.InstanceId, card, eff);
            slot.EmptyHint = slot.BarData != null ? null
                : (HasVisibleGroups(slot.ChartId, slot.InstanceId, card, eff) ? "暂无数据，等待刷新…" : "未配置数据组，请在卡片管理中勾选");
        }
        else if (slot.Kind == DeclarativeChartKind.Number)
        {
            slot.NumberData = BuildInstanceNumber(slot.ChartId, slot.InstanceId, card, eff);
            // 问题4：数据概览 tooltip 改为数据项级（在 BuildInstanceNumber 内按组生成），槽位级 tooltip 不再使用。
            slot.TooltipText = null;
            slot.EmptyHint = slot.NumberData != null ? null
                : (HasVisibleGroups(slot.ChartId, slot.InstanceId, card, eff) ? "暂无数据，等待刷新…" : "未配置数据组，请在卡片管理中勾选");
        }
    }

    /// <summary>问题4：构建数据概览单个数据项的悬停提示文本——按用户勾选的 tooltip 字段顺序，
    /// 但仅保留属于本数据组的字段（避免活跃天数项错误展示累计用量等其它组字段）。
    /// <para>「字段名称」虚拟字段 → 本项标签行；值字段 → “SDK 显示名 值”行（不属于本组/取不到值的字段跳过）；
    /// 无可展示内容时返回 null（不显示 tooltip）。</para></summary>
    private string? BuildGroupTooltipText(IReadOnlyList<string>? fields, DataGroup group, string itemLabel)
    {
        if (fields == null || fields.Count == 0) return null;
        var groupFieldNames = new HashSet<string>(group.Fields.Select(f => f.FieldName), StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var f in fields)
        {
            if (string.Equals(f, TooltipFieldCatalog.DateVirtual, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(f, TooltipFieldCatalog.FieldNameVirtual, StringComparison.OrdinalIgnoreCase))
            {
                // 字段名称行：本数据项的显示标签（如 "活跃天数"）。
                lines.Add(itemLabel);
                continue;
            }
            // 问题4：仅展示本数据组声明的字段，其它组的字段不混入。
            if (!groupFieldNames.Contains(f)) continue;
            var valueText = ResolveFieldDisplay(f);
            if (valueText != null) lines.Add($"{TooltipFieldCatalog.GetDisplay(f)} {valueText}");
        }
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    /// <summary>解析图表实例的 tooltip 字段配置（实例级优先，回退图表级；null = 用户未配置）。</summary>
    private static IReadOnlyList<string>? ResolveInstanceTooltipFields(string chartId, string instanceId, AccountCustomization eff)
    {
        if (eff.VisibleTooltipFields == null) return null;
        if (!string.Equals(instanceId, chartId, StringComparison.Ordinal) &&
            eff.VisibleTooltipFields.TryGetValue(instanceId, out var inst) && inst != null)
            return inst;
        return eff.VisibleTooltipFields.TryGetValue(chartId, out var chart) ? chart : null;
    }

    /// <summary>按指定 Bar 图表实例的可见数据组构建进度条数据（并按数据组注入 req-105 tooltip 文本）。</summary>
    private MetricBarData? BuildInstanceBars(string chartId, string instanceId, CardDeclaration card, AccountCustomization eff)
    {
        var groups = ResolveInstanceDataGroups(chartId, instanceId, eff);
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? dgFilter = null;
        if (groups != null)
            dgFilter = new Dictionary<string, IReadOnlyCollection<string>> { [chartId] = groups };
        // req-105：tooltip 按数据组构建——每个进度条只包含属于自己数据组的字段，
        // 避免全部进度条共享同一 tooltip 导致跨数据组字段串扰（如本周限额进度条显示 5h 已用百分比）。
        var barData = UsageMonitor.App.Services.Display.DeclarativeChartBuilder.BuildMetricBars(
            card, ResolveFieldValue, labelResolver: DeclarativeFieldLabel,
            visibleChartIds: new[] { chartId }, visibleDataGroupIds: dgFilter,
            tooltipBuilder: group => BuildBarTooltipForGroup(chartId, group),
            resetTextResolver: ResolveResetText);
        return barData;
    }

    /// <summary>问题4：Reset 角色字段 → 重置剩余文案（如 "2 小时 21 分钟后重置"），供进度条 FooterText 展示。</summary>
    private string? ResolveResetText(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.FiveHourResetAt => string.IsNullOrWhiteSpace(PrimaryResetText) || PrimaryResetText == "--" ? null : PrimaryResetText,
        UsageMonitor.Core.Models.UsageFields.WeeklyResetAt => string.IsNullOrWhiteSpace(WeeklyResetText) || WeeklyResetText == "--" ? null : WeeklyResetText,
        _ => null
    };

    /// <summary>req-105：构建单个进度条（数据组）的悬停提示文本。
    /// <para>仅包含该数据组自己的 Value 字段（被勾选时）与虚拟字段（字段名称/日期），
    /// 不混入其他数据组的字段。字段名称与值分行展示。</para>
    /// </summary>
    private string? BuildBarTooltipForGroup(string chartId, DataGroup group)
    {
        var fields = GetEffectiveTooltipFieldsForChart(chartId);
        var valueField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
        // 问题3：无任何字段配置（用户未配置且声明缺失）时回退默认 tooltip（字段名 + 当前值），
        // 保证进度条悬停始终有提示；空集合 = 用户主动全取消，不显示 tooltip。
        if (fields == null)
        {
            if (valueField == null) return null;
            var v = BuildTooltipFieldValue(valueField);
            var name = TooltipFieldCatalog.GetDisplay(valueField);
            return v == null ? name : $"{name}\n{v}";
        }
        if (fields.Count == 0) return null;
        var lines = BuildTooltipLines(fields, valueField, dateText: null);
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    /// <summary>req-105：构建 tooltip 行列表（字段名称行·虚拟 + 日期行·虚拟 + 当前上下文字段的值行）。
    /// <para>「字段名称」勾选时显示当前字段的中文显示名（独立行）；「日期」勾选时显示日期行；
    /// 上下文字段被勾选时显示其值行。三者分行排列，实现“字段名称 + 值”分行展示效果。</para>
    /// </summary>
    /// <param name="checkedFields">用户勾选的 tooltip 字段（含虚拟字段）。</param>
    /// <param name="contextValueField">当前上下文的 Value 字段名（进度条 = 所属数据组 Value 字段；折线/热力图 = 图表 Value 字段）。</param>
    /// <param name="dateText">日期文本（可空；折线/热力图逐点日期由控件自处理，此处传 null）。</param>
    private List<string> BuildTooltipLines(IReadOnlyList<string> checkedFields, string? contextValueField, string? dateText)
    {
        var lines = new List<string>();
        // 问题9：按用户保存的字段顺序逐一生成行（卡片管理页拖拽排序后顺序即时生效）。
        foreach (var f in checkedFields)
        {
            if (string.Equals(f, TooltipFieldCatalog.FieldNameVirtual, StringComparison.OrdinalIgnoreCase))
            {
                // 字段名称虚拟字段：显示当前上下文字段的中文显示名（独立行）。
                if (contextValueField != null) lines.Add(TooltipFieldCatalog.GetDisplay(contextValueField));
            }
            else if (string.Equals(f, TooltipFieldCatalog.DateVirtual, StringComparison.OrdinalIgnoreCase))
            {
                // 日期虚拟字段：显示日期行。
                if (!string.IsNullOrEmpty(dateText)) lines.Add(dateText!);
            }
            else if (contextValueField != null && string.Equals(f, contextValueField, StringComparison.OrdinalIgnoreCase))
            {
                // 值行：仅当上下文字段被勾选时显示。
                var value = BuildTooltipFieldValue(contextValueField);
                if (value != null) lines.Add(value);
            }
        }
        return lines;
    }

    /// <summary>req-105：字段 tooltip 值格式化（仅值本身，不含标签；标签由「字段名称」虚拟字段提供）。取不到返回 null。</summary>
    private string? BuildTooltipFieldValue(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.FiveHourUsedPercent => $"{PrimaryBarPercent:0}%",
        UsageMonitor.Core.Models.UsageFields.WeeklyUsedPercent => $"{WeeklyBarPercent:0}%",
        UsageMonitor.Core.Models.UsageFields.RemainingCredits => _remainingCredits >= 0 ? $"{_remainingCredits:0}" : null,
        UsageMonitor.Core.Models.UsageFields.VideoQuota => $"{VideoIntervalPercent:0}%",
        UsageMonitor.Core.Models.UsageFields.VideoUsedCount => VideoIntervalPercent > 0 ? $"{VideoIntervalPercent:0}%" : null,
        _ => ResolveFieldValue(fieldName)?.ToString("0")
    };

    /// <summary>按指定 Number 图表实例的数据组构建数字网格数据。
    /// <para>问题6/7：三行均由声明字段驱动——第一行 = Value 字段的 SDK 显示名（回退组 display）；
    /// 第二行 = Value 字段值，若组声明 Upper 角色字段则渲染为 "分子/分母"；
    /// 第三行 = Meta 角色字段文本（备注，可缺省）。</para></summary>
    private MetricGridData? BuildInstanceNumber(string chartId, string instanceId, CardDeclaration card, AccountCustomization eff)
    {
        var decl = card.Charts.FirstOrDefault(c => c.ChartId == chartId && c.Kind == DeclarativeChartKind.Number);
        if (decl == null) return null;
        var groups = ResolveInstanceDataGroups(chartId, instanceId, eff);
        // 问题4：tooltip 字段配置（用户优先，回退声明），按数据组拆分到各数据项。
        var tooltipFields = ResolveInstanceTooltipFields(chartId, instanceId, eff) ?? decl.Tooltip?.Fields;
        var orderedGroups = decl.DataGroups.AsEnumerable();
        if (groups != null) orderedGroups = orderedGroups.Where(g => groups.Contains(g.Id));
        var items = new List<MetricGridItem>();
        foreach (var g in orderedGroups)
        {
            var valueField = g.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
            if (valueField == null) continue;
            var display = ResolveFieldDisplay(valueField);
            if (display == null) continue;
            // 问题6：组内声明 Upper 角色字段时拼接 "分子/分母"；未声明则仅显示单值。
            var upperField = g.Fields.FirstOrDefault(f => f.Role == FieldRole.Upper)?.FieldName;
            if (upperField != null)
            {
                var upperDisplay = ResolveFieldDisplay(upperField);
                if (upperDisplay != null) display = $"{display}/{upperDisplay}";
            }
            // 问题7：第一行数据名称优先取 Value 字段的 SDK 显示名，无映射时回退组 display / 内置标签。
            var fieldDisplay = TooltipFieldCatalog.GetDisplay(valueField);
            var label = !string.Equals(fieldDisplay, valueField, StringComparison.Ordinal)
                ? fieldDisplay
                : (!string.IsNullOrWhiteSpace(g.Display) ? g.Display! : DeclarativeFieldLabel(valueField));
            // 问题7：第三行备注 = Meta 角色字段文本（取不到时不显示）。
            var metaField = g.Fields.FirstOrDefault(f => f.Role == FieldRole.Meta)?.FieldName;
            var detail = metaField != null ? ResolveFieldNoteText(metaField) : null;
            // 问题4：每个数据项携带自身数据组的独立 tooltip。
            var tooltip = BuildGroupTooltipText(tooltipFields, g, label);
            items.Add(new MetricGridItem(label, display, detail, Tooltip: tooltip));
        }
        return items.Count > 0 ? new MetricGridData(items) : null;
    }

    /// <summary>问题7：Meta 角色备注字段文本解析（数据概览第三行）；取不到返回 null。</summary>
    private string? ResolveFieldNoteText(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.MostActiveDate => string.IsNullOrWhiteSpace(_numberMostActiveDay) ? null : _numberMostActiveDay,
        UsageMonitor.Core.Models.UsageFields.TotalDays => _numberTotalDays > 0 ? $"共 {_numberTotalDays} 天" : null,
        _ => null
    };

    /// <summary>问题8：判断指定图表实例是否至少有一个可见数据组（区分 "未配置数据组" 与 "数据未到达" 两种空态）。</summary>
    private static bool HasVisibleGroups(string chartId, string instanceId, CardDeclaration card, AccountCustomization eff)
    {
        var decl = card.Charts.FirstOrDefault(c => c.ChartId == chartId);
        if (decl == null || decl.DataGroups.Count == 0) return false;
        var groups = ResolveInstanceDataGroups(chartId, instanceId, eff);
        if (groups == null) return true; // 未配置 = 全部可见
        return decl.DataGroups.Any(g => groups.Contains(g.Id));
    }

    /// <summary>解析图表实例的可见数据组 ID 列表（实例级配置优先，回退图表级，再回退 null=全部可见）。</summary>
    private static List<string>? ResolveInstanceDataGroups(string chartId, string instanceId, AccountCustomization eff)
    {
        if (eff.VisibleDataGroups != null)
        {
            if (!string.Equals(instanceId, chartId, StringComparison.Ordinal) &&
                eff.VisibleDataGroups.TryGetValue(instanceId, out var instGroups))
                return instGroups;
            if (eff.VisibleDataGroups.TryGetValue(chartId, out var chartGroups))
                return chartGroups;
        }
        return null;
    }

    /// <summary>req-107 B8：字段取值器——标准字段名 → 当前值（过渡期映射到已刷新的 VM 属性，后续可改从标准字段字典泛化解析）。</summary>
    private double? ResolveFieldValue(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.FiveHourUsedPercent => PrimaryBarPercent,
        UsageMonitor.Core.Models.UsageFields.WeeklyUsedPercent => WeeklyBarPercent,
        UsageMonitor.Core.Models.UsageFields.VideoQuota => VideoIntervalPercent,
        UsageMonitor.Core.Models.UsageFields.RemainingCredits => RemainingCredits,
        _ => null
    };

    /// <summary>req-107 B8：字段显示标签解析（过渡期内置中文标签，后续接 I18n + SDK 元数据 LabelKey）。</summary>
    private static string DeclarativeFieldLabel(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.FiveHourUsedPercent => "5h 限额",
        UsageMonitor.Core.Models.UsageFields.WeeklyUsedPercent => "本周限额",
        UsageMonitor.Core.Models.UsageFields.VideoQuota => "视频赠送",
        UsageMonitor.Core.Models.UsageFields.RemainingCredits => "剩余积分",
        _ => fieldName
    };

    /// <summary>Number 图表字段显示值解析器：标准字段名 → 格式化显示文本（数据概览多数据组，取不到返回 null 跳过）。
    /// <para>问题6：ActiveDays 仅返回单值（如 "40"），不再硬拼 "40/45"；分母由声明的 Upper 角色字段（total_days）驱动。</para></summary>
    private string? ResolveFieldDisplay(string fieldName) => fieldName switch
    {
        UsageMonitor.Core.Models.UsageFields.UsedTokens => string.IsNullOrWhiteSpace(_numberCumulativeText) ? null : _numberCumulativeText,
        UsageMonitor.Core.Models.UsageFields.MostActiveToken => _numberPeakToken >= 0 ? FormatTokenNumber(_numberPeakToken) : null,
        UsageMonitor.Core.Models.UsageFields.ActiveDays => _numberActiveDays > 0 ? _numberActiveDays.ToString() : null,
        UsageMonitor.Core.Models.UsageFields.TotalDays => _numberTotalDays > 0 ? _numberTotalDays.ToString() : null,
        UsageMonitor.Core.Models.UsageFields.RemainingCredits => _remainingCredits >= 0 ? _remainingCredits.ToString("0") : null,
        _ => ResolveFieldValue(fieldName)?.ToString("0")
    };

    /// <summary>将 Token 数值格式化为人类可读形式（如 552.49M / 5.85B）。</summary>
    private static string FormatTokenNumber(double value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000:0.00}B";
        if (value >= 1_000_000) return $"{value / 1_000_000:0.00}M";
        if (value >= 1_000) return $"{value / 1_000:0.00}K";
        return $"{value:0}";
    }

    /// <summary>
    /// Provider 注入的"V2 度量进度条组"数据（REQ-083）。
    /// 返回 null 时主窗口 <c>ChartCardTemplateSelector</c> 自动回退到旧 CardLimitBarsTemplate。
    /// req-104：按用户选择的字段过滤。
    /// </summary>
    public UsageMonitor.Core.Models.MetricBarData? CardMetricBarData
    {
        get
        {
            // req-107 B8：三插件已完成声明式迁移（MiniMax/Kimi/Deepseek），优先用 Card 声明构建的 DeclarativeBars；
            // 旧插件无 Card 声明时 DeclarativeBars 为 null，宿主走主窗口旧 XAML 模板（不在本 VM 范围内）。
            var data = DeclarativeBars;
            if (data == null || ConfigService == null) return data;

            // req-104：按用户选择过滤进度条字段
            if (!ConfigService.Settings.SelectedProgressFields.TryGetValue(_providerId, out var selectedFields))
                return data; // 未配置时显示全部

            if (selectedFields.Count == 0)
                return data; // 空列表时显示全部

            var filteredBars = data.Bars.Where(b => selectedFields.Contains(b.Label)).ToList();
            return new UsageMonitor.Core.Models.MetricBarData(filteredBars);
        }
    }

    /// <summary>
    /// Provider 注入的"V2 度量数字网格"数据（REQ-083）。
    /// 返回 null 时主窗口 <c>ChartCardTemplateSelector</c> 自动回退到旧 CardBalanceTemplate。
    /// req-104：按用户选择的字段过滤。
    /// </summary>
    public UsageMonitor.Core.Models.MetricGridData? CardMetricGridData
    {
        get
        {
            // req-107 B8：三插件已迁移到 DeclarativeNumber（Card 声明根），回退路径已废弃。
            var data = DeclarativeNumber;
            if (data == null || ConfigService == null) return data;

            // req-104：按用户选择过滤数字网格字段
            if (!ConfigService.Settings.SelectedMetricFields.TryGetValue(_providerId, out var selectedFields))
                return data; // 未配置时显示全部

            if (selectedFields.Count == 0)
                return data; // 空列表时显示全部

            var filteredItems = data.Items.Where(i => selectedFields.Contains(i.Label)).ToList();
            return new UsageMonitor.Core.Models.MetricGridData(filteredItems);
        }
    }

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

    /// <summary>插件是否声明支持周期切换（req-007）。为 true 时卡片折线图右上角显示周期切片器按钮。</summary>
    public bool SupportsPeriodSwitch
    {
        get => _supportsPeriodSwitch;
        set { _supportsPeriodSwitch = value; OnPropertyChanged(); }
    }

    /// <summary>问题3：折线图周期切片器选项（由插件 slicer.timeRanges 声明派生；null = 未声明，控件回退内置两档）。</summary>
    public IReadOnlyList<UsageMonitor.App.Controls.PeriodOption>? PeriodOptions
    {
        get => _periodOptions;
        private set { if (!ReferenceEquals(_periodOptions, value)) { _periodOptions = value; OnPropertyChanged(); } }
    }
    private IReadOnlyList<UsageMonitor.App.Controls.PeriodOption>? _periodOptions;
    // 声明默认周期只应用一次，避免每次刷新覆盖用户手动切换的周期。
    private bool _periodInitializedFromDeclaration;

    /// <summary>问题3：当前插件是否在 Card 声明中为折线图声明了 Period 模式切片器。</summary>
    private bool HasDeclaredPeriodSlicer
    {
        get
        {
            var slicer = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.Line)?.Slicer;
            return slicer is { Mode: SlicerMode.Period } && slicer.TimeRanges.Count > 0;
        }
    }

    /// <summary>
    /// 问题3：把插件声明的折线图 Period 切片器（slicer.timeRanges / default）应用到 VM。
    /// <para>声明存在 → SupportsPeriodSwitch=true + PeriodOptions 派生；首次应用时按 slicer.default 初始化
    /// <see cref="CurrentPeriod"/> 并切片；无声明 → 关闭切片器。周期键与选项均由声明驱动，不再写死。</para>
    /// </summary>
    private void ApplyDeclaredPeriodSlicer()
    {
        var slicer = Provider?.Card?.Charts.FirstOrDefault(c => c.Kind == DeclarativeChartKind.Line)?.Slicer;
        if (slicer is not { Mode: SlicerMode.Period } || slicer.TimeRanges.Count == 0)
        {
            PeriodOptions = null;
            if (_supportsPeriodSwitch) SupportsPeriodSwitch = false;
            return;
        }

        var options = new List<UsageMonitor.App.Controls.PeriodOption>();
        foreach (var range in slicer.TimeRanges)
        {
            var option = UsageMonitor.App.Controls.ChartPeriods.FromTimeRange(range);
            if (option != null && !options.Any(o => o.Period == option.Period)) options.Add(option);
        }
        PeriodOptions = options.Count > 0 ? options : null;
        SupportsPeriodSwitch = options.Count > 0;

        // 默认周期：仅首次应用声明时初始化，后续刷新不覆盖用户手动选择。
        if (!_periodInitializedFromDeclaration && options.Count > 0)
        {
            _periodInitializedFromDeclaration = true;
            string? defKey = null;
            if (!string.IsNullOrEmpty(slicer.Default) && Enum.TryParse<TimeRange>(slicer.Default, true, out var tr))
                defKey = UsageMonitor.App.Controls.ChartPeriods.FromTimeRange(tr)?.Period;
            var target = defKey != null && options.Any(o => string.Equals(o.Period, defKey, StringComparison.OrdinalIgnoreCase))
                ? defKey
                : options[^1].Period;
            if (!string.Equals(_currentPeriod, target, StringComparison.OrdinalIgnoreCase))
            {
                CurrentPeriod = target;
                SliceCardLineByPeriod(target);
            }
        }
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
    /// req-079 U-33：是否已收到过至少一次数据更新响应（无论成败）。
    /// <para>供任务栏迷你图骨架屏判定：<c>MiniChartItemViewModel.IsDataReady</c> 以此为准，
    /// false 且刷新中时显示骨架占位，避免“无缓存数据”时的空白闪烁。</para>
    /// </summary>
    public bool HasReceivedData
    {
        get => _hasReceivedData;
        set
        {
            if (_hasReceivedData == value) return;
            _hasReceivedData = value;
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
    /// <para>绑定到 RingChartControl.EnabledMetrics，由 <c>MainViewModel.SyncGlobalEnabledMetricsToAllProviders</c>
    /// 在用户勾选变更时同步。null / 空集合表示“全部启用”，
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
            OnPropertyChanged(nameof(EffectiveCardLineValues));
            // 问题3：Provider 注入后立即同步声明的折线图周期切片器（不等首次 RebuildChartSlots）。
            ApplyDeclaredPeriodSlicer();
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
            // req-107 B8：SetPeriodAsync 接口成员已收敛，VM 端按 period 切片缓存数据即可
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
    /// 问题1/9：启动时把持久化的最近字段快照（usage_field_versions）回填到卡片显示。
    /// <para>仅在尚未收到实时刷新数据时生效（<see cref="HasReceivedData"/> 为 true 时跳过，避免旧快照覆盖新数据）；
    /// 快照统一封装为 UsageInfo 走 <see cref="UpdateFromUsage"/> 的声明式渲染路径，
    /// 回填数据概览 / 进度条 / five_hour_reset_at（5h 倒计时启动即开始走秒）等；
    /// <see cref="LastUpdateText"/> 显示快照时间（含日期），避免被误认为实时数据。</para>
    /// </summary>
    /// <param name="fields">标准字段名 → 最新值字典（来自 <c>IDataModule.GetLatestFieldsAsync</c>）。</param>
    public void RestoreFromFieldSnapshot(IReadOnlyDictionary<string, object> fields)
    {
        if (fields == null || fields.Count == 0) return;
        if (HasReceivedData) return; // 已收到实时数据，跳过恢复（防止异步恢复晚于首次刷新）

        // 最近一次刷新失败的快照不恢复（避免启动就显示错误态）
        if (fields.TryGetValue(UsageMonitor.Core.Models.UsageFields.IsSuccess, out var ok) && ok is bool okFlag && !okFlag) return;

        var extra = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in fields) extra[kv.Key] = kv.Value;

        var snapshotTime = fields.TryGetValue(UsageMonitor.Core.Models.UsageFields.LastUpdated, out var lu) && lu is DateTime luDt
            ? luDt : DateTime.Now;

        var usage = new UsageInfo
        {
            ProviderId = _providerId,
            ProviderName = DisplayName,
            AccountId = _accountId,
            IsSuccess = true,
            Extra = extra,
            LastUpdated = snapshotTime
        };

#pragma warning disable CS0618 // 旧量值字段回填（API 型插件的回退渲染路径依赖）
        if (TryReadDouble(fields, UsageMonitor.Core.Models.UsageFields.UsedAmount, out var usedAmount)) usage.UsedAmount = (decimal)usedAmount;
        if (TryReadDouble(fields, UsageMonitor.Core.Models.UsageFields.TotalAmount, out var totalAmount)) usage.TotalAmount = (decimal)totalAmount;
        if (TryReadDouble(fields, UsageMonitor.Core.Models.UsageFields.UsedTokens, out var usedTokens)) usage.UsedTokens = (long)usedTokens;
        if (TryReadDouble(fields, UsageMonitor.Core.Models.UsageFields.TotalTokens, out var totalTokens)) usage.TotalTokens = (long)totalTokens;
        if (fields.TryGetValue(UsageMonitor.Core.Models.UsageFields.Unit, out var unit) && unit is string unitStr && !string.IsNullOrEmpty(unitStr))
            usage.Unit = unitStr;

        // 无任何可展示数据时跳过（避免把卡片初始占位替换成“暂无数据”）
        var primaryMetric = Provider?.Card?.PrimaryMetric;
        bool hasDeclarative = !string.IsNullOrEmpty(primaryMetric) && extra.ContainsKey(primaryMetric!);
        bool hasLegacy = usage.UsedAmount != 0 || usage.UsedTokens != 0 || usage.TotalAmount != 0;
#pragma warning restore CS0618
        if (!hasDeclarative && !hasLegacy) return;

        UpdateFromUsage(usage);

        // 快照时间显示含日期（区分实时刷新的 HH:mm:ss）
        LastUpdateText = snapshotTime.ToString("MM-dd HH:mm");
        UsageMonitor.Core.Services.FileLogger.Info("ProviderUsageViewModel",
            $"{_providerId}:{AccountIdSafe} 已从字段快照恢复 {fields.Count} 个字段（快照时间 {snapshotTime:yyyy-MM-dd HH:mm:ss}）");
    }

    /// <summary>问题1：从字段快照字典容错读取数值（double/long/string 装箱兼容）。</summary>
    private static bool TryReadDouble(IReadOnlyDictionary<string, object> fields, string key, out double value)
    {
        value = 0;
        if (!fields.TryGetValue(key, out var raw) || raw == null) return false;
        try
        {
            value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return double.TryParse(raw.ToString(), out value);
        }
    }

    /// <summary>
    /// 根据 UsageInfo 更新显示数据
    /// </summary>
    public void UpdateFromUsage(UsageInfo usage)
    {
        // req-099/bug5：刷新后通知 V2 卡片数据属性重取（Provider 已在 GetUsageAsync 更新 LastUsage，
        // 使 Kimi/Deepseek/Qoder 等 Web 插件的 CardMetricBarData/CardMetricGridData 在新框架下重新渲染）。
        OnPropertyChanged(nameof(CardMetricBarData));
        OnPropertyChanged(nameof(CardMetricGridData));
        IsError = !usage.IsSuccess;
        ErrorMessage = usage.ErrorMessage;
        LastUpdateText = usage.LastUpdated.ToString("HH:mm:ss");
        HasReceivedData = true; // req-079 U-33：标记已收到数据响应（任务栏骨架屏据此结束占位）

        if (!usage.IsSuccess)
        {
            StatusText = "查询失败";

            // Stage B（声明式插件架构）：失败态引导文案由插件 errorGuidance 声明驱动——
            // 按声明顺序匹配错误消息关键字，命中即显示引导；无声明/未命中保持通用文案。
            // 替代原按 ProviderId=="MiniMax" 的硬编码分支（宿主零 Provider 硬编码）。
            var guidance = ResolveErrorGuidance(usage.ErrorMessage);
            if (guidance != null) StatusText = guidance;

            // req-008：失败场景也要把余额快照重置为 4 个默认占位项，避免显示上一次成功的旧值。
            UpdateBalanceFromExtra(usage.Extra ?? new Dictionary<string, object>());
            return;
        }

        UsagePercentage = usage.GetUsagePercentage();

        // Stage D（声明驱动渲染）：插件声明了卡片主指标（Card.PrimaryMetric）且本次 extras 已捕获该字段时，
        // 走声明式富卡片渲染路径（进度条/订阅胶囊/汇总面板/每日图表）。
        // 替代旧 "domExtract" 魔法标志（旧提取器删除后已无写入方，导致富渲染路径失活）——
        // 改按“声明 + 数据存在性”判定，宿主零 Provider 硬编码。
        var declaredPrimaryMetric = Provider?.Card?.PrimaryMetric;
        if (usage.Extra != null && !string.IsNullOrEmpty(declaredPrimaryMetric)
            && usage.Extra.ContainsKey(declaredPrimaryMetric))
        {
            UpdateFromDeclarativeExtras(usage);
            return;
        }

        // 走到这里说明未命中声明式富渲染条件：回到「历史用量百分比」折线图模式。
        _isDeclarativeRenderMode = false;
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
        else if (usage.UsedAmount > 0)
        {
            // req-099/bug5：纯“已用金额”型 API 插件（如 OpenAI 仅返回 UsedAmount、无 TotalAmount/Tokens），
            // 之前落到 else 显示“暂无数据”。此处按“已用 X”展示，恢复其之前的功能。
            UsedText = $"{usage.UsedAmount:F2} {usage.Unit}";
            TotalText = "不限";
            RemainingText = "--";
            StatusText = $"已用 {UsedText}";
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
    /// 渲染声明式抓取产出的富卡片数据（声明驱动，与具体 Provider 无关）。
    /// 数据来源：声明式抓取写入 UsageInfo.Extra 的通用字段键
    /// （5h/周/视频的用量百分比、重置时间、订阅档位、积分、调用汇总）。
    /// </summary>
    private void UpdateFromDeclarativeExtras(UsageInfo usage)
    {
        var extra = usage.Extra!;
        _isDeclarativeRenderMode = true;

        // 小工具：容错读取 double / long / string（Extra 值为 object 装箱）。
        double D(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToDouble(v) : -1;
        long L(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToInt64(v) : 0;
        string S(string k) => extra.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

        // 1. 渲染需求（render_kinds）传给 XAML，用于控制“订阅胶囊/5h/周进度条/汇总面板”是否呈现。
        if (extra.TryGetValue("render_kinds", out var rk) && rk is IEnumerable<string> kinds)
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
            // req-107 B6：render_kinds 缺失/类型不符时回退到声明式 Card.RenderKinds（defaults.json），
            // 避免 RenderKinds 被清空导致 5h/周进度条整段消失。
            RenderKinds = Provider?.Card?.RenderKinds ?? Array.Empty<string>();
        }

        // 2. 订阅档位胶囊（req-088 Phase2：类型 + 档位拆分并排）。
        IsSubscriptionActive = extra.TryGetValue("subscription_active", out var sa) && sa is bool sab && sab;
        var subType = S("subscription_type");
        var subTier = S("subscription_tier");
        if (string.IsNullOrWhiteSpace(subTier)) subTier = S("subscription_title"); // 兼容旧数据（仅档位）
        SubscriptionType = !string.IsNullOrWhiteSpace(subType) ? subType : "Token Plan";
        SubscriptionTier = !string.IsNullOrWhiteSpace(subTier) ? subTier : "订阅";
        SubscriptionTitle = $"{SubscriptionType} · {SubscriptionTier}";

        // 3. 5h 限额进度条（主进度条，绿色主题）。
        var p5 = D("five_hour_used_percent");
        PrimaryBarPercent = p5 >= 0 ? Math.Min(100, p5) : 0;
        UsagePercentage = PrimaryBarPercent; // 遗留逻辑：保留卡片顶部主进度条的取值
        PrimaryResetText = BuildRemainText(extra, "five_hour_reset_at");
        // req-028：同时保存 five_hour_reset_at 原始 DateTime，供托盘/悬浮窗倒计时 + 到时自动刷新。
        Next5hResetAt = extra.TryGetValue("five_hour_reset_at", out var ra) && ra is DateTime radt ? radt : null;

        // 4. 周限额进度条。
        var pw = D("weekly_used_percent");
        WeeklyBarPercent = pw >= 0 ? Math.Min(100, pw) : 0;
        WeeklyResetText = BuildRemainText(extra, "weekly_reset_at");
        // 问题8：保存 weekly_reset_at 原始 DateTime，供任务栏迷你图周限额倒计时。
        NextWeeklyResetAt = extra.TryGetValue("weekly_reset_at", out var wra) && wra is DateTime wradt ? wradt : null;

        // 5. 状态行（保留 StatusText，包含 5h + 周概要）。
        StatusText = (PrimaryBarPercent > 0 || WeeklyBarPercent > 0)
            ? $"5h 已用 {PrimaryBarPercent:0}% · 本周 已用 {WeeklyBarPercent:0}%"
            : "已登录";

        // 6. 视频赠送 5h + 周。
        var v5Used = L("five_hour_video_used");
        var v5Total = L("five_hour_video_total");
        VideoQuotaText = v5Total > 0 ? $"{v5Used}/{v5Total}" : "--";
        VideoIntervalPercent = v5Total > 0 ? Math.Min(100, 100.0 * v5Used / v5Total) : 0;
        var vwUsed = L("weekly_video_used");
        var vwTotal = L("weekly_video_total");
        VideoWeeklyText = vwTotal > 0 ? $"{vwUsed}/{vwTotal}" : "--";
        VideoWeeklyPercent = vwTotal > 0 ? Math.Min(100, 100.0 * vwUsed / vwTotal) : 0;

        // 6b. req-107 B8 渲染消费方：从 Card 显示声明（defaults.json）构建声明式进度条，驱动 V2 MetricBarTemplate。
        RebuildDeclarativeBars();

        // 6c. 数据到位后重建/刷新卡片图表槽位（Bar/Number 数据原位刷新，结构未变不重建控件）。
        RebuildChartSlots();

        // 7. 卡片顶一行的 “已使用 / 总额度 / 剩余额度” 仍保留供未适配卡片主题的插件使用；
        //    本插件额外叠加为信息性文本，不会被三列 UI 误读。
        UsedText = PrimaryBarPercent > 0 ? $"{PrimaryBarPercent:0}%" : "--";
        TotalText = "100%";
        RemainingText = $"{Math.Max(0, 100 - PrimaryBarPercent):0}%";

        // 8. 汇总面板（req-008：多列布局的 BalanceItem 集合）。
        // 默认 4 项：累计 / 峰值 / 活跃 / 积分余额。每项 Value / Detail 由通用字段填充。
        // 插件可通过 provider.BalanceItems 按 Label 覆盖同名项 / 追加额外项 / 隐藏默认项。
        var credits = D("remaining_credits");
        RemainingCredits = credits;
        var totalTokens = S("used_tokens_text");
        var activeDays = L("active_days");
        var totalDays = L("total_days");
        var mostActive = S("most_active_day"); // 格式 "2026-07-01 (552.49M)"
        // Number 图表（数据概览）原始值缓存：供 ResolveFieldDisplay 格式化各数据组。
        _numberCumulativeText = totalTokens;
        _numberPeakToken = D("most_active_token");
        _numberActiveDays = activeDays;
        _numberTotalDays = totalDays;
        _numberMostActiveDay = mostActive; // 问题7：数据概览备注行（Meta 角色字段 most_active_date）取值缓存
        // req-047：提取排名百分比（usage_ranking_percent），用于显示"前X%"
        double? rankingPercent = null;
        if (extra.TryGetValue("usage_ranking_percent", out var rp) && rp is double rpVal && rpVal > 0)
            rankingPercent = rpVal;
        // 订阅到期时间（仅在已订阅时拼接）
        DateTime? subscriptionEnd = null;
        if (IsSubscriptionActive && extra.TryGetValue("subscription_end_at", out var se) && se is DateTime sed)
            subscriptionEnd = sed;

        RebuildBalanceItems(totalTokens, mostActive, activeDays, totalDays, credits, subscriptionEnd, rankingPercent);

        // 9. 折线图 / 热力图：用「每日 Token 用量」填充卡片图表数据。
        UpdateDeclarativeCharts(extra);

        // req-fix-诊断（bug3a/5）：记录卡片渲染门控的运行时值，供下次运行定位“卡片正文空白”。
        UsageMonitor.Core.Services.FileLogger.Info("CardRender",
            $"{ProviderId}: IsDetailExpanded={IsDetailExpanded}, RenderKinds=[{string.Join(",", RenderKinds)}], " +
            $"Show5h={Show5hBar}, ShowWeekly={ShowWeeklyBar}, 5h%={PrimaryBarPercent:0}, weekly%={WeeklyBarPercent:0}, " +
            $"CardChartKinds=[{string.Join(",", CardChartKinds)}], HasCardChart={HasCardChart}, " +
            $"CollapseParts=[{string.Join(",", CollapseVisibleParts)}], Line={CardLineValues.Count}, Heat={HeatMapCells.Count}");
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

    /// <summary>从 <c>most_active_day</c> 字符串中提取数值部分（如"2026-07-01 (552.49M)" → "552.49M"）。</summary>
    private static string ExtractMostActiveToken(string? mostActive)
    {
        if (string.IsNullOrEmpty(mostActive)) return "--";
        var open = mostActive.IndexOf('(');
        var close = mostActive.IndexOf(')');
        if (open < 0 || close <= open) return "--";
        return mostActive.Substring(open + 1, close - open - 1).Trim();
    }

    /// <summary>从 <c>most_active_day</c> 字符串中提取日期部分（如"2026-07-01 (552.49M)" → "2026-07-01"）。</summary>
    private static string? ExtractMostActiveDate(string? mostActive)
    {
        if (string.IsNullOrEmpty(mostActive)) return null;
        var open = mostActive.IndexOf('(');
        if (open <= 0) return null;
        return mostActive.Substring(0, open).Trim();
    }

    /// <summary>
    /// 用声明式抓取的「每日 Token 用量」填充卡片折线图与热力图数据。
    /// <para>
    /// 数据来源：<c>daily_token_values</c>（每日 token，按日期升序）与 <c>daily_token_dates</c>
    /// （对应 yyyy-MM-dd，可能缺省）。折线图取当前周期窗口内的趋势（req-007：默认 7 天，可切换为 30 天），
    /// Y 轴自适应为窗口内最大值；热力图为每个 token&gt;0 的日期生成一个单元（颜色按相对峰值的强度分三档），
    /// 缺日期时按「最后一个点=今天」向前推断日历。完整数据同时缓存到 <c>_fullDailyValues</c> 与
    /// <c>_fullDailyDates</c>，供 <see cref="HandlePeriodChanged"/> 按新周期重新切片。
    /// </para>
    /// </summary>
    private void UpdateDeclarativeCharts(Dictionary<string, object> extra)
    {
        // 读取每日 token 数值序列（Extra 内存直传，值为 List<long>；兼容其它可枚举形态）。
        var values = ReadLongList(extra, "daily_token_values");
        var dates = ReadStringList(extra, "daily_token_dates");
        // req-034 修复：读取每独立的缓存命中率（提前读取，供热力图和折线图共用）
        var dailyCacheHitPercents = ReadDoubleList(extra, "daily_cache_hit_percents");

        // req-007：缓存完整数据，供 PeriodChanged 重新切片。values 在这里就已经是完整升序序列。
        _fullDailyValues = values;
        _fullDailyDates = dates;
        // req-034 修复：缓存完整缓存命中率数据，供 SliceCardLineByPeriod 按周期切片
        _fullDailyCacheHitPercents = dailyCacheHitPercents;

        // req-107 B8：SupportsPeriodSwitch / ExtraTooltipLines 接口成员已收敛为 [Obsolete]，周期切换能力交由 Card.Line.Slicer(Period)、tooltip 扩展行交由 Card.Chart.Tooltip；本方法不再重复从接口读取。

        // req-107 B8 进度：折线图数据由 Effective* 派生属性接管（声明式优先 + _cardLineValues 回退），
        // 周期切片由 SliceCardLineByPeriod（HandlePeriodChanged / 初始刷新）统一处理；本方法不再重复写入折线图。
        // 保留：全量缓存 _fullDailyValues/_fullDailyDates/_fullDailyCacheHitPercents 以供切片，热力图 HeatMapCells 装配。
        // 初始化：先按当前周期切一次，确保首次渲染即有数据（HandlePeriodChanged 会再切一次，无副作用）。
        SliceCardLineByPeriod(CurrentPeriod);

        // req-107 B8：声明式热力图装配（从全量缓存 _fullDailyValues/_fullDailyDates/_fullDailyCacheHitPercents 按日生成 YearHeatMapCell）。
        // 取代原 UpdateDeclarativeCharts 内的内联装配。RecolorHeatMapCells 依赖 cell.Token 仍兼容。
        RebuildDeclarativeHeatMap();
    }

    /// <summary>
    /// req-107 B8：从全量日数据构建热力图单元（声明式装配）。
    /// <para>Cell 含 Day/Percent/Token/Background/ValueText/ComparisonText；YearHeatMapControl 直接绑定；
    /// <see cref="RecolorHeatMapCells"/> 通过 cell.Token 重算背景色兼容。</para>
    /// </summary>
    private void RebuildDeclarativeHeatMap()
    {
        var values = _fullDailyValues;
        var dates = _fullDailyDates;
        var dailyCacheHitPercents = _fullDailyCacheHitPercents;

        // req-009 + req-021：每个 token 日期生成单元，背景色按 token 绝对值走 HeatMapTierScale；token<=0 强制 "无用量"色。
        var zeroBrush = FreezeBrush(0xF3, 0xF4, 0xF6);
        HeatMapCells.Clear();
        if (values.Count == 0) return;

        // 从 cache_hit_percent 全局缓存命中（负数/缺失视为无数据）；属性 setter 会触发 INPC 供折线图 tooltip 使用。
        double cacheHitPercent = -1;
        // 注：此处无法访问 extras（已在外层调用），使用 _cacheHitPercent 字段的现有值（由 UpdateFromDeclarativeExtras 此前赋值）。
        if (_cacheHitPercent > 0) cacheHitPercent = _cacheHitPercent;

        // 计算峰值（用于 Percent 归一）
        long peak = 0;
        foreach (var v in values) if (v > peak) peak = v;
        if (peak <= 0) peak = 1;

        for (int i = 0; i < values.Count; i++)
        {
            var token = values[i];
            string day = i < dates.Count && !string.IsNullOrEmpty(dates[i])
                ? dates[i]
                : DateTime.Today.AddDays(-(values.Count - 1 - i)).ToString("yyyy-MM-dd");
            double percent = Math.Min(100.0, 100.0 * token / peak);
            var bgBrush = token > 0 ? UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush(token, ProviderId) : zeroBrush;
            double dayCacheHit = i < dailyCacheHitPercents.Count ? dailyCacheHitPercents[i] : -1;

            HeatMapCells.Add(new YearHeatMapCell
            {
                Day = day,
                Percent = percent,
                Token = token,
                Background = bgBrush,
                // 问题10：主值行 = 声明的主 Value 字段（缓存命中百分比）；对比行 = 第二 Value 字段（每日 Token 用量），
                // 仅在对应字段被勾选时展示（YearHeatMapControl.TooltipComparisonField 控制）。
                ValueText = dayCacheHit >= 0 ? $"{dayCacheHit:0.00}%" : "--",
                Unit = "",
                ComparisonText = token > 0 ? $"用量 {FormatTokens(token)}" : string.Empty
            });
        }
        OnPropertyChanged(nameof(EffectiveHeatMapCells));
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
    // 所有调用点（UpdateDeclarativeCharts / RecolorHeatMapCells）已改为走 HeatMapTierScale.ResolveBrush。
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
    /// <param name="key">超时字段名（"five_hour_reset_at" / "weekly_reset_at"）</param>
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
