using System.Collections.Generic;

namespace UsageMonitor.Core.Models.Contracts;

/// <summary>
/// 卡片显示契约（req-101 B5）。
/// <para>
/// 插件通过此强类型契约声明用量卡片的显示方式：Provider 名称/Logo、运行模式、订阅名称，
/// 以及数字项/进度条项/热力图项的组织方式。主程序据此渲染，插件与主程序共用一套语言。
/// </para>
/// </summary>
public class CardDisplayContract
{
    /// <summary>Provider 显示名称。</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Provider Logo 路径。</summary>
    public string LogoPath { get; set; } = string.Empty;

    /// <summary>运行模式（Api / TokenPlan）。</summary>
    public ProviderMode Mode { get; set; } = ProviderMode.Api;

    /// <summary>订阅名称（TokenPlan 模式可选，如 "TokenPlanMax-年度会员"）。</summary>
    public string? SubscriptionName { get; set; }

    /// <summary>数字项列表（如"累计/峰值/余额"多排数字）。</summary>
    public List<NumberItem> NumberItems { get; set; } = new();

    /// <summary>进度条项列表（如 5h/周限额进度条）。</summary>
    public List<ProgressItem> ProgressItems { get; set; } = new();

    /// <summary>热力图项列表（每日用量日历）。</summary>
    public List<HeatmapItem> HeatmapItems { get; set; } = new();
}

/// <summary>数字项（req-101 B5）。</summary>
public class NumberItem
{
    /// <summary>数字名称（如 "余额"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>数据字段名（从 UsageInfo/Extra 取数用）。</summary>
    public string DataField { get; set; } = string.Empty;

    /// <summary>数据描述（辅助行，可空）。</summary>
    public string? Description { get; set; }

    /// <summary>是否换行（多排布局用）。</summary>
    public bool IsNewLine { get; set; }
}

/// <summary>进度条项（req-101 B5）。</summary>
public class ProgressItem
{
    /// <summary>进度条名称（如 "5h 限额"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>总量字段名。</summary>
    public string TotalField { get; set; } = string.Empty;

    /// <summary>已用数据字段名。</summary>
    public string DataField { get; set; } = string.Empty;

    /// <summary>刷新剩余时间字段名（可空）。</summary>
    public string? RefreshTimeField { get; set; }

    /// <summary>百分比字段名（可空，优先于 Total/Data 计算）。</summary>
    public string? PercentField { get; set; }
}

/// <summary>热力图项（req-101 B5）。</summary>
public class HeatmapItem
{
    /// <summary>日期字段名。</summary>
    public string DateField { get; set; } = string.Empty;

    /// <summary>数值字段名。</summary>
    public string ValueField { get; set; } = string.Empty;
}
