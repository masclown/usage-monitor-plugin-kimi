namespace UsageMonitor.Core.Models.Contracts.Charts;

/// <summary>
/// 折线图契约（req-101 B6）。
/// <para>声明折线图横轴时间字段、纵轴数据字段与纵轴语义（用量/累计用量/百分比）。</para>
/// </summary>
public class LineChartContract
{
    /// <summary>横轴时间字段名。</summary>
    public string TimeField { get; set; } = string.Empty;

    /// <summary>纵轴数据字段名。</summary>
    public string DataField { get; set; } = string.Empty;

    /// <summary>纵轴语义（用量/累计用量/百分比）。</summary>
    public ChartDataSemantics DataSemantics { get; set; } = ChartDataSemantics.Usage;
}

/// <summary>
/// 图表数据语义（req-101 B6）。用于折线图等判断纵轴数值的含义与格式化方式。
/// </summary>
public enum ChartDataSemantics
{
    /// <summary>单期用量（如每日 Token 数）。</summary>
    Usage,

    /// <summary>累计用量（单调递增）。</summary>
    CumulativeUsage,

    /// <summary>百分比 0-100。</summary>
    Percent
}
