namespace UsageMonitor.Core.Models;

/// <summary>
/// 预定义时间范围（req-107 B5）。用于 Period 模式切片器（折线/热力图的"近 7 天 / 近 30 天"等）。
/// <para>切片器选项无需插件写描述：Period 模式用本枚举的内置翻译（主程序 i18n 提供），
/// DataGroup 模式用主字段的 SDK 标签（<see cref="UsageFieldMetadataRegistry"/>）。</para>
/// </summary>
public enum TimeRange
{
    /// <summary>近 7 天。</summary>
    Last7Days,
    /// <summary>近 30 天。</summary>
    Last30Days,
    /// <summary>近 90 天。</summary>
    Last90Days,
    /// <summary>本周。</summary>
    ThisWeek,
    /// <summary>本月。</summary>
    ThisMonth,
    /// <summary>全部。</summary>
    All
}

/// <summary>
/// <see cref="TimeRange"/> 扩展方法：把预定义时间范围解析为天数 / i18n 标签键。
/// </summary>
public static class TimeRangeExtensions
{
    /// <summary>
    /// 获取时间范围对应的天数；<see cref="TimeRange.All"/> 返回 <c>null</c>（不限窗口）。
    /// </summary>
    public static int? ToDays(this TimeRange range) => range switch
    {
        TimeRange.Last7Days => 7,
        TimeRange.Last30Days => 30,
        TimeRange.Last90Days => 90,
        TimeRange.ThisWeek => 7,
        TimeRange.ThisMonth => 30,
        _ => null
    };

    /// <summary>
    /// 获取时间范围的主程序 i18n 标签键（插件零翻译：标签由主程序内置）。
    /// </summary>
    public static string LabelKey(this TimeRange range) => range switch
    {
        TimeRange.Last7Days => "TimeRange.Last7Days",
        TimeRange.Last30Days => "TimeRange.Last30Days",
        TimeRange.Last90Days => "TimeRange.Last90Days",
        TimeRange.ThisWeek => "TimeRange.ThisWeek",
        TimeRange.ThisMonth => "TimeRange.ThisMonth",
        _ => "TimeRange.All"
    };
}
