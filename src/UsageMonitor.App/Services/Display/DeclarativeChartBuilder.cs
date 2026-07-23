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
    /// <returns>进度条数据；无 Bar 声明时返回 null（宿主回退旧渲染）。</returns>
    public static MetricBarData? BuildMetricBars(
        CardDeclaration? card,
        Func<string, double?> valueResolver,
        Func<string, string?>? textResolver = null,
        Func<string, string>? labelResolver = null)
    {
        if (card == null) return null;
        var bars = new List<MetricBarItem>();
        foreach (var chart in card.Charts.Where(c => c.Kind == DeclarativeChartKind.Bar))
        {
            foreach (var group in chart.DataGroups)
            {
                var valueField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
                if (valueField == null) continue;
                var percent = valueResolver(valueField) ?? 0;
                var upperField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Upper)?.FieldName;
                var label = labelResolver?.Invoke(valueField) ?? valueField;
                string? rightText = null;
                if (upperField != null && textResolver != null)
                    rightText = textResolver(upperField);
                bars.Add(new MetricBarItem(label, Math.Max(0, Math.Min(100, percent)), rightText, FooterText: null));
            }
        }
        return bars.Count > 0 ? new MetricBarData(bars) : null;
    }
}
