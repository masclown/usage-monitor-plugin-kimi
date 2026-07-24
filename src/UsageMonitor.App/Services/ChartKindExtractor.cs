using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.App.Services;

/// <summary>
/// req-107 B6：从 <see cref="IUsageProvider"/> 提取卡片图表类型集合的统一 helper。
/// <para>声明驱动路径（req-107 B6 收敛）：优先从 <see cref="IUsageProvider.Card"/> 声明的 <c>Charts</c> 提取 <see cref="CardChartKind"/>；
/// 旧插件（无 Card 声明）回退 <see cref="IUsageProvider.SupportedCardCharts"/>（向后兼容）。</para>
/// </summary>
public static class ChartKindExtractor
{
    /// <summary>
    /// 提取 Provider 声明支持的卡片图表类型集合。
    /// </summary>
    /// <param name="provider">插件实例（null 返回空列表）。</param>
    /// <returns>声明的 <see cref="CardChartKind"/> 列表（声明驱动优先，回退到旧 SupportedCardCharts）。</returns>
    public static IReadOnlyList<CardChartKind> ExtractDeclaredChartKinds(IUsageProvider? provider)
    {
        if (provider == null) return System.Array.Empty<CardChartKind>();
        var card = provider.Card;
        if (card != null && card.Charts.Count > 0)
        {
            // 声明驱动：从 CardDeclaration.Charts 提取，映射 DeclarativeChartKind → CardChartKind（req-005 旧枚举）
            // 仅映射 PluginConfigWindow 支持的 5 种：Line/Bar/Ring/HeatMap/DayNightArc
            var kinds = new List<CardChartKind>();
            foreach (var ch in card.Charts)
            {
                var mapped = MapDeclarativeToCardKind(ch.Kind);
                if (mapped.HasValue && !kinds.Contains(mapped.Value)) kinds.Add(mapped.Value);
            }
            if (kinds.Count > 0) return kinds;
        }
        return provider.SupportedCardCharts ?? System.Array.Empty<CardChartKind>();
    }

    /// <summary>
    /// DeclarativeChartKind（req-107 新）→ CardChartKind（req-005 旧）映射；null 表示旧枚举无对应值。
    /// <para>声明式 Bar 语义为「进度条组」（5h/周/视频进度条），由 LimitBars/MetricBar 专用渲染路径呈现，
    /// 不映射为 <see cref="CardChartKind.Bar"/>，避免通用 BarChartControl 对进度条数据做无意义的二次柱状渲染。</para>
    /// </summary>
    private static CardChartKind? MapDeclarativeToCardKind(DeclarativeChartKind d) => d switch
    {
        DeclarativeChartKind.Line => CardChartKind.Line,
        // 声明式 Bar = 进度条组（专用渲染路径），旧枚举 CardChartKind.Bar 是通用柱状图，语义不匹配 → 跳过
        DeclarativeChartKind.Bar => null,
        DeclarativeChartKind.Ring => CardChartKind.Ring,
        DeclarativeChartKind.HeatMap => CardChartKind.HeatMap,
        // DeclarativeChartKind.Number / MiniRingChart / MiniText 无对应旧枚举（req-107 新增）
        _ => null
    };
}
