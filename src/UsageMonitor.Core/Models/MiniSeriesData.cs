namespace UsageMonitor.Core.Models;

/// <summary>
/// 迷你图表序列数据点（X = 时间戳，Y = 数值）。
/// <para>
/// 用于任务栏迷你时序图表（柱状 / 折线 / 面积）的数据契约：
/// X 轴限定为日期/时间维度，Y 轴限定为数值（百分比或绝对数值）。
/// </para>
/// </summary>
public sealed class MiniSeriesPoint
{
    /// <summary>数据点时间戳（X 轴，日期/时间维度）。</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>数据点数值（Y 轴，百分比 0-100 或绝对数值）。</summary>
    public double Value { get; init; }
}

/// <summary>
/// 迷你图表 Y 轴数值类型约束。
/// <para>SDK 通过此枚举限定序列纵坐标的语义，渲染端据此选择格式化方式。</para>
/// </summary>
public enum MiniSeriesValueKind
{
    /// <summary>百分比（0-100），tooltip 格式化为 "42%"。</summary>
    Percent = 0,

    /// <summary>绝对数值（Token 用量、Credits、调用次数、费用等），tooltip 按 <see cref="MiniSeriesData.Unit"/> 格式化。</summary>
    Number = 1
}

/// <summary>
/// 迷你图表时间序列数据集（一个数据组对应一份序列）。
/// <para>
/// 数据流：Provider 刷新时将序列写入 <c>UsageInfo.Extra["mini_series:{seriesKey}"]</c>，
/// 宿主 <c>ProviderUsageViewModel</c> 解析缓存后由 <c>MiniChartItemViewModel</c> 按当前数据组取出渲染。
/// </para>
/// <para>
/// 序列上限约定：单组最多 90 点（与 <see cref="QueryRange"/> 最大窗口对齐），超出部分由宿主截断。
/// </para>
/// </summary>
public sealed class MiniSeriesData
{
    /// <summary>序列数据点列表（按时间升序排列）。</summary>
    public IReadOnlyList<MiniSeriesPoint> Points { get; init; } = Array.Empty<MiniSeriesPoint>();

    /// <summary>Y 轴数值类型（百分比 / 绝对数值），影响 tooltip 与气泡的格式化。</summary>
    public MiniSeriesValueKind ValueKind { get; init; } = MiniSeriesValueKind.Percent;

    /// <summary>数值单位（如 "%"、"credits"、"次"、"万tokens"）；ValueKind=Percent 时可为 null（渲染端自动补 %）。</summary>
    public string? Unit { get; init; }

    /// <summary>Y 轴上限；null 时渲染端自动取序列最大值（全零时兜底为 1）。</summary>
    public double? MaxValue { get; init; }

    /// <summary>序列最大点数常量（超出截断，保留最新的点）。</summary>
    public const int MaxPointCount = 90;

    /// <summary>
    /// 从原始点列表创建序列数据集，自动截断超出 <see cref="MaxPointCount"/> 的旧数据（保留最新）。
    /// </summary>
    /// <param name="points">原始数据点（时间升序）</param>
    /// <param name="valueKind">Y 轴数值类型</param>
    /// <param name="unit">数值单位</param>
    /// <param name="maxValue">Y 轴上限（null=自动）</param>
    /// <returns>截断后的序列数据集</returns>
    public static MiniSeriesData Create(
        IReadOnlyList<MiniSeriesPoint> points,
        MiniSeriesValueKind valueKind = MiniSeriesValueKind.Percent,
        string? unit = null,
        double? maxValue = null)
    {
        if (points == null || points.Count == 0)
            return new MiniSeriesData { ValueKind = valueKind, Unit = unit, MaxValue = maxValue };

        // 超出上限时保留最新的 N 个点（列表尾部）
        IReadOnlyList<MiniSeriesPoint> effective = points.Count > MaxPointCount
            ? points.Skip(points.Count - MaxPointCount).ToList()
            : points;

        return new MiniSeriesData
        {
            Points = effective,
            ValueKind = valueKind,
            Unit = unit,
            MaxValue = maxValue
        };
    }
}
