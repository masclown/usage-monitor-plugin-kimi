using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using UsageMonitor.App.Controls;
using UsageMonitor.Core.Services;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
//   明确选择 WPF 版本，避免与 System.Drawing / System.Windows.Forms 同名类型冲突。
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FontFamily = System.Windows.Media.FontFamily;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using FontStretches = System.Windows.FontStretches;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 历史窗口的 Provider 多选项（checkbox 绑定）
/// </summary>
public class ProviderOption : INotifyPropertyChanged
{
    private bool _isSelected = true;

    /// <summary>ProviderId（用于 Repository 查询）</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>UI 显示名称。已卸载时拼接"（已卸载）"</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>当前是否仍安装 / 启用（在主插件列表中存在）</summary>
    public bool IsInstalled { get; set; } = true;

    /// <summary>是否被勾选（默认全选）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 时间范围选项
/// </summary>
public enum HistoryRange
{
    Last7Days,
    Last30Days,
    Last90Days,
    All
}

/// <summary>
/// 图表类型
/// </summary>
public enum HistoryChartKind
{
    Line,
    Bar,
    HeatMap,
    DayNightArc
}

/// <summary>
/// req-013：详情表格行（按 Provider × 刷新事件）。
/// <para>
/// 数据源从原 <c>usage_daily</c>（日聚合）切换为 <c>usage_refresh_aggregates</c>（每次刷新聚合）。
/// 热力图仍走 <c>usage_daily</c>，保留“按天聚合”的语义不变。
/// </para>
/// </summary>
public class DailyDetailRow
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    /// <summary>req-013：本次刷新的时间（HH:mm:ss）</summary>
    public string RefreshedAtText { get; set; } = string.Empty;
    public string MaxPercent { get; set; } = "--";
    public string MinPercent { get; set; } = "--";
    public string EndPercent { get; set; } = "--";
    public string AvgPercent { get; set; } = "--";
    public string Snapshots { get; set; } = "0";
}

/// <summary>
/// 历史窗口主 ViewModel。
/// <para>
/// 通过 <see cref="UsageHistoryRepository"/> 查明细 + 日聚合，支持多 Provider 对比 + 多时间范围 + 双图表。
/// </para>
/// </summary>
public class HistoryViewModel : INotifyPropertyChanged
{
    private readonly UsageHistoryRepository _repository;

    /// <summary>req-012：配置服务（用于按 PluginEnabled 过滤 Provider + 订阅 ConfigChanged）。</summary>
    private readonly UsageMonitor.Core.Services.ConfigService _configService;

    /// <summary>系统已安装插件的 Provider 静态映射（id -> displayName），用于回显已卸载历史</summary>
    private readonly Dictionary<string, string> _installedProviderNames;

    /// <summary>
    /// 创建 HistoryViewModel。
    /// </summary>
    /// <param name="repository">已建好的持久化仓库</param>
    /// <param name="configService">req-012：配置服务（用于按 PluginEnabled 过滤 Provider）</param>
    /// <param name="installedPlugins">
    /// 当前已安装的插件列表（用来获取 displayName）。已卸载 Provider 用 ProviderId 作为显示名 + "（已卸载）" 后缀。
    /// </param>
    public HistoryViewModel(UsageMonitor.Core.Services.ConfigService configService,
                            UsageHistoryRepository repository,
                            IEnumerable<(string providerId, string displayName)> installedPlugins)
    {
        _configService = configService;
        _repository = repository;
        _installedProviderNames = installedPlugins
            .GroupBy(x => x.providerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().displayName, StringComparer.OrdinalIgnoreCase);

        _rangeOptions = new[]
        {
            new KeyValuePair<HistoryRange, string>(HistoryRange.Last7Days, "最近 7 天"),
            new KeyValuePair<HistoryRange, string>(HistoryRange.Last30Days, "最近 30 天"),
            new KeyValuePair<HistoryRange, string>(HistoryRange.Last90Days, "最近 90 天"),
            new KeyValuePair<HistoryRange, string>(HistoryRange.All, "全部")
        };
        _selectedRange = HistoryRange.Last7Days;

        _chartKindOptions = new[]
        {
            new KeyValuePair<HistoryChartKind, string>(HistoryChartKind.Line, "折线图"),
            new KeyValuePair<HistoryChartKind, string>(HistoryChartKind.Bar, "柱状图"),
            new KeyValuePair<HistoryChartKind, string>(HistoryChartKind.HeatMap, "热力图"),
            new KeyValuePair<HistoryChartKind, string>(HistoryChartKind.DayNightArc, "编程时段")
        };
        _selectedChartKind = HistoryChartKind.Line;

        // 默认占位文案
        _statusMessage = "正在加载 Provider 列表…";

        // 订阅全局用量色阶变更：重着色热力图单元（颜色按新色阶刷）。
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnTierChanged;

        // req-012：订阅 ConfigChanged → 设置页勾选/取消勾选插件时，历史窗口的 Provider 列表实时跟随。
        // 注意：ConfigChanged 在 Save 路径上会高频触发，UI 线程 Invoke 时做去重（移除已不存在的 Provider）。
        _configService.ConfigChanged += OnConfigChanged;
    }

