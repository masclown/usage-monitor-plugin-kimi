using System;
using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Services.Display;

/// <summary>
/// 声明式图表构建器（req-107 B8 渲染消费方）：把 <see cref="CardDeclaration"/> 的图表声明 + 字段取值器
/// 转换为可渲染的图表数据模型（当前实现 Bar → <see cref="MetricBarData"/>）。
/// <para>这是"声明驱动渲染"的核心：渲染不再硬编码"5h/周/视频"进度条，而是遍历 Card 声明的数据组，
/// 按 <see cref="FieldReference"/> 的字段名经取值器解析为值，动态生成进度条。字段标签由 SDK 元数据 + 主程序 i18n 提供（插件零翻译）。</para>
/// </summary>
public static class DeclarativeChartBuilder
{
    /// <summary>
    /// 从卡片声明构建度量进度条数据（合并所有 Bar 图表的数据组）。
    /// </summary>
    /// <param name="card">卡片显示声明（来自插件 defaults.json）。</param>
    /// <param name="valueResolver">字段取值器：标准字段名 → 数值（百分比/次数等）；取不到返回 null。</param>
    /// <param name="textResolver">字段文本取值器：标准字段名 → 文本（如重置时间）；可空。</param>
    /// <param name="labelResolver">字段标签解析器：标准字段名 → 显示标签（来自 SDK 元数据/i18n）。</param>
    /// <param name="visibleChartIds">可见图表 ID 过滤（可空 = 全部）。</param>
    /// <param name="visibleDataGroupIds">可见数据组 ID 过滤（可空 = 全部）。</param>
    /// <param name="tooltipBuilder">按数据组构建悬停提示文本（可空 = 不设置 ToolTip）：每个进度条的 tooltip 由其所属数据组决定，
    /// 避免全部进度条共享同一 tooltip 导致跨数据组字段串扰（如本周限额进度条显示 5h 已用百分比）。</param>
    /// <param name="resetTextResolver">问题4：Reset 角色字段文本解析器（字段名 → 重置剩余文案），填入 FooterText；可空。</param>
    /// <returns>进度条数据；无 Bar 声明时返回 null（宿主回退旧渲染）。</returns>
    public static MetricBarData? BuildMetricBars(
        CardDeclaration? card,
        Func<string, double?> valueResolver,
        Func<string, string?>? textResolver = null,
        Func<string, string>? labelResolver = null,
        IReadOnlyCollection<string>? visibleChartIds = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? visibleDataGroupIds = null,
        Func<DataGroup, string?>? tooltipBuilder = null,
        Func<string, string?>? resetTextResolver = null)
    {
        if (card == null) return null;
        var charts = card.Charts.Where(c => c.Kind == DeclarativeChartKind.Bar);
        if (visibleChartIds != null) charts = charts.Where(c => visibleChartIds.Contains(c.ChartId));
        var bars = new List<MetricBarItem>();
        foreach (var chart in charts)
        {
            var groups = (IEnumerable<DataGroup>)chart.DataGroups;
            if (visibleDataGroupIds != null && visibleDataGroupIds.TryGetValue(chart.ChartId, out var allowed) && allowed != null)
                groups = groups.Where(g => allowed.Contains(g.Id));
            foreach (var group in groups)
            {
                var valueField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
                if (valueField == null) continue;
                var percent = valueResolver(valueField) ?? 0;
                var upperField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Upper)?.FieldName;
                var label = labelResolver?.Invoke(valueField) ?? valueField;
                string? rightText = null;
                if (upperField != null && textResolver != null)
                    rightText = textResolver(upperField);
                // 问题4：解析 Reset 角色字段 → FooterText（重置剩余文案）+ ResetFieldName（实时倒计时渲染键）
                var resetField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Reset)?.FieldName;
                string? footerText = null;
                if (resetField != null && resetTextResolver != null)
                    footerText = resetTextResolver(resetField);
                var bar = new MetricBarItem(label, Math.Max(0, Math.Min(100, percent)), rightText, FooterText: footerText)
                {
                    ResetFieldName = resetField
                };
                if (tooltipBuilder != null)
                    bar = bar with { TooltipText = tooltipBuilder(group) };
                bars.Add(bar);
            }
        }
        return bars.Count > 0 ? new MetricBarData(bars) : null;
    }
}
