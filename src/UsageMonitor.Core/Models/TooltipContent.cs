using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 通用 tooltip 内容容器（REQ-082 SDK v2）。插件自由拼装，SDK 只负责渲染。
/// <para>
/// 设计原则：
/// <list type="bullet">
/// <item>插件决定「显示什么」，SDK 决定「怎么显示」。</item>
/// <item>支持：文本行（普通/加粗/辅助）、色块明细行（图例）、合计行。</item>
/// <item>无业务词汇：「块 / 文本 / 色块 / 汇总」均为通用概念。</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Blocks">内容块序列，按顺序渲染。</param>
public sealed record TooltipContent(
    IReadOnlyList<TooltipBlock> Blocks);

/// <summary>
/// tooltip 内容块基类（REQ-082 SDK v2）。所有具体块类型都继承此 record。
/// </summary>
public abstract record TooltipBlock;

/// <summary>
/// 文本行块（REQ-082 SDK v2）。用于标题、日期、数值等任意文本。
/// </summary>
/// <param name="Text">文本内容。</param>
/// <param name="Style">文本样式（默认 Normal）。</param>
public sealed record TooltipTextBlock(
    string Text,
    TooltipTextStyle Style = TooltipTextStyle.Normal) : TooltipBlock;

/// <summary>
/// 色块明细行块（REQ-082 SDK v2）。同时充当图例：「■ 标签  值」。
/// <para>
/// 用于多系列明细展示（如「deepseek-v4-flash ¥0.43」），色块颜色可由插件指定，
/// null 时由宿主按系列索引从主题色板分配。
/// </para>
/// </summary>
/// <param name="Label">标签文本。</param>
/// <param name="Value">右侧值文本（已格式化）。</param>
/// <param name="Color">颜色（"#RRGGBB" / "ARGB" / null）。</param>
public sealed record TooltipColorRow(
    string Label,
    string Value,
    string? Color = null) : TooltipBlock;

/// <summary>
/// 合计 / 汇总行块（REQ-082 SDK v2）。加粗展示，用于分组总计。
/// </summary>
/// <param name="Label">左侧标签。</param>
/// <param name="Value">右侧汇总值（已格式化）。</param>
public sealed record TooltipSummaryRow(
    string Label,
    string Value) : TooltipBlock;

/// <summary>
/// tooltip 文本样式枚举（REQ-082 SDK v2）。
/// </summary>
public enum TooltipTextStyle
{
    /// <summary>普通文本（12px，次级色）。</summary>
    Normal,

    /// <summary>加粗文本（14px，主色）。</summary>
    Bold,

    /// <summary>辅助文本（10px，三级色）。</summary>
    Secondary
}

/// <summary>
/// tooltip 兼容层（REQ-082 SDK v2）。从旧版 <c>HoverTooltipData(Title, Value, Detail)</c>
/// 快捷构造 <see cref="TooltipContent"/>，用于平滑过渡。
/// </summary>
public static class TooltipCompat
{
    /// <summary>
    /// 把旧版三元组 (Title, Value, Detail) 转换为新的 <see cref="TooltipContent"/>。
    /// </summary>
    /// <param name="title">标题文本（映射为 Secondary 样式）。</param>
    /// <param name="value">主数值（映射为 Bold 样式）。</param>
    /// <param name="detail">详情文本（映射为 Normal 样式，null 时省略）。</param>
    /// <returns>等价的 TooltipContent。</returns>
    public static TooltipContent FromLegacy(string title, string value, string? detail)
    {
        var blocks = new List<TooltipBlock>(3)
        {
            new TooltipTextBlock(title, TooltipTextStyle.Secondary),
            new TooltipTextBlock(value, TooltipTextStyle.Bold)
        };
        if (!string.IsNullOrEmpty(detail))
        {
            blocks.Add(new TooltipTextBlock(detail, TooltipTextStyle.Normal));
        }
        return new TooltipContent(blocks);
    }
}