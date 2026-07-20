using UsageMonitor.Core.Models;

namespace UsageMonitor.Plugin.Qoder;

/// <summary>
/// req-087-B5：Qoder 图表数据转换。
/// 将表格记录转换为折线图、柱状图、热力图数据结构。
/// </summary>
public static class QoderChartAdapter
{
    /// <summary>
    /// 转换为折线图数据（按时间走势）。
    /// </summary>
    /// <param name="records">用量记录列表</param>
    /// <returns>折线图数据点列表</returns>
    public static List<LineChartPoint> ToLineChartData(IEnumerable<QoderUsageRecord> records)
    {
        return records
            .Where(r => r.CreditsParsed && r.Time > DateTime.MinValue)
            .OrderBy(r => r.Time)
            .Select(r => new LineChartPoint
            {
                Timestamp = r.Time,
                Value = r.CreditsAfter,
                Label = r.ModelTier
            })
            .ToList();
    }

    /// <summary>
    /// 转换为柱状图数据（按模型分级聚合）。
    /// </summary>
    /// <param name="records">用量记录列表</param>
    /// <returns>柱状图数据点列表</returns>
    public static List<BarChartPoint> ToBarChartData(IEnumerable<QoderUsageRecord> records)
    {
        return records
            .Where(r => r.CreditsParsed)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.ModelTier) ? "未知" : r.ModelTier)
            .Select(g => new BarChartPoint
            {
                Label = g.Key,
                Value = g.Sum(r => r.CreditsAfter),
                Count = g.Count()
            })
            .OrderByDescending(b => b.Value)
            .ToList();
    }

    /// <summary>
    /// 转换为热力图数据（按日期+小时段聚合）。
    /// </summary>
    /// <param name="records">用量记录列表</param>
    /// <returns>热力图数据点列表</returns>
    public static List<HeatMapPoint> ToHeatMapData(IEnumerable<QoderUsageRecord> records)
    {
        return records
            .Where(r => r.CreditsParsed && r.Time > DateTime.MinValue)
            .GroupBy(r => new { Date = r.Time.Date, Hour = r.Time.Hour })
            .Select(g => new HeatMapPoint
            {
                Date = g.Key.Date,
                Hour = g.Key.Hour,
                Value = g.Sum(r => r.CreditsAfter),
                Count = g.Count()
            })
            .ToList();
    }

    /// <summary>
    /// 计算总消耗（折后 Credits 总和）。
    /// </summary>
    public static decimal CalculateTotalCredits(IEnumerable<QoderUsageRecord> records)
    {
        return records
            .Where(r => r.CreditsParsed)
            .Sum(r => r.CreditsAfter);
    }

    /// <summary>
    /// 计算总费用（折后费用总和）。
    /// </summary>
    public static decimal CalculateTotalCost(IEnumerable<QoderUsageRecord> records)
    {
        return records
            .Where(r => r.CostParsed)
            .Sum(r => r.CostAfter);
    }
}

/// <summary>
/// 折线图数据点。
/// </summary>
public class LineChartPoint
{
    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>数值（Credits 折后值）</summary>
    public decimal Value { get; set; }

    /// <summary>标签（模型分级）</summary>
    public string? Label { get; set; }
}

/// <summary>
/// 柱状图数据点。
/// </summary>
public class BarChartPoint
{
    /// <summary>标签（模型分级）</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>数值（Credits 总和）</summary>
    public decimal Value { get; set; }

    /// <summary>记录条数</summary>
    public int Count { get; set; }
}

/// <summary>
/// 热力图数据点。
/// </summary>
public class HeatMapPoint
{
    /// <summary>日期</summary>
    public DateTime Date { get; set; }

    /// <summary>小时段（0-23）</summary>
    public int Hour { get; set; }

    /// <summary>数值（Credits 总和）</summary>
    public decimal Value { get; set; }

    /// <summary>记录条数</summary>
    public int Count { get; set; }
}
