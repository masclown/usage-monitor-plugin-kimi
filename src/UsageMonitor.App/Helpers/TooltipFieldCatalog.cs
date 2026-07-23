using System.Collections.Generic;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-105：常用 SDK 字段目录（供设置窗口 Tooltip 显示字段多选用）。
/// <para>完整字段白名单在 <c>UsageMonitor.Core.Plugins.IUsageProvider</c> 的 <c>FieldMappings</c>；
/// 此处只列最常用的字段，让设置 UI 简洁。如需更全，调用方可在 <c>CommonFields</c> 之上自定义。</para>
/// </summary>
public static class TooltipFieldCatalog
{
    /// <summary>Tooltip 字段选项（SDK 字段名 + 中文显示）。</summary>
    public sealed record TooltipFieldOption(string FieldName, string Display);

    /// <summary>常用 SDK 字段列表（按显示顺序）。Tooltip UI 勾选列表源。</summary>
    public static readonly IReadOnlyList<TooltipFieldOption> CommonFields = new List<TooltipFieldOption>
    {
        new("daily_token_value", "每日 Token 用量"),
        new("daily_cache_hit_value", "每日缓存命中"),
        new("five_hour_used_percent", "5h 已用百分比"),
        new("weekly_used_percent", "本周已用百分比"),
        new("remaining_credits", "剩余积分"),
        new("video_used_count", "视频已用次数"),
        new("five_hour_reset_at", "5h 重置时间"),
        new("weekly_reset_at", "周重置时间"),
    };
}