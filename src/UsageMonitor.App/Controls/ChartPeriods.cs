namespace UsageMonitor.App.Controls;

using UsageMonitor.Core.Models;

/// <summary>
/// 折线图周期切片器的单个选项（req-107：由插件声明的 <see cref="TimeRange"/> 派生，不再写死）。
/// </summary>
/// <param name="Period">周期键（如 "7d" / "30d" / "90d"）。</param>
/// <param name="Label">按钮显示文案（如 "近 7 天"）。</param>
public sealed record PeriodOption(string Period, string Label);

/// <summary>
/// 折线图周期切换按钮的常量字符串集合（req-007）。
/// <para>
/// 周期键统一为 "Nd" 形式（N = 天数）；插件通过 defaults.json 的
/// <c>slicer.timeRanges</c> 声明可选周期，宿主经 <see cref="FromTimeRange"/> 派生按钮选项，
/// 不再局限于内置的周 / 月两档。
/// </para>
/// </summary>
public static class ChartPeriods
{
    /// <summary>近 7 天（周窗口）。</summary>
    public const string Week = "7d";

    /// <summary>近 30 天（月窗口）。</summary>
    public const string Month = "30d";

    /// <summary>未声明时的默认周期选项（近 7 天 / 近 30 天，向后兼容旧行为）。</summary>
    public static readonly IReadOnlyList<PeriodOption> DefaultOptions = new[]
    {
        new PeriodOption(Week, "近 7 天"),
        new PeriodOption(Month, "近 30 天"),
    };

    /// <summary>解析周期字符串为对应的"天数"窗口大小；支持任意 "Nd" 形式，未知值回退到 <see cref="Week"/>。</summary>
    public static int ToDays(string? period)
    {
        if (!string.IsNullOrEmpty(period) && period!.EndsWith("d", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(period.Substring(0, period.Length - 1), out var n) && n > 0)
            return n;
        return 7;
    }

    /// <summary>把插件声明的 <see cref="TimeRange"/> 派生为周期选项；无限窗口（All）等无法映射为天数时返回 null。</summary>
    public static PeriodOption? FromTimeRange(TimeRange range)
    {
        var days = range.ToDays();
        if (days is not > 0) return null;
        return new PeriodOption($"{days.Value}d", $"近 {days.Value} 天");
    }
}
