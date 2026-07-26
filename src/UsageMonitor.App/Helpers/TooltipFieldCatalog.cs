using System;
using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-105：Tooltip 字段目录（供设置窗口 Tooltip 显示字段多选用）。
/// <para>字段选项不再使用全局固定列表，而是按图表声明的数据组派生：
/// 每个图表只列出其数据组涉及的 Value 字段 + 虚拟字段（字段名称 / 日期），
/// 避免设置 UI 出现当前图表不涉及的字段（如本周限额进度条不应出现 5h 已用百分比）。</para>
/// </summary>
public static class TooltipFieldCatalog
{
    /// <summary>Tooltip 字段选项（SDK 字段名 + 中文显示）。</summary>
    public sealed record TooltipFieldOption(string FieldName, string Display);

    /// <summary>虚拟字段：字段名称（tooltip 中显示字段显示名行，与值分行展示）。</summary>
    public const string FieldNameVirtual = "__field_name__";

    /// <summary>虚拟字段：日期（tooltip 中显示日期行）。</summary>
    public const string DateVirtual = "__date__";

    /// <summary>字段名称虚拟字段的中文显示。</summary>
    public const string FieldNameDisplay = "字段名称";

    /// <summary>日期虚拟字段的中文显示。</summary>
    public const string DateDisplay = "日期";

    /// <summary>SDK 字段名 → 中文显示名映射（设置 UI 与 tooltip 字段名称行共用，保证一致）。</summary>
    private static readonly Dictionary<string, string> DisplayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [UsageFields.DailyTokenValue] = "每日 Token 用量",
        [UsageFields.DailyCacheHitValue] = "每日缓存命中",
        [UsageFields.FiveHourUsedPercent] = "5h 已用百分比",
        [UsageFields.WeeklyUsedPercent] = "本周已用百分比",
        [UsageFields.RemainingCredits] = "剩余积分",
        [UsageFields.VideoQuota] = "视频赠送",
        [UsageFields.VideoUsedCount] = "视频已用次数",
        [UsageFields.UsedTokens] = "累计用量",
        [UsageFields.MostActiveToken] = "单日峰值",
        [UsageFields.ActiveDays] = "活跃天数",
        [UsageFields.FiveHourResetAt] = "5h 重置时间",
        [UsageFields.WeeklyResetAt] = "周重置时间",
    };

    /// <summary>获取字段中文显示名（未映射时优先回退 <see cref="UsageFieldFormatter"/> 提供的本地化标签，否则返回字段名本身）。</summary>
    /// <remarks>
    /// 解析顺序：① 本表硬编码（与 MiniMax 最贴合的子集）；② <see cref="UsageFieldFormatter.GetLabel"/>
    /// （基于 <c>UsageFieldMetadata.LabelKey</c> i18n + Description，未注册字段返回 snake_case 转 Humanize），
    /// 使 DeepSeek/Kimi/Qoder 等插件的字段也能被识别（避免 "balance_amount" 这种英文直接露出）。
    /// </remarks>
    public static string GetDisplay(string fieldName)
        => DisplayMap.TryGetValue(fieldName, out var display)
            ? display
            : UsageFieldFormatter.GetLabel(fieldName);

    /// <summary>
    /// 按图表声明派生 Tooltip 字段选项（虚拟字段 + 数据组 Value 字段，去重保序）。
    /// <para>① 「字段名称」虚拟字段恒提供；② 「日期」虚拟字段仅当数据组含 Meta 角色字段（日期维度）时提供；
    /// ③ 数据组 Value 角色字段按出现顺序去重列出。</para>
    /// </summary>
    /// <param name="chart">图表声明（提供数据组字段信息）。</param>
    /// <returns>该图表可选的 Tooltip 字段选项列表。</returns>
    public static IReadOnlyList<TooltipFieldOption> GetFieldsForChart(ChartDeclaration chart)
    {
        var options = new List<TooltipFieldOption>
        {
            new(FieldNameVirtual, FieldNameDisplay),
        };

        // 日期虚拟字段：仅当任一数据组含 Meta 角色字段（如 daily_token_date / daily_cache_hit_date）时提供。
        if (chart.DataGroups.Any(g => g.Fields.Any(f => f.Role == FieldRole.Meta)))
            options.Add(new TooltipFieldOption(DateVirtual, DateDisplay));

        // 数据组 Value 字段（去重保序）。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in chart.DataGroups)
        {
            foreach (var field in group.Fields.Where(f => f.Role == FieldRole.Value))
            {
                if (seen.Add(field.FieldName))
                    options.Add(new TooltipFieldOption(field.FieldName, GetDisplay(field.FieldName)));
            }
        }
        return options;
    }
}
