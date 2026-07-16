namespace UsageMonitor.Core.Models;

/// <summary>
/// 主窗口用量卡片中展示的图表类型。
/// <para>
/// 每个 Provider 可在插件配置窗口独立选择，随 <c>AppSettings.ProviderCardCharts</c>
/// （key = ProviderId）持久化。<see cref="None"/> 表示不显示图表、仅保留进度条。
/// </para>
/// </summary>
public enum CardChartKind
{
    /// <summary>不显示图表（仅进度条，默认）</summary>
    None,

    /// <summary>折线图（带渐变面积填充）</summary>
    Line,

    /// <summary>柱状图</summary>
    Bar,

    /// <summary>圆环进度图</summary>
    Ring,

    /// <summary>热力图</summary>
    HeatMap,

    /// <summary>日月"编程时段"弧线图</summary>
    DayNightArc
}
