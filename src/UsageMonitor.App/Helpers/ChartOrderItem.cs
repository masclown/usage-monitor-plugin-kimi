using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-097：图表顺序设置页的列表项。
/// </summary>
public class ChartOrderItem
{
    /// <summary>Provider ID（唯一标识）。</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider 显示名称。</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>图表类型。</summary>
    public CardChartKind ChartKind { get; set; }

    /// <summary>图表类型显示名称。</summary>
    public string ChartKindDisplayName => ChartKind switch
    {
        CardChartKind.Line => "折线图",
        CardChartKind.Bar => "柱状图",
        CardChartKind.Ring => "圆环图",
        CardChartKind.HeatMap => "热力图",
        CardChartKind.DayNightArc => "时段弧线",
        _ => ChartKind.ToString()
    };
}
