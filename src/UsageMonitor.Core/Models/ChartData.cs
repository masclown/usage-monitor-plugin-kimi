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

// ============== REQ-082 SDK v2 新增模型 ==============

/// <summary>
/// 多系列堆叠柱状图输入数据（REQ-082 SDK v2）。
/// <para>
/// 用于按类别拆分的多系列叠加展示（例如：按日期 × 模型维度的消费金额堆叠柱）。
/// 业务词汇无关——“类别 / 系列 / 数值”由插件自由命名。
/// </para>
/// </summary>
/// <param name="Categories">X 轴类别标签（如日期）。</param>
/// <param name="Series">堆叠系列（数量动态）。</param>
/// <param name="Unit">数值单位提示（"¥"/"tokens"/"次"），供 tooltip / 轴标签使用。</param>
/// <param name="Title">图表标题（可空）。</param>
public sealed record StackedBarChartData(
    IReadOnlyList<string> Categories,
    IReadOnlyList<ChartSeries> Series,
    string? Unit = null,
    string? Title = null) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.StackedBar;
}

/// <summary>
/// 面积图输入数据（REQ-082 SDK v2），独立控件，非折线变体。
/// <para>
/// 用于单系列面积填充展示（如单模型请求量随时间变化）。
/// </para>
/// </summary>
/// <param name="Values">Y 轴数值序列。</param>
/// <param name="Categories">X 轴标签（可空，缺省时使用 0..N-1 索引）。</param>
/// <param name="MaxValue">Y 轴上限（可空，缺省取序列最大值或 1）。</param>
/// <param name="Unit">数值单位（供 tooltip 使用）。</param>
/// <param name="SeriesName">系列名称（tooltip / 图例显示）。</param>
/// <param name="FillBelowLine">问题7：是否填充曲线下方区域（默认 true）。</param>
/// <param name="SmoothCurve">问题7：是否使用平滑曲线（默认 true）。</param>
public sealed record AreaChartData(
    IReadOnlyList<double> Values,
    IReadOnlyList<string>? Categories = null,
    double? MaxValue = null,
    string? Unit = null,
    string? SeriesName = null,
    bool FillBelowLine = true,
    bool SmoothCurve = true) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Area;
}

/// <summary>
/// 分组容器输入数据（REQ-082 SDK v2）。嵌套其他 Kind 按维度分组展示。
/// <para>
/// 用于「多模型 / 多 Key」场景，每个分组包含多个指标 + 嵌套子图表。
/// </para>
/// </summary>
/// <param name="Groups">分组集合。</param>
public sealed record GroupedChartData(
    IReadOnlyList<ChartGroup> Groups) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Grouped;
}

/// <summary>
/// 单个分组（REQ-082 SDK v2）。
/// </summary>
/// <param name="Title">分组标题（如模型名 "deepseek-v4-flash"）。</param>
/// <param name="Subtitle">副标题（如供应商 / 上下文窗口）。</param>
/// <param name="Metrics">该组内的多个指标。</param>
public sealed record ChartGroup(
    string Title,
    string? Subtitle = null,
    IReadOnlyList<GroupMetric>? Metrics = null);

/// <summary>
/// 单个指标项（REQ-082 SDK v2）。
/// </summary>
/// <param name="Name">指标名（如 "API 请求次数" / "Tokens"）。</param>
/// <param name="FormattedTotal">汇总值文本（已格式化，如 "3,424"）。</param>
/// <param name="Chart">该指标对应的嵌套图表数据（任意 Kind）。</param>
public sealed record GroupMetric(
    string Name,
    string FormattedTotal,
    IChartData Chart);

/// <summary>
/// 度量进度条组输入数据（REQ-082 SDK v2），动态 N 条带标签的进度条。
/// <para>
/// 取代主窗口 XAML 中硬编码的“5h 限额 / 本周限额 / 视频赠送”进度条，
/// 插件可任意声明 Label + Percent + RightText + FooterText，颜色由 IChartPalette / UsageTierScale 决定。
/// </para>
/// </summary>
/// <param name="Bars">进度条项集合。</param>
public sealed record MetricBarData(
    IReadOnlyList<MetricBarItem> Bars) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.MetricBar;
}

/// <summary>
/// 单个进度条项（REQ-082 SDK v2）。
/// </summary>
/// <param name="Label">左侧标签（"5h 限额"/"本周限额"/任意）。</param>
/// <param name="Percent">0-100 进度百分比。</param>
/// <param name="RightText">右侧文本（重置时间等，可空）。</param>
/// <param name="FooterText">底部文本（"已用 43%"，可空）。</param>
/// <param name="ColorHint">颜色提示（null = 由主题/色阶决定）。</param>
/// <param name="IsVisible">是否显示（默认 true）。</param>
public sealed record MetricBarItem(
    string Label,
    double Percent,
    string? RightText = null,
    string? FooterText = null,
    string? ColorHint = null,
    bool IsVisible = true)
{
    /// <summary>req-105：悬停提示文本（卡片管理 tooltip 字段配置驱动；null 表示不显示 ToolTip）。</summary>
    public string? TooltipText { get; init; }

    /// <summary>问题4：本进度条数据组声明的 Reset 角色字段名（如 five_hour_reset_at）。
    /// <para>null = 未声明重置字段；非 null 时宿主可据此在进度条底部渲染实时刷新倒计时。</para></summary>
    public string? ResetFieldName { get; init; }
}

/// <summary>
/// 度量数字网格输入数据（REQ-082 SDK v2），动态 N 个并排独立数字。
/// <para>
/// 取代主窗口 XAML 中硬编码的余额快照 4 项（累计/峰值/活跃/积分余额），
/// 插件可任意声明数字项。
/// </para>
/// </summary>
/// <param name="Items">数字项集合。</param>
public sealed record MetricGridData(
    IReadOnlyList<MetricGridItem> Items) : IChartData
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.MetricGrid;
}

/// <summary>
/// 单个数字项（REQ-082 SDK v2）。
/// </summary>
/// <param name="Label">标签（"累计"/"余额"/任意）。</param>
/// <param name="Value">主数字（已格式化，如 "4.35B"）。</param>
/// <param name="Detail">辅助行（可空，如 "前 12%"）。</param>
/// <param name="ColorHint">颜色提示（null = 主题 Accent）。</param>
/// <param name="IsVisible">是否显示（默认 true）。</param>
/// <param name="Tooltip">问题4：本数据项的独立悬停提示文本（null = 不显示 tooltip；仅含本项数据组相关字段）。</param>
public sealed record MetricGridItem(
    string Label,
    string Value,
    string? Detail = null,
    string? ColorHint = null,
    bool IsVisible = true,
    string? Tooltip = null);

/// <summary>
/// 通用图表系列（REQ-082 SDK v2），用于堆叠柱 / 多系列图表。
/// </summary>
/// <param name="Name">系列名称（"deepseek-v4-flash"等）。</param>
/// <param name="Values">与 Categories 等长的数值序列。</param>
/// <param name="Color">可选指定颜色（缺省由色板分配，可接受 "#RRGGBB" / "ARGB"）。</param>
public sealed record ChartSeries(
    string Name,
    IReadOnlyList<double> Values,
    string? Color = null);