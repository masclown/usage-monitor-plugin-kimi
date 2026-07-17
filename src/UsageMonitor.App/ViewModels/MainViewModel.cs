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
    private string _balanceText = "--";
    private string _balanceValueText = "--";
    private string _balanceUnitText = "";
    private string _balanceDetailText = "";
    private bool _hasBalanceInfo;
    private Action? _openConfigAction;
    private Func<Task>? _refreshCardAction;
    // 卡片多进度条与订阅档位相关字段
    private string _subscriptionTitle = "Token Plan 订阅";
    private bool _isSubscriptionActive;
    private double _primaryBarPercent;
    private double _weeklyBarPercent;
    private string _primaryResetText = "--";
    private string _weeklyResetText = "--";
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
    /// <summary>账户余额摘要（如“余额 12,345 积分”）。如未抓到则保持“--”</summary>
    public string BalanceText { get => _balanceText; set { _balanceText = value; OnPropertyChanged(); } }
    /// <summary>账户余额数值部分（如“12,345”），用于卡片中放大突出显示；无数值时为占位文案（“暂无积分”/“--”）</summary>
    public string BalanceValueText { get => _balanceValueText; set { _balanceValueText = value; OnPropertyChanged(); } }
    /// <summary>账户余额单位部分（如“积分”），随数值一起显示；无数值时为空字符串以隐藏单位</summary>
    public string BalanceUnitText { get => _balanceUnitText; set { _balanceUnitText = value; OnPropertyChanged(); } }
    /// <summary>账户余额详情（套餐窗口、Cookie 时间等）</summary>
    public string BalanceDetailText { get => _balanceDetailText; set { _balanceDetailText = value; OnPropertyChanged(); } }
    /// <summary>是否已抓取到余额/账单信息（控制 UI 折叠面板显示）</summary>
    public bool HasBalanceInfo { get => _hasBalanceInfo; set { _hasBalanceInfo = value; OnPropertyChanged(); } }

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
            }
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

        // 8. 汇总面板（积分/累计/排名/活跃天/重置时间）。
        HasBalanceInfo = true;
        var credits = D("mm_remainingCredits");
        RemainingCredits = credits;
        // 不再回退到 SubscriptionTitle，避免与顶部订阅胶囊重复显示。
        BalanceText = credits > 0 ? $"{credits:N0} 积分" : "暂无积分";
        // 拆分数值与单位：卡片中数值放大突出、单位保持小字；无积分时仅显示占位文案并隐藏单位
        BalanceValueText = credits > 0 ? $"{credits:N0}" : "暂无积分";
        BalanceUnitText = credits > 0 ? "积分" : "";

        var sb = new System.Text.StringBuilder();
        var totalTokens = S("mm_totalTokens");
        if (!string.IsNullOrEmpty(totalTokens)) sb.Append($"累计 {totalTokens}");
        var ranking = D("mm_rankingPercent");
        if (ranking > 0) sb.Append($" · 排名前 {ranking:0.##}%");
        var activeDays = L("mm_activeDays");
        var totalDays = L("mm_totalDays");
        if (totalDays > 0) sb.Append($" · 活跃 {activeDays}/{totalDays} 天");
        var mostActive = S("mm_mostActiveDay");
        if (!string.IsNullOrEmpty(mostActive)) sb.Append($" · 峰值 {mostActive}");

        // 订阅到期时间（仅在已订阅时拼接）
        if (IsSubscriptionActive && extra.TryGetValue("mm_subscriptionEndTime", out var se) && se is DateTime sed)
            sb.Append($"\n订阅续期至 {sed:yyyy-MM-dd}");
        BalanceDetailText = sb.ToString().TrimStart(' ', '·');

        // 9. 折线图 / 热力图：用「每日 Token 用量」填充卡片图表数据。
        UpdateMiniMaxCharts(extra);
    }

    /// <summary>
    /// 用 MiniMax DOM 抓取的「每日 Token 用量」填充卡片折线图与热力图数据。
    /// <para>
    /// 数据来源：<c>mm_dailyTokenValues</c>（每日 token，按日期升序）与 <c>mm_dailyTokenDates</c>
    /// （对应 yyyy-MM-dd，可能缺省）。折线图取最近 30 天的趋势（Y 轴自适应为区间最大值）；
    /// 热力图为每个 token&gt;0 的日期生成一个单元（颜色按相对峰值的强度分三档），
    /// 缺日期时按「最后一个点=今天」向前推断日历。
    /// </para>
    /// </summary>
    private void UpdateMiniMaxCharts(Dictionary<string, object> extra)
    {
        // 读取每日 token 数值序列（Extra 内存直传，值为 List<long>；兼容其它可枚举形态）。
        var values = ReadLongList(extra, "mm_dailyTokenValues");
        var dates = ReadStringList(extra, "mm_dailyTokenDates");

        // 折线图：取最近 30 天，Y 轴自适应为该区间最大值（至少 1，避免除零/全零全平）。
        if (values.Count >= 2)
        {
            const int lineWindow = 30;
            var start = Math.Max(0, values.Count - lineWindow);
            var recent = new List<double>();
            for (int i = start; i < values.Count; i++) recent.Add(values[i]);
            CardLineValues = recent;
            double max = 0;
            foreach (var r in recent) if (r > max) max = r;
            CardLineMax = max > 0 ? max : 1;
        }
        else
        {
            CardLineValues = Array.Empty<double>();
            CardLineMax = 1;
        }

        // 热力图：为每个 token>0 的日期生成单元（颜色按相对峰值强度分三档）。
        HeatMapCells.Clear();
        if (values.Count > 0)
        {
            long peak = 0;
            foreach (var v in values) if (v > peak) peak = v;
            if (peak <= 0) peak = 1;

            for (int i = 0; i < values.Count; i++)
            {
                var token = values[i];
                if (token <= 0) continue; // 0 用量日显示为网底，不生成单元
                // 日期：优先用真实 date；缺失时按「最后一个点=今天」向前推。
                string day = i < dates.Count && !string.IsNullOrEmpty(dates[i])
                    ? dates[i]
                    : DateTime.Today.AddDays(-(values.Count - 1 - i)).ToString("yyyy-MM-dd");
                double percent = Math.Min(100.0, 100.0 * token / peak);
                HeatMapCells.Add(new YearHeatMapCell
                {
                    Day = day,
                    Percent = percent,
                    Background = SelectHeatMapBrush(percent)
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

    /// <summary>把 0-100 的强度百分比映射为热力图三档画笔（低绿/中橙/高红），已 Freeze 可跨线程绑定。</summary>
    private static System.Windows.Media.Brush SelectHeatMapBrush(double percent)
    {
        if (percent >= 66.0) return HeatHigh;
        if (percent >= 33.0) return HeatMid;
        return HeatLow;
    }

    // 热力图三档画笔（与 Tokens.xaml 的 UsageLow/Mid/High 同色系），Freeze 后可跨线程安全使用。
    private static readonly System.Windows.Media.Brush HeatLow = FreezeBrush(0x22, 0xC5, 0x5E);
    private static readonly System.Windows.Media.Brush HeatMid = FreezeBrush(0xF5, 0x9E, 0x0B);
    private static readonly System.Windows.Media.Brush HeatHigh = FreezeBrush(0xEF, 0x44, 0x44);

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
    /// 从 <paramref name="extra"/> 提取账户余额与账单信息填充到 VM 字段。
    /// 参考 MiniMaxBalanceFetcher 的字段语义（仅在 Extra 中存在对应字段时填充）。
    /// </summary>
    private void UpdateBalanceFromExtra(Dictionary<string, object> extra)
    {
        HasBalanceInfo = false;
        BalanceText = "--";
        BalanceValueText = "--";
        BalanceUnitText = "";
        BalanceDetailText = "";

        if (extra == null || extra.Count == 0) return;

        // 状态指示：balanceFetcherStatus
        // - no_cookie: 未抓到 Cookie
        // - error: 抓取出错
        // - success: 全部成功
        // - 其他：部分成功
        var status = extra.TryGetValue("balanceFetcherStatus", out var sObj) ? sObj?.ToString() : null;
        if (string.IsNullOrEmpty(status)) return;  // 非余额抓取场景，直接跳过

        HasBalanceInfo = true;

        // 余额摘要：优先用 coding_plan 接口的 5h 窗口剩余量
        if (extra.TryGetValue("accountIntervalRemaining", out var irObj) &&
            extra.TryGetValue("accountIntervalTotal", out var itObj))
        {
            var remain = Convert.ToInt64(irObj);
            var total = Convert.ToInt64(itObj);
            BalanceText = $"{remain:N0} / {total:N0}（5h窗口剩余）";
        }
        else if (extra.TryGetValue("accountWeeklyRemaining", out var wrObj) &&
                 extra.TryGetValue("accountWeeklyTotal", out var wtObj))
        {
            var remain = Convert.ToInt64(wrObj);
            var total = Convert.ToInt64(wtObj);
            BalanceText = $"{remain:N0} / {total:N0}（周窗口剩余）";
        }
        else if (status == "no_cookie")
        {
            BalanceText = "未登录";
        }
        else
        {
            BalanceText = "暂不可用";
        }

        // 余额详情拼接
        var sb = new System.Text.StringBuilder();
        if (extra.TryGetValue("accountIntervalEndAt", out var endObj) && endObj is DateTime endAt)
        {
            sb.Append($"5h窗口重置于 {endAt:HH:mm}");
        }
        if (extra.TryGetValue("balancePageSnapshotPath", out var pathObj))
        {
            sb.Append($"  ·  HTML快照: {Path.GetFileName(pathObj?.ToString() ?? "")}");
        }
        if (status == "no_cookie" && extra.TryGetValue("balanceFetcherMessage", out var msgObj))
        {
            sb.Append($"\n{msgObj}");
        }
        BalanceDetailText = sb.ToString().TrimStart(' ', '·', '\n', ' ');
    }

    private static string FormatTokens(long count)
    {
        if (count < 0) return "不限";
        if (count >= 1_000_000) return $"{count / 1_000_000.0:F1}M";
        if (count >= 1_000) return $"{count / 1_000.0:F1}K";
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
    /// 重着色本卡片的"每日 Token"热力图单元（按当前全局色阶）。
    /// </summary>
    public void RecolorHeatMapCells()
    {
        foreach (var cell in HeatMapCells)
        {
            cell.Background = UsageMonitor.App.Helpers.UsageTierScale.ResolveBrush(cell.Percent);
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
        var configWindow = new Views.PluginConfigWindow(
            DisplayName, ConfigFields, config, _provider.LoginConfig,
            _provider.SupportedCardCharts, currentCharts);
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

    /// <summary>供主窗口和设置窗口复用的全局配置服务。</summary>
    public ConfigService ConfigService => _configService;

    /// <summary>各服务商的用量显示列表（全量，包含被禁用的项，用于切换时保留状态）</summary>
    public ObservableCollection<ProviderUsageViewModel> Usages { get; } = new();

    /// <summary>仅展示已启用插件的用量卡片（主窗口 ItemsControl 实际绑定此集合）</summary>
    public ObservableCollection<ProviderUsageViewModel> EnabledUsages { get; } = new();

    /// <summary>插件列表</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems { get; } = new();

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
    public IRelayCommand RefreshCommand { get; }

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

    /// <summary>REQ-004：触发区域宽度（≥80）。</summary>
    public int TriggerRectWidth
    {
        get => _configService.Settings.TrayTooltipTriggerRect.Width;
        set
        {
            var r = _configService.Settings.TrayTooltipTriggerRect.With(width: Math.Max(80, value));
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

    /// <summary>REQ-004：触发区域高度（≥60）。</summary>
    public int TriggerRectHeight
    {
        get => _configService.Settings.TrayTooltipTriggerRect.Height;
        set
        {
            var r = _configService.Settings.TrayTooltipTriggerRect.With(height: Math.Max(60, value));
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

    /// <summary>REQ-004：使用 WPF SystemParameters.WorkArea 夹回 RectInt（避免越屏 / 超出工作区）。</summary>
    private RectInt ClampRect(RectInt r)
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            return r.ClampToScreen((int)wa.Left, (int)wa.Top, (int)wa.Right, (int)wa.Bottom);
        }
        catch
        {
            // 静态字段访问在异常路径下回退到 1080p 兑底
            return r.ClampToScreen();
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

        RefreshCommand = new RelayCommand(async () => await refreshService.RefreshAllAsync());
        SaveSettingsCommand = new RelayCommand(() => _configService.Save());

        // REQ-003：环形图 metric 顺序从设置同步到 ListBox 集合；提供上下移动 + 恢复默认三个命令
        // 使用 RelayCommand<int> 泛型版本（列表索引），CommunityToolkit.Mvvm 8.x 的非泛型 RelayCommand 仅接 Action。
        SyncRingChartMetricOrderFromConfig();
        MoveRingMetricUpCommand = new RelayCommand<int>(idx =>
        {
            if (idx <= 0 || idx >= RingChartMetricOrder.Count) return;
            (RingChartMetricOrder[idx - 1], RingChartMetricOrder[idx]) =
                (RingChartMetricOrder[idx], RingChartMetricOrder[idx - 1]);
            PersistRingChartMetricOrder();
        });
        MoveRingMetricDownCommand = new RelayCommand<int>(idx =>
        {
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

        // REQ-004：触发区域默认重置命令 + 进入蒙版
        ResetTriggerAreaCommand = new RelayCommand(() =>
        {
            var def = RectInt.DefaultBottomRight();
            // 使用当前主屏工作区夹回，避免默认 1920×1080 兑底越界
            try
            {
                var wa = SystemParameters.WorkArea;
                def = def.ClampToScreen((int)wa.Left, (int)wa.Top, (int)wa.Right, (int)wa.Bottom);
            }
            catch { }
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

        // 初始化插件列表与用量显示
        foreach (var plugin in pluginManager.Plugins)
        {
            // 读取已保存的显示模式
            var savedMode = _configService.Settings.ProviderTaskbarModes
                .GetValueOrDefault(plugin.Provider.ProviderId, TaskbarDisplayMode.Text);
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
                RenderKinds = plugin.Provider.DefaultRenderKinds
            };
            usageVm.AttachConfigService(_configService);
            Usages.Add(usageVm);
        }

        // 启动时构建一次"已启用"过滤集合
        RebuildEnabledUsages();

        // 监听历史数据变化
        _historyStore.ProviderHistoryChanged += OnProviderHistoryChanged;
        _historyStore.HistoryChanged += OnAnyHistoryChanged;

        // 订阅全局用量色阶变更：档位 / 颜色改了之后，强制让所有进度条 XAML 绑定刷新
        // （PercentToBrushConverter 重新走 ResolveBrush → 返回新 Brush），同时重着色卡片热力图单元。
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnUsageTierChanged;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
