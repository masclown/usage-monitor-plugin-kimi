using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
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
    private IReadOnlyList<double> _historyValues = Array.Empty<double>();
    private string _balanceText = "--";
    private string _balanceDetailText = "";
    private bool _hasBalanceInfo;
    private Action? _openConfigAction;

    /// <summary>
    /// 创建用量显示 VM。
    /// </summary>
    /// <param name="openConfigAction">
    /// 点击卡片上"⚙ 设置"按钮时触发的回调。
    /// 不传时点击按钮不会有反应（插件没配置项的情况）。
    /// </param>
    public ProviderUsageViewModel(Action? openConfigAction = null)
    {
        _openConfigAction = openConfigAction;
        ConfigCommand = new RelayCommand(OpenConfig, () => _openConfigAction != null);
    }

    /// <summary>
    /// 主窗口中卡片右上角"⚙ 设置"按钮绑定的命令（调用构造时传入的回调）。
    /// </summary>
    public RelayCommand ConfigCommand { get; }

    /// <summary>
    /// 主窗口卡片点击设置按钮时调用外部逻辑（打开 PluginConfigWindow）。
    /// </summary>
    private void OpenConfig()
    {
        _openConfigAction?.Invoke();
    }

    public string ProviderId { get => _providerId; set { _providerId = value; OnPropertyChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
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
    /// <summary>账户余额详情（套餐窗口、Cookie 时间等）</summary>
    public string BalanceDetailText { get => _balanceDetailText; set { _balanceDetailText = value; OnPropertyChanged(); } }
    /// <summary>是否已抓取到余额/账单信息（控制 UI 折叠面板显示）</summary>
    public bool HasBalanceInfo { get => _hasBalanceInfo; set { _hasBalanceInfo = value; OnPropertyChanged(); } }

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
    /// 历史已用百分比数据点（用于折线图绘制，0-100 数值）
    /// </summary>
    public IReadOnlyList<double> HistoryValues
    {
        get => _historyValues;
        set
        {
            _historyValues = value ?? Array.Empty<double>();
            OnPropertyChanged();
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
    /// 数据来源：MiniMaxDomExtractor 写入 UsageInfo.Extra 的 mm_* 键（
    /// remains_percent 的 5h/周/视频 + usage_summary 的累计/排名/活跃天 + credit 积分）。
    /// </summary>
    private void UpdateFromMiniMaxDom(UsageInfo usage)
    {
        var extra = usage.Extra!;

        // 小工具：容错读取 double / long / string（Extra 值为 object 装箱）。
        double D(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToDouble(v) : -1;
        long L(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToInt64(v) : 0;
        string S(string k) => extra.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

        var p5 = D("mm_5hUsedPercent");
        var pw = D("mm_weeklyUsedPercent");

        // 进度条用 5h 已用百分比（GetUsagePercentage 已由 UsedAmount/TotalAmount 算出，这里保险重设）。
        UsagePercentage = p5 >= 0 ? p5 : (pw >= 0 ? pw : 0);
        StatusText = (p5 >= 0 || pw >= 0)
            ? $"5h 已用 {(p5 >= 0 ? p5 : 0):0}% · 周 {(pw >= 0 ? pw : 0):0}%"
            : "已登录";

        // 三列（值自带说明，避免与固定标签不符）：5h / 周 / 视频赠送。
        UsedText = p5 >= 0 ? $"5h {p5:0}%" : "--";
        TotalText = pw >= 0 ? $"周 {pw:0}%" : "--";
        var vUsed = L("mm_videoIntervalUsed");
        var vTotal = L("mm_videoIntervalTotal");
        RemainingText = vTotal > 0 ? $"视频 {vTotal - vUsed}/{vTotal}" : "--";

        // 下方汇总面板（复用余额快照 UI）：积分/累计/排名/活跃天/重置时间。
        HasBalanceInfo = true;
        var credits = D("mm_remainingCredits");
        BalanceText = credits > 0 ? $"{credits:N0} 积分" : "Token Plan 订阅";

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
        if (extra.TryGetValue("mm_5hResetAt", out var r5) && r5 is DateTime r5d)
            sb.Append($"\n5h 重置 {r5d:MM-dd HH:mm}");
        if (extra.TryGetValue("mm_weeklyResetAt", out var rw) && rw is DateTime rwd)
            sb.Append($" · 周重置 {rwd:MM-dd HH:mm}");
        BalanceDetailText = sb.ToString().TrimStart(' ', '·');
    }

    /// <summary>
    /// 从 <paramref name="extra"/> 提取账户余额与账单信息填充到 VM 字段。
    /// 参考 MiniMaxBalanceFetcher 的字段语义（仅在 Extra 中存在对应字段时填充）。
    /// </summary>
    private void UpdateBalanceFromExtra(Dictionary<string, object> extra)
    {
        HasBalanceInfo = false;
        BalanceText = "--";
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
        // 通用登录配置：只要插件声明了 LoginConfig（不限于 MiniMax），配置窗口就显示"获取登录态"按钮
        var configWindow = new Views.PluginConfigWindow(
            DisplayName, ConfigFields, config, _provider.LoginConfig);
        configWindow.Owner = System.Windows.Application.Current.Windows
            .OfType<Window>().FirstOrDefault(w => w.IsActive);

        if (configWindow.ShowDialog() == true)
        {
            _configService.UpdateProviderConfig(ProviderId, config);
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

    /// <summary>各服务商的用量显示列表（全量，包含被禁用的项，用于切换时保留状态）</summary>
    public ObservableCollection<ProviderUsageViewModel> Usages { get; } = new();

    /// <summary>仅展示已启用插件的用量卡片（主窗口 ItemsControl 实际绑定此集合）</summary>
    public ObservableCollection<ProviderUsageViewModel> EnabledUsages { get; } = new();

    /// <summary>插件列表</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems { get; } = new();

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

    /// <summary>刷新命令</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>保存设置命令</summary>
    public IRelayCommand SaveSettingsCommand { get; }

    public MainViewModel(PluginManager pluginManager, ConfigService configService, RefreshService refreshService, UsageHistoryStore? historyStore = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _refreshService = refreshService;
        _historyStore = historyStore ?? new UsageHistoryStore();
        _historyStore.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);

        RefreshCommand = new RelayCommand(async () => await refreshService.RefreshAllAsync());
        SaveSettingsCommand = new RelayCommand(() => _configService.Save());

        // 初始化插件列表与用量显示
        foreach (var plugin in pluginManager.Plugins)
        {
            // 读取已保存的显示模式
            var savedMode = _configService.Settings.ProviderTaskbarModes
                .GetValueOrDefault(plugin.Provider.ProviderId, TaskbarDisplayMode.Text);

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
            };
            PluginItems.Add(item);

            // 初始化用量显示（传入打开配置的回调，让卡片上"⚙ 设置"按钮能复用现有逻辑）
            var usageVm = new ProviderUsageViewModel(item.OpenConfigDialog)
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                IsEnabled = plugin.IsEnabled,
                DisplayMode = savedMode
            };
            Usages.Add(usageVm);
        }

        // 启动时构建一次"已启用"过滤集合
        RebuildEnabledUsages();

        // 监听历史数据变化
        _historyStore.ProviderHistoryChanged += OnProviderHistoryChanged;
        _historyStore.HistoryChanged += OnAnyHistoryChanged;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
