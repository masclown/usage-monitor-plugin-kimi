using System.ComponentModel;
using System.Runtime.CompilerServices;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 用量卡片「图表槽位」ViewModel：表示卡片图表区中一个可渲染的图表实例。
/// <para>
/// 卡片图表区由有序的槽位列表驱动（<see cref="ProviderUsageViewModel.CardChartSlots"/>），
/// 每个槽位对应插件 <c>Card.Charts</c> 声明的一个图表实例（允许同一声明图表的多实例，
/// 通过 <see cref="InstanceId"/> 的 <c>#n</c> 后缀区分）。渲染模板按 <see cref="SlotKind"/> 选择。
/// </para>
/// <para>
/// 槽位的可见性（勾选）、顺序、数据组与折叠分界线位置均由
/// <c>AccountCustomization</c>（卡片管理页配置）驱动，配置变更后由
/// <see cref="ProviderUsageViewModel.RebuildChartSlots"/> 重建/刷新。
/// </para>
/// </summary>
public class CardChartSlotViewModel : INotifyPropertyChanged
{
    private readonly ProviderUsageViewModel _owner;
    private MetricBarData? _barData;
    private MetricGridData? _numberData;
    private string? _emptyHint;
    private string? _tooltipText;
    private bool _isAboveDivider;
    private int _ordinal;

    /// <summary>创建图表槽位。</summary>
    /// <param name="owner">所属卡片 ViewModel（数据源）。</param>
    /// <param name="instanceId">图表实例 ID（首个实例等于 chartId，追加实例为 <c>chartId#n</c>）。</param>
    /// <param name="chartId">基础图表声明 ID（去除 <c>#n</c> 后缀）。</param>
    /// <param name="kind">声明式图表类型。</param>
    /// <param name="ordinal">在有序列表中的位置（0 起）。</param>
    /// <param name="isAboveDivider">是否位于折叠分界线之上（折叠时仍可见）。</param>
    public CardChartSlotViewModel(ProviderUsageViewModel owner, string instanceId, string chartId,
        DeclarativeChartKind kind, int ordinal, bool isAboveDivider)
    {
        _owner = owner;
        InstanceId = instanceId;
        ChartId = chartId;
        Kind = kind;
        _ordinal = ordinal;
        _isAboveDivider = isAboveDivider;
        SlotKind = kind switch
        {
            DeclarativeChartKind.Bar => "BarGroup",
            DeclarativeChartKind.Line => "Line",
            DeclarativeChartKind.Ring => "Ring",
            DeclarativeChartKind.HeatMap => "HeatMap",
            DeclarativeChartKind.Number => "Number",
            DeclarativeChartKind.StackedBar => "StackedBar",
            DeclarativeChartKind.Area => "Area",
            _ => kind.ToString()
        };
    }

    /// <summary>问题5：折叠分界线专用私有构造（SlotKind="Divider"，不对应任何图表声明）。</summary>
    private CardChartSlotViewModel(ProviderUsageViewModel owner, int ordinal)
    {
        _owner = owner;
        InstanceId = "__divider__";
        ChartId = "__divider__";
        Kind = default;
        _ordinal = ordinal;
        _isAboveDivider = false; // 折叠态随分界线下方图表一同隐藏
        IsDivider = true;
        SlotKind = "Divider";
    }

    /// <summary>问题5：创建折叠分界线槽位（卡片图表区在分界位置渲染浅灰色横线）。</summary>
    public static CardChartSlotViewModel CreateDivider(ProviderUsageViewModel owner, int ordinal)
        => new(owner, ordinal);

    /// <summary>问题5：是否为折叠分界线槽位（分界线不参与 Bar/Number 数据刷新）。</summary>
    public bool IsDivider { get; }

    /// <summary>所属卡片 ViewModel（模板经此绑定卡片级数据，如折线/热力图/圆环）。</summary>
    public ProviderUsageViewModel Owner => _owner;

    /// <summary>图表实例 ID（首个实例等于 chartId，追加实例为 <c>chartId#n</c>）。</summary>
    public string InstanceId { get; }

    /// <summary>基础图表声明 ID（去除 <c>#n</c> 后缀，用于回查 <c>Card.Charts</c> 声明）。</summary>
    public string ChartId { get; }

    /// <summary>声明式图表类型。</summary>
    public DeclarativeChartKind Kind { get; }

    /// <summary>模板选择键（"BarGroup"/"Line"/"Ring"/"HeatMap"/"Number"）。</summary>
    public string SlotKind { get; }

    /// <summary>在有序列表中的位置（0 起）。</summary>
    public int Ordinal
    {
        get => _ordinal;
        set { if (_ordinal != value) { _ordinal = value; OnPropertyChanged(); } }
    }

    /// <summary>是否位于折叠分界线之上（卡片折叠时仍可见）。</summary>
    public bool IsAboveDivider
    {
        get => _isAboveDivider;
        set { if (_isAboveDivider != value) { _isAboveDivider = value; OnPropertyChanged(); } }
    }

    /// <summary>进度条组（Bar）槽位的度量进度条数据（按本实例可见数据组构建）。</summary>
    public MetricBarData? BarData
    {
        get => _barData;
        set { if (!ReferenceEquals(_barData, value)) { _barData = value; OnPropertyChanged(); } }
    }

    /// <summary>数字（Number）槽位的度量数字网格数据（按本实例数据组构建）。</summary>
    public MetricGridData? NumberData
    {
        get => _numberData;
        set { if (!ReferenceEquals(_numberData, value)) { _numberData = value; OnPropertyChanged(); } }
    }

    /// <summary>堆叠柱状图（StackedBar）槽位的图表数据（按声明的 pivot 字段构建）。</summary>
    public StackedBarChartData? StackedBarData
    {
        get => _stackedBarData;
        set { if (!ReferenceEquals(_stackedBarData, value)) { _stackedBarData = value; OnPropertyChanged(); } }
    }
    private StackedBarChartData? _stackedBarData;

    /// <summary>面积图（Area）槽位的图表数据（按声明的 pivot 字段构建）。</summary>
    public AreaChartData? AreaData
    {
        get => _areaData;
        set { if (!ReferenceEquals(_areaData, value)) { _areaData = value; OnPropertyChanged(); } }
    }
    private AreaChartData? _areaData;

    /// <summary>问题1：槽位悬停提示文本（按用户勾选的 tooltip 字段顺序生成；null = 不显示 tooltip）。</summary>
    public string? TooltipText
    {
        get => _tooltipText;
        set { if (_tooltipText != value) { _tooltipText = value; OnPropertyChanged(); } }
    }

    /// <summary>空数据提示文本：图表所有数据组均未勾选时显示（非空时模板展示灰色提示而非图表内容）。</summary>
    public string? EmptyHint
    {
        get => _emptyHint;
        set { if (_emptyHint != value) { _emptyHint = value; OnPropertyChanged(); } }
    }

    /// <summary>问题6：槽位本身是否可见——图表声明了数据组但全部未勾选时为 false（整个图表隐藏而非仅显示空提示）。</summary>
    public bool IsSlotVisible
    {
        get => _isSlotVisible;
        set { if (_isSlotVisible != value) { _isSlotVisible = value; OnPropertyChanged(); } }
    }
    private bool _isSlotVisible = true;

    /// <summary>问题3：图表显示名（i18n 解析后，如“每日消费”），供槽位标题头展示；分界线槽位为空。</summary>
    public string? ChartDisplayName
    {
        get => _chartDisplayName;
        set { if (_chartDisplayName != value) { _chartDisplayName = value; OnPropertyChanged(); } }
    }
    private string? _chartDisplayName;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
