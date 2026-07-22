using System.Collections.Generic;
using UsageMonitor.Core.Models.Attributes;

namespace UsageMonitor.Core.Models.Contracts;

/// <summary>
/// Tooltip（托盘悬停/悬浮窗）字段组契约（req-100 B5）。
/// <para>定义托盘 Tooltip 与悬浮窗需要的标准字段：标题、摘要行、额外文本行。</para>
/// </summary>
public class TooltipContract
{
    /// <summary>标题（数据字段，通常为 Provider 名称）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public string Title { get; set; } = string.Empty;

    /// <summary>摘要行（数据字段，如 "剩余 38%"）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>额外文本行集合（数据字段，可空）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public List<string> ExtraLines { get; set; } = new();
}
