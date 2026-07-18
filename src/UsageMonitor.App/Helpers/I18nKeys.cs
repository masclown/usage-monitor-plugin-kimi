namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-024：i18n 扩展点占位（历史窗口范围 / 图表下拉框标签常量集合）。
/// <para>
/// 二期替换方案：把每个 const 替换为读取 .resx 资源（如 <c>Properties.Resources.Strings.History_Range_Last7Days</c>），
/// 配合 <c>Thread.CurrentThread.CurrentUICulture</c> 实现运行时语言切换。届时本类的常量字段全部删除，
/// HistoryViewModel 内的 KeyValuePair 初始化改为 <c>new KVP(range, I18nKeys.Range_Last7Days)</c> 即可。
/// </para>
/// <para>
/// 一期不做国际化框架，仅预留常量集中维护的入口，避免散落在多个文件的中文文案"复制粘贴"。
/// </para>
/// </summary>
public static class I18nKeys
{
    // 历史窗口 - 时间范围
    public const string Range_Last7Days = "最近 7 天";
    public const string Range_Last30Days = "最近 30 天";
    public const string Range_Last90Days = "最近 90 天";
    public const string Range_All = "全部";

    // 历史窗口 - 图表类型
    public const string Chart_Line = "折线图";
    public const string Chart_Bar = "柱状图";
    public const string Chart_HeatMap = "热力图";
    public const string Chart_DayNightArc = "编程时段";
}