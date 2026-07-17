namespace UsageMonitor.App.Controls;

/// <summary>
/// 折线图周期切换按钮的常量字符串集合（req-007）。
/// <para>
/// 与 <c>UsageMonitor.Core.Plugins.IUsageProvider.SetPeriodAsync</c> 的 <c>period</c> 参数保持
/// 一致：宿主在用户点击分段按钮后用这些常量值调用插件，插件据此切换数据窗口。
/// 当前只暴露两类常用周期（周 / 月），未来扩展时（如"近 3 月 / 近 1 年"）只需在此追加常量
/// 并同步更新 <c>MiniLineChartControl</c> 的右上角按钮渲染。
/// </para>
/// </summary>
public static class ChartPeriods
{
    /// <summary>近 7 天（周窗口）。</summary>
    public const string Week = "7d";

    /// <summary>近 30 天（月窗口）。</summary>
    public const string Month = "30d";

    /// <summary>解析周期字符串为对应的"天数"窗口大小；未知值回退到 <see cref="Week"/>。</summary>
    public static int ToDays(string? period) => period switch
    {
        Month => 30,
        Week => 7,
        _ => 7
    };
}
