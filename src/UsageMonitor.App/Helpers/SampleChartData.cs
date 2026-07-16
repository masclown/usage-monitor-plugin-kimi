namespace UsageMonitor.App.Helpers;

/// <summary>
/// 图表示例 / 推断数据源。
/// <para>
/// 现阶段（真实数据管线接入前）为主窗口卡片图表与插件配置预览提供"形态贴近参考图"的示例序列。
/// 真实数据接入后，只需把真实序列喂给各图表控件的数据依赖属性即可，UI 与控件无需改动。
/// </para>
/// </summary>
public static class SampleChartData
{
    /// <summary>折线示例：一段"先升后降"的已用百分比序列（0-100）。</summary>
    public static IReadOnlyList<double> UsageTrend { get; } = new double[]
    {
        12, 28, 44, 61, 74, 68, 55, 63, 82, 90, 76, 58, 47, 52, 66
    };

    /// <summary>柱状示例：按天的消费 / 请求量形态（任意量纲，用于展示柱状图样式）。</summary>
    public static IReadOnlyList<double> DailyBars { get; } = new double[]
    {
        8, 14, 6, 22, 39, 17, 11, 48, 63, 30, 19, 12, 25, 41, 9
    };

    /// <summary>编程时段示例：24 小时活跃度（0-1），白天高、凌晨低，对应参考图日月弧线形态。</summary>
    public static IReadOnlyList<double> HourlyActivity { get; } = new double[]
    {
        0.05, 0.03, 0.02, 0.02, 0.03, 0.08,
        0.25, 0.45, 0.60, 0.72, 0.82, 0.88,
        0.90, 0.86, 0.80, 0.70, 0.62, 0.55,
        0.60, 0.50, 0.38, 0.28, 0.15, 0.08
    };

    /// <summary>
    /// 当真实序列不足以绘制（少于 2 个点）时返回示例折线，否则原样返回真实数据。
    /// </summary>
    public static IReadOnlyList<double> EnsureSeries(IReadOnlyList<double>? data)
        => (data != null && data.Count >= 2) ? data : UsageTrend;
}
