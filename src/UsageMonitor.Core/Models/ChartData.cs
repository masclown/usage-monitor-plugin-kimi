using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 图表输入数据基类（REQ-005 SDK）。所有具体图表的强类型 record 都实现此接口。
/// <para>
/// <see cref="Kind"/> 由宿主用 switch 路由到对应的 <c>IUsageChartFactory</c>；
/// data 本身只携带渲染所需的标量 / 序列数据，不关心 UI 实现细节。
/// </para>
/// </summary>
public interface IChartData
{
    /// <summary>图表种类，宿主据此选 DataTemplate / Factory。</summary>
    ChartKind Kind { get; }
}

/// <summary>
/// 折线图输入数据：等距时间序列 + 可选上下界。
/// </summary>
/// <param name="Values">Y 轴数值序列（按时间正序）。</param>
/// <param name="MaxValue">Y 轴上限；缺省时使用序列最大值或 1。</param>
/// <param name="XLabelFormat">X 轴标签格式化字符串（可空）。</param>
/// <param name="YLabelFormat">Y 轴标签格式化字符串（可空）。</param>
public sealed record LineChartData(
    IReadOnlyList<double> Values,
    double? MaxValue = null,
    string? XLabelFormat = null,
    string? YLabelFormat = null) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Line;
}

/// <summary>
/// 柱状图输入数据：等距柱高 + 可选上下界 + 可选标签。
/// </summary>
/// <param name="Values">柱高序列。</param>
/// <param name="MaxValue">Y 轴上限；缺省时使用序列最大值或 1。</param>
/// <param name="Labels">每个柱子的 X 标签（可空，长度不足时柱不显示标签）。</param>
public sealed record BarChartData(
    IReadOnlyList<double> Values,
    double? MaxValue = null,
    IReadOnlyList<string>? Labels = null) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Bar;
}

/// <summary>
/// 圆环图输入数据：当前进度百分比 + 可选警告 / 危险阈值 + 可选中心标签。
/// </summary>
/// <param name="Percent">0~100 的进度百分比（&lt;0 视为 0，&gt;100 视为 100）。</param>
/// <param name="WarningThreshold">达到后切换警告色的百分比（可空，缺省沿用全局）。</param>
/// <param name="DangerThreshold">达到后切换危险色的百分比（可空，缺省沿用全局）。</param>
/// <param name="CenterLabel">中心文本（可空；空时控件按自身规则显示百分比或 metric 文本）。</param>
public sealed record RingChartData(
    double Percent,
    double? WarningThreshold = null,
    double? DangerThreshold = null,
    string? CenterLabel = null) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Ring;
}

/// <summary>
/// 热力图输入数据：二维单元集合 + 行 / 列规模 + 数值上下界。
/// </summary>
/// <param name="Cells">单元格数值，按行主序拉平（length 应等于 Rows × Columns）。</param>
/// <param name="Rows">行数（如 7 表示按周一行）。</param>
/// <param name="Columns">列数。</param>
/// <param name="MinValue">数值下界；缺省时取 Cells 最小值。</param>
/// <param name="MaxValue">数值上界；缺省时取 Cells 最大值或 1。</param>
public sealed record HeatMapData(
    IReadOnlyList<double> Cells,
    int Rows,
    int Columns,
    double? MinValue = null,
    double? MaxValue = null) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.HeatMap;
}

/// <summary>
/// 编程时段图输入数据：24 个整点活跃度 + 可选上午 / 下午峰值时刻。
/// </summary>
/// <param name="HourlyActivity">长度 = 24，每个元素为该小时活跃度（任意非负数，单位由 Provider 决定）。</param>
/// <param name="PeakHour">峰值小时（0~23，可空，缺省由控件自动算）。</param>
public sealed record DayNightArcData(
    IReadOnlyList<double> HourlyActivity,
    int? PeakHour = null) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.DayNightArc;
}