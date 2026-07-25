using System;
using System.Collections.Generic;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 问题8：任务栏 Mini 图表的 Tooltip/文本显示字段目录（供设置窗口 S4 页多选与渲染端共用）。
/// <para>固定 5 个可选项：当前 Provider / 账号名 / 5h 用量百分比 / 周用量百分比 / 刷新倒计时。
/// 虚拟字段（__provider_name__ / __refresh_countdown__）非 SDK 数据字段，仅控制显示。</para>
/// </summary>
public static class MiniTooltipFieldCatalog
{
    /// <summary>Mini Tooltip 字段选项（字段名 + 中文显示）。</summary>
    public sealed record MiniTooltipFieldOption(string FieldName, string Display);

    /// <summary>虚拟字段：当前 Provider 显示名。</summary>
    public const string ProviderNameVirtual = "__provider_name__";

    /// <summary>虚拟字段：刷新（5h 重置）倒计时。</summary>
    public const string RefreshCountdownVirtual = "__refresh_countdown__";

    /// <summary>固定可选字段列表（顺序即 tooltip 行 / 文本片段顺序）。</summary>
    private static readonly IReadOnlyList<MiniTooltipFieldOption> Options = new List<MiniTooltipFieldOption>
    {
        new(ProviderNameVirtual, "当前 Provider"),
        new(UsageFields.AccountDisplayName, "账号名"),
        new(UsageFields.FiveHourUsedPercent, "5h 用量百分比"),
        new(UsageFields.WeeklyUsedPercent, "周用量百分比"),
        new(RefreshCountdownVirtual, "刷新倒计时"),
    };

    /// <summary>获取全部可选字段选项（所有 Mini 图表共用同一目录）。</summary>
    public static IReadOnlyList<MiniTooltipFieldOption> GetOptions() => Options;

    /// <summary>获取字段中文显示名（无映射时回退字段名本身）。</summary>
    public static string GetDisplay(string fieldName)
    {
        foreach (var option in Options)
        {
            if (string.Equals(option.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
                return option.Display;
        }
        return fieldName;
    }
}
