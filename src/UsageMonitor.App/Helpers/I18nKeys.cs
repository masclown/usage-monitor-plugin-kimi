namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-024 / req-070 F-28：i18n 键名常量集合（历史窗口范围 / 图表下拉框标签）。
/// <para>
/// 调用方通过 <c>I18n.T(I18nKeys.Range_Last7Days)</c> 获取当前语言的文案。
/// 键名在 <see cref="UsageMonitor.Core.Services.I18n"/> 静态构造中注册中文默认值。
/// </para>
/// </summary>
public static class I18nKeys
{
    // 历史窗口 - 时间范围
    public const string Range_Last7Days = "history.range.last7days";
    public const string Range_Last30Days = "history.range.last30days";
    public const string Range_Last90Days = "history.range.last90days";
    public const string Range_All = "history.range.all";

    // 历史窗口 - 图表类型
    public const string Chart_Line = "history.chart.line";
    public const string Chart_Bar = "history.chart.bar";
    public const string Chart_HeatMap = "history.chart.heatmap";
    public const string Chart_DayNightArc = "history.chart.daynightarc";
}