using System.Collections.Generic;
using UsageMonitor.Core.Models.Attributes;

namespace UsageMonitor.Core.Models.Contracts;

/// <summary>
/// Mini 环形图字段组契约（req-100 B5）。
/// <para>定义任务栏迷你环形图所需的标准字段：Provider 名称/Logo（数据）、数据项（数据）、色阶设置（设置）。</para>
/// </summary>
public class MiniRingChartContract
{
    /// <summary>Provider 显示名称（数据字段）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Provider Logo 路径（数据字段）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public string LogoPath { get; set; } = string.Empty;

    /// <summary>数据项集合（数据字段）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public List<RingChartDataItem> DataItems { get; set; } = new();

    /// <summary>色阶设置（设置参数字段）。</summary>
    [FieldUsage(FieldUsage.Setting)]
    public ColorTierSettings? ColorTiers { get; set; }

    /// <summary>是否色阶倒序（设置参数字段）。</summary>
    [FieldUsage(FieldUsage.Setting)]
    public bool IsColorReversed { get; set; }
}

/// <summary>
/// 环形图数据项（req-100/101 契约共用）。
/// </summary>
public class RingChartDataItem
{
    /// <summary>数据项标签（如 "5h 限额"）。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>百分比 0-100。</summary>
    public double Percent { get; set; }

    /// <summary>中心/悬停显示的文本（可空，如 "62%" 或剩余额度）。</summary>
    public string? ValueText { get; set; }
}

/// <summary>
/// 色阶设置（req-100/101 契约共用）。用于环形图/热力图按百分比或数值分档着色。
/// </summary>
public class ColorTierSettings
{
    /// <summary>各档位阈值（升序），达到即切换到对应颜色。与 <see cref="Colors"/> 一一对应。</summary>
    public List<double> Thresholds { get; set; } = new();

    /// <summary>各档位颜色（"#RRGGBB"），与 <see cref="Thresholds"/> 一一对应。</summary>
    public List<string> Colors { get; set; } = new();
}
