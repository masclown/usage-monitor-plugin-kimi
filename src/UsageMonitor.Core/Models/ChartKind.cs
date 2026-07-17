namespace UsageMonitor.Core.Models;

/// <summary>
/// 图表类型枚举（REQ-005 SDK 一期 k1）。
/// <para>
/// 取代 v1 阶段 <see cref="CardChartKind"/> 仅用于内部回退的角色；SDK 一期优先枚举化"图表种类"，
/// 新增图表在 <see cref="Plugins.IUsageChartFactory"/> 注册即可，无需再改此处。
/// 二期（k2）拆 NuGet 时此枚举作为公共契约的一部分保持稳定。
/// </para>
/// </summary>
public enum ChartKind
{
    /// <summary>折线图（带渐变面积填充）。</summary>
    Line,

    /// <summary>柱状图。</summary>
    Bar,

    /// <summary>圆环进度图（任务栏 / 卡片）。</summary>
    Ring,

    /// <summary>年热力图（GitHub 贡献图风格）。</summary>
    HeatMap,

    /// <summary>日月"编程时段"弧线图。</summary>
    DayNightArc
}