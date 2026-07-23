namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-069 F-08：可选能力接口——声明式图表支持（卡片字段映射 + 图表类型声明）。
/// <para>实现此接口的插件声明自己有专属的卡片渲染需求（专用字段名、专属图表类型）。
/// 宿主（MainViewModel/App/DisplayModule）通过 <c>provider is IChartSupportProvider</c> 检查能力，按需调用映射逻辑。</para>
/// <para>req-107 B6 渐进拆分（F-08 ISP 原则第二步）：<see cref="IUsageProvider"/> 继承此接口，
/// <see cref="SupportedCardCharts"/> 成员由本接口承载。
/// 旧插件继续实现 <see cref="IUsageProvider"/> 即可（继承得到默认 null/空集合实现）。</para>
/// </summary>
public interface IChartSupportProvider
{
    /// <summary>
    /// 插件声明支持的卡片图表类型（req-005-019）——回退路径。
    /// req-107 B6 收敛目标：优先从 <see cref="IUsageProvider.Card"/> 声明的 <c>Charts</c> 提取（由 <c>ChartKindExtractor</c> 统一读取）。
    /// 本属性保留作为老插件无 Card 声明时的回退。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<Models.CardChartKind> SupportedCardCharts => new[]
    {
        Models.CardChartKind.Line,
        Models.CardChartKind.Bar,
        Models.CardChartKind.Ring
    };
}