    /// <summary>色阶表变化后重着色热力图单元。</summary>
    private void OnTierChanged(object? sender, EventArgs e)
    {
        foreach (var cell in HeatMapCells)
        {
            cell.Background = UsageMonitor.App.Helpers.UsageTierScale.ResolveBrush(cell.Percent);
        }
    }

    private readonly KeyValuePair<HistoryRange, string>[] _rangeOptions;
    private readonly KeyValuePair<HistoryChartKind, string>[] _chartKindOptions;

    private HistoryRange _selectedRange;
    private HistoryChartKind _selectedChartKind;
    private string _statusMessage = "";
    private bool _isLoading;
    private bool _noDataWarning;
    private HistorySeries _ringSeries = new();
    private IReadOnlyList<double> _barValues = Array.Empty<double>();
    private string _activeDaysText = "--";
    private string _peakUsageText = "--";
    private string _avgUsageText = "--";

    /// <summary>可选时间范围（绑定到 ComboBox）</summary>
    public IReadOnlyList<KeyValuePair<HistoryRange, string>> RangeOptions => _rangeOptions;

    /// <summary>可选图表类型</summary>
    public IReadOnlyList<KeyValuePair<HistoryChartKind, string>> ChartKindOptions => _chartKindOptions;

    /// <summary>当前选中的时间范围</summary>
    public HistoryRange SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (_selectedRange == value) return;
            _selectedRange = value;
            OnPropertyChanged();
            _ = LoadDataAsync();
        }
    }

    /// <summary>当前选中的图表类型</summary>
    public HistoryChartKind SelectedChartKind
    {
        get => _selectedChartKind;
        set
        {
            if (_selectedChartKind == value) return;
            _selectedChartKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLineChart));
            OnPropertyChanged(nameof(IsHeatMap));
            OnPropertyChanged(nameof(IsBarChart));
            OnPropertyChanged(nameof(IsDayNightArc));
        }
    }

    public bool IsLineChart => _selectedChartKind == HistoryChartKind.Line;
    public bool IsHeatMap => _selectedChartKind == HistoryChartKind.HeatMap;
    public bool IsBarChart => _selectedChartKind == HistoryChartKind.Bar;
    public bool IsDayNightArc => _selectedChartKind == HistoryChartKind.DayNightArc;

    /// <summary>柱状图数据（取首个选中 Provider 的百分比序列）。</summary>
    public IReadOnlyList<double> BarValues
    {
        get => _barValues;
        private set { _barValues = value ?? Array.Empty<double>(); OnPropertyChanged(); }
    }

    /// <summary>活跃天数（有数据的天数），仿参考图统计卡。</summary>
    public string ActiveDaysText { get => _activeDaysText; private set { _activeDaysText = value; OnPropertyChanged(); } }
    /// <summary>区间内单日最高用量%。</summary>
    public string PeakUsageText { get => _peakUsageText; private set { _peakUsageText = value; OnPropertyChanged(); } }
    /// <summary>区间内平均用量%。</summary>
    public string AvgUsageText { get => _avgUsageText; private set { _avgUsageText = value; OnPropertyChanged(); } }

    /// <summary>状态文本（"加载中..."/"共 X 条"/"暂无数据"...）</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    /// <summary>是否加载中（控制 spinner / 禁用按钮）</summary>
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

    /// <summary>当前 Provider 是否无数据（用于显示提示）</summary>
    public bool NoDataWarning
    {
        get => _noDataWarning;
        set
        {
            if (_noDataWarning == value) return;
            _noDataWarning = value;
            OnPropertyChanged();
        }
    }

    /// <summary>所有可选 Provider（含已卸载）</summary>
    public ObservableCollection<ProviderOption> Providers { get; } = new();

    /// <summary>折线图多线数据</summary>
    public ObservableCollection<HistorySeries> LineSeries { get; } = new();

    /// <summary>req-040：折线图横坐标日期标签（与 LineSeries 中第一个序列的 Values 等长）</summary>
    public IReadOnlyList<string> LineDates
    {
        get => _lineDates;
        private set { _lineDates = value; OnPropertyChanged(); }
    }
    private IReadOnlyList<string> _lineDates = Array.Empty<string>();

    /// <summary>热力图单元集合</summary>
    public ObservableCollection<YearHeatMapCell> HeatMapCells { get; } = new();

    /// <summary>详情表格</summary>
    public ObservableCollection<DailyDetailRow> DetailRows { get; } = new();

    /// <summary>单 Provider 选中时的圆环摘要数据源</summary>
    public HistorySeries RingSeries
    {
        get => _ringSeries;
        set
        {
            _ringSeries = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRingSeries));
            OnPropertyChanged(nameof(RingLatestPercent));
            OnPropertyChanged(nameof(RingSampleCount));
        }
    }

    public bool HasRingSeries => _ringSeries.Values != null && _ringSeries.Values.Count > 0;

    /// <summary>圆环图最新百分比（0-100）</summary>
    public double RingLatestPercent
    {
        get
        {
            if (_ringSeries.Values == null || _ringSeries.Values.Count == 0) return 0;
            var v = _ringSeries.Values[^1];
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }
    }

    /// <summary>圆环图采样点数量（用于在右边摘要区以"共 X 个采样点"形式展示）</summary>
    public int RingSampleCount => _ringSeries.Values?.Count ?? 0;

    /// <summary>
    /// 是否单 Provider 选中（用于决定是否显示圆环摘要）
    /// </summary>
    public bool IsSingleProviderSelected
    {
        get
        {
            var selected = Providers.Where(p => p.IsSelected).ToList();
            return selected.Count == 1;
        }
    }

    /// <summary>
    /// 初始化 Provider 列表（合并 PluginManager.Plugins 和 DB 中出现过的 ProviderId）。
    /// 启动时一次性调用。
    /// </summary>
    /// <remarks>
    /// req-012：现在按 <see cref="UsageMonitor.Core.Models.AppSettings.PluginEnabled"/> 过滤——
    /// 只显示**已启用**的 Provider。已卸载但保留历史的 Provider **暂不显示**（决策 7A）；
    /// 重新安装并启用插件后，IsInstalled 路径会自动加入。
    /// </remarks>
    public async Task InitializeProvidersAsync()
    {
        Providers.Clear();
        // 不再调用 _repository.GetKnownProviderIdsAsync()：req-012 决策 7A 不再展示已卸载 Provider。
        // 重新安装并启用插件后，IsInstalled 路径会自然带进来。

        // 第一优先：已安装且**已启用**的 Provider
        foreach (var (pid, name) in _installedProviderNames.OrderBy(p => p.Value, StringComparer.CurrentCulture))
        {
            var enabled = _configService.Settings.PluginEnabled.GetValueOrDefault(pid, true);
            if (!enabled) continue; // 禁用项不显示（决策 1）
            Providers.Add(new ProviderOption
            {
                ProviderId = pid,
                DisplayName = name,
                IsInstalled = true,
                IsSelected = true
            });
        }

        foreach (var p in Providers)
        {
            p.PropertyChanged += OnProviderOptionChanged;
        }
        await LoadDataAsync();
    }

    /// <summary>
    /// req-012：ConfigChanged 回调 —— 设置页勾选/取消勾选插件时，历史窗口的 Provider 列表实时跟随。
    /// <para>
    /// 触发场景：设置页"插件" Tab 取消勾选 / 重新勾选某个 Provider。
    /// 行为：
    /// <list type="bullet">
    ///   <item><description>当前列表中已不启用的 Provider 移除</description></item>
    ///   <item><description>新启用的 Provider（如历史窗口打开后用户启用）追加（用 InstalledProviderNames 找 displayName）</description></item>
    ///   <item><description>重新加载数据（异步）</description></item>
    /// </list>
    /// </para>
    /// </summary>
    private void OnConfigChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.InvokeAsync(() =>
        {
            var enabledMap = _configService.Settings.PluginEnabled;

            // 1. 移除已不启用的 Provider
            var toRemove = Providers
                .Where(p => !enabledMap.GetValueOrDefault(p.ProviderId, true))
                .ToList();
            foreach (var p in toRemove)
            {
                p.PropertyChanged -= OnProviderOptionChanged;
                Providers.Remove(p);
            }

            // 2. 追加新启用的 Provider（_installedProviderNames 里有但当前 Providers 列表没有）
            var existingIds = new HashSet<string>(
                Providers.Select(p => p.ProviderId), System.StringComparer.OrdinalIgnoreCase);
            foreach (var (pid, name) in _installedProviderNames)
            {
                if (existingIds.Contains(pid)) continue;
                var enabled = enabledMap.GetValueOrDefault(pid, true);
                if (!enabled) continue;
                var opt = new ProviderOption
                {
                    ProviderId = pid,
                    DisplayName = name,
                    IsInstalled = true,
                    IsSelected = true
                };
                opt.PropertyChanged += OnProviderOptionChanged;
                Providers.Add(opt);
            }

            // 3. 重新加载数据
            _ = LoadDataAsync();
        });
    }

    private void OnProviderOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProviderOption.IsSelected))
            _ = LoadDataAsync();
    }

    /// <summary>
    /// 按当前筛选条件加载历史数据。多 Provider 时折叠为平均；折线图保留明细。
    /// </summary>
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "正在加载…";
        NoDataWarning = false;
        try
        {
            var selected = Providers.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                LineSeries.Clear();
                HeatMapCells.Clear();
                DetailRows.Clear();
                RingSeries = new HistorySeries();
                BarValues = Array.Empty<double>();
                ActiveDaysText = "--"; PeakUsageText = "--"; AvgUsageText = "--";
                StatusMessage = "未勾选任何 Provider";
                NoDataWarning = true;
                return;
            }

            var (from, to) = ComputeRange();
            var ids = selected.Select(p => p.ProviderId).ToList();

            // 折线图：每个 provider 一条 series
            var lineList = new List<HistorySeries>();
            // 在多 provider 时统一折线笔刷
            var brushes = new[]
            {
                Brushes.SteelBlue, Brushes.MediumSeaGreen, Brushes.DarkOrange,
                Brushes.MediumPurple, Brushes.Crimson, Brushes.Teal
            };
            int bIdx = 0;
            int maxCount = 0;
            List<string>? longestDates = null;
            foreach (var p in selected)
            {
                var records = await _repository.QueryPointsAsync(p.ProviderId, from, to);
                var values = records.Select(r => (double)r.UsedPercent).ToList();
                // req-040：取数据点最多的 Provider 的日期作为横坐标标签
                if (values.Count > maxCount)
                {
                    maxCount = values.Count;
                    longestDates = records.Select(r => r.RecordedAt.ToString("M/d", CultureInfo.InvariantCulture)).ToList();
                }
                lineList.Add(new HistorySeries
                {
                    ProviderId = p.ProviderId,
                    DisplayName = p.IsInstalled ? p.DisplayName : p.ProviderId,
                    LineBrush = brushes[bIdx++ % brushes.Length],
                    Values = values
                });
            }
            LineDates = longestDates ?? (IReadOnlyList<string>)Array.Empty<string>();

            LineSeries.Clear();
            foreach (var ls in lineList)
                LineSeries.Add(ls);

            // 单 Provider 时填充圆环摘要数据
            if (selected.Count == 1 && lineList.Count == 1)
            {
                var only = lineList[0];
                RingSeries = new HistorySeries
                {
                    ProviderId = only.ProviderId,
                    DisplayName = only.DisplayName,
                    LineBrush = only.LineBrush,
                    Values = only.Values
                };
            }
            else
            {
                RingSeries = new HistorySeries();
            }

            // req-013：详情表格数据源切换为"刷新聚合"（usage_refresh_aggregates）。
            // 注意：热力图仍使用 usage_daily（与原逻辑保持一致），仅详情表格同步换为 refresh aggregates。
            var refreshList = await _repository.QueryRefreshAggregatesAsync(ids, from, to);
            // req-039：诊断日志，确认查询返回的记录数
            UsageMonitor.Core.Services.FileLogger.Info("HistoryViewModel",
                $"LoadDataAsync: QueryRefreshAggregatesAsync returned {refreshList.Count} records for providers=[{string.Join(",", ids)}] from={from} to={to}");
            var dailyList = await _repository.QueryDailyAsync(ids, from, to);
            DetailRows.Clear();
            foreach (var agg in refreshList)
            {
                var name = selected.FirstOrDefault(p =>
                                string.Equals(p.ProviderId, agg.ProviderId, StringComparison.OrdinalIgnoreCase));
                DetailRows.Add(new DailyDetailRow
                {
                    ProviderId = agg.ProviderId,
                    ProviderName = name?.DisplayName ?? agg.ProviderId,
                    Day = agg.BusinessDay,
                    RefreshedAtText = agg.RefreshAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    MaxPercent = $"{agg.MaxUsedPercent:0.##}%",
                    MinPercent = $"{agg.MinUsedPercent:0.##}%",
                    EndPercent = $"{agg.EndUsedPercent:0.##}%",
                    AvgPercent = $"{agg.AvgUsedPercent:0.##}%",
                    Snapshots = agg.SnapshotCount.ToString(CultureInfo.InvariantCulture)
                });
            }

            // 热力图单元：按日期取各 provider "end_used_percent" 的平均
            var dateBuckets = dailyList
                .GroupBy(d => d.Day, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var cells = new List<YearHeatMapCell>();
            foreach (var bucket in dateBuckets)
            {
                // 取当日所有 provider EndUsedPercent 的平均
                double avg = bucket.Select(b => b.EndUsedPercent).Average();
                cells.Add(new YearHeatMapCell
                {
                    Day = bucket.Key,
                    Percent = avg,
                    Background = SelectHeatMapBrush(avg)
                });
            }
            HeatMapCells.Clear();
            foreach (var c in cells) HeatMapCells.Add(c);

            // 柱状图数据 + 统计卡（活跃天数 / 峰值 / 平均），对齐参考图
            BarValues = lineList.Count > 0 ? lineList[0].Values : Array.Empty<double>();
            ActiveDaysText = dateBuckets.Count.ToString(CultureInfo.InvariantCulture);
            PeakUsageText = dailyList.Count > 0 ? $"{dailyList.Max(d => d.MaxUsedPercent):0.#}%" : "--";
            AvgUsageText = dailyList.Count > 0 ? $"{dailyList.Average(d => d.AvgUsedPercent):0.#}%" : "--";

            StatusMessage = LineSeries.Count == 0
                ? "暂无任何历史数据，先在主窗口点几下'刷新'"
                : $"已加载 {LineSeries.Count} 个 Provider，{DetailRows.Count} 行刷新聚合";
            NoDataWarning = LineSeries.Count == 0;

            // 通知依赖于 Provider 选择数变化的 UI 属性
            OnPropertyChanged(nameof(IsSingleProviderSelected));
        }
        catch (Exception ex)
        {
            StatusMessage = "加载失败：" + ex.Message;
            NoDataWarning = true;
            UsageMonitor.Core.Services.FileLogger.Error("HistoryViewModel",
                "LoadDataAsync failed", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 把 EndUsedPercent (0-100) 映射为与全局一致的分档刷子（低绿 / 注意金 #facd14 / 中橙 / 高红）。
    /// 档位与颜色统一由 <see cref="UsageMonitor.App.Helpers.UsageTierScale"/> 定义，
    /// 使历史窗口热力图与主界面进度条保持完全一致（含 50% 金色档）。
    /// </summary>
    private static Brush SelectHeatMapBrush(double percent)
        => UsageMonitor.App.Helpers.UsageTierScale.ResolveBrush(percent);

    /// <summary>
    /// 把 SelectedRange 转成 (from, to) 时间范围。
    /// </summary>
    private (DateTime from, DateTime to) ComputeRange()
    {
        var now = DateTime.Now;
        var to = now;
        DateTime from;
        switch (_selectedRange)
        {
            case HistoryRange.Last7Days:
                from = now.AddDays(-7); break;
            case HistoryRange.Last30Days:
                from = now.AddDays(-30); break;
            case HistoryRange.Last90Days:
                from = now.AddDays(-90); break;
            case HistoryRange.All:
            default:
                from = DateTime.MinValue; break;
        }
        return (from, to);
    }

    /// <summary>
    /// 把当前 DetailRows 导出为 CSV 字符串，供"导出 CSV"按钮使用。
    /// </summary>
    public string ExportCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Provider,Day,MaxUsedPercent,MinUsedPercent,EndUsedPercent,AvgUsedPercent,Snapshots");
        foreach (var row in DetailRows)
        {
            sb.Append(EscapeCsv(row.ProviderName)).Append(',')
              .Append(EscapeCsv(row.Day)).Append(',')
              .Append(row.MaxPercent.TrimEnd('%')).Append(',')
              .Append(row.MinPercent.TrimEnd('%')).Append(',')
              .Append(row.EndPercent.TrimEnd('%')).Append(',')
              .Append(row.AvgPercent.TrimEnd('%')).Append(',')
              .Append(row.Snapshots)
              .AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// CSV 字段转义：含逗号 / 引号 / 换行的字段需加双引号并将内部引号翻倍。
    /// </summary>
    private static string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// 把 CSV 写到用户选择的文件路径（由 XAML 端 SaveFileDialog 调用）。
    /// </summary>
    public bool SaveCsvToFile(string filePath)
    {
        try
        {
            File.WriteAllText(filePath, ExportCsv(), new UTF8Encoding(true));
            return true;
        }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Error("HistoryViewModel",
                $"SaveCsvToFile({filePath}) failed", ex);
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
