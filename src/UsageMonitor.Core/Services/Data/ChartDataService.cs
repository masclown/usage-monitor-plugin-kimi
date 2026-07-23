using System;
using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Data;

/// <summary>图表取数结果（req-107 B8）：标签（Meta，如日期）+ 数值（Value）两条平行序列。</summary>
/// <param name="Labels">Meta 维度标签（如 yyyy-MM-dd 日期）。</param>
/// <param name="Values">Value 维度数值。</param>
/// <param name="Field">Value 字段名（供单位/格式化解析）。</param>
public sealed record ChartSeries(IReadOnlyList<string> Labels, IReadOnlyList<double> Values, string Field);

/// <summary>
/// 声明式图表取数服务（req-107 B8）：按 <see cref="DataGroup"/> 的字段引用（Meta/Value role）+ <see cref="QueryRange"/>
/// 从时序明细表（<c>usage_daily_trend</c>）解析为图表序列，供声明式折线/热力图渲染消费。
/// <para>取数与渲染解耦：本服务只产出 <see cref="ChartSeries"/>，渲染切换（ChartCardTemplateSelector 路由）在其上接线。
/// 字段名经 <see cref="UsageFields"/> 标准字段解析到明细表列，体现"声明驱动取数"。</para>
/// </summary>
public sealed class ChartDataService
{
    private readonly UsageDetailRepository _detailRepository;

    /// <summary>创建取数服务。</summary>
    /// <param name="detailRepository">时序明细仓储。</param>
    public ChartDataService(UsageDetailRepository detailRepository)
    {
        _detailRepository = detailRepository ?? throw new ArgumentNullException(nameof(detailRepository));
    }

    /// <summary>
    /// 按数据组取每日趋势序列：解析 Meta（日期）+ Value 字段引用与 queryRange，从 <c>usage_daily_trend</c> 读取。
    /// </summary>
    /// <param name="providerId">插件 ID。</param>
    /// <param name="accountId">账号 ID（多账号隔离，缺省 default）。</param>
    /// <param name="dataGroup">数据组声明（含字段引用与查询窗口）。</param>
    /// <returns>图表序列；无法解析 Meta/Value 时返回 null。</returns>
    public ChartSeries? GetDailyTrendSeries(string providerId, string accountId, DataGroup dataGroup)
    {
        if (dataGroup == null) return null;
        var valueField = dataGroup.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
        var hasMetaDate = dataGroup.Fields.Any(f => f.Role == FieldRole.Meta);
        if (valueField == null || !hasMetaDate) return null;

        var (from, to) = ResolveDateRange(dataGroup.QueryRange);
        var rows = _detailRepository.GetDailyTrend(providerId, accountId, from, to);

        var labels = new List<string>(rows.Count);
        var values = new List<double>(rows.Count);
        foreach (var row in rows)
        {
            labels.Add(row.Date);
            values.Add(SelectTrendValue(row, valueField));
        }
        return new ChartSeries(labels, values, valueField);
    }

    /// <summary>按 Value 字段名从趋势行取对应数值（token_total 或缓存命中率）。</summary>
    private static double SelectTrendValue(DailyTrendRow row, string valueField) => valueField switch
    {
        UsageFields.DailyCacheHitValue or UsageFields.CacheHitPercent or UsageFields.DailyCacheHitPercent => row.CacheHitPercent ?? 0,
        _ => row.TokenTotal // daily_token_value / token_total 等取当日总 Token
    };

    /// <summary>
    /// 把 <see cref="QueryRange"/> 解析为 (fromDate, toDate)；缺省取近 30 天窗口。
    /// </summary>
    private static (string? from, string? to) ResolveDateRange(QueryRange? range)
    {
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        int days;
        if (range?.Days is > 0)
        {
            days = range.Days.Value;
        }
        else if (range?.Range != null)
        {
            days = range.Range.Value.ToDays() ?? 30;
        }
        else
        {
            days = 30;
        }
        var from = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        return (from, to);
    }
}
