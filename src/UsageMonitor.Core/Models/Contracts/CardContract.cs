using System;
using UsageMonitor.Core.Models.Attributes;

namespace UsageMonitor.Core.Models.Contracts;

/// <summary>
/// 卡片字段组契约（req-100 B5）。
/// <para>
/// 定义用量卡片「数据区」需要的标准字段。插件将网页/API 原始数据映射并写入这些 SDK 字段，
/// 主程序按契约取数展示，不再硬编码字段名。每个属性用 <see cref="FieldUsageAttribute"/> 标注用途，
/// 便于设置界面按用途（Data/Theme/Setting）分类显示未匹配字段（见 req-100 B7）。
/// </para>
/// </summary>
public class CardContract
{
    /// <summary>已用量（数据字段）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public double UsedAmount { get; set; }

    /// <summary>总量（数据字段）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public double TotalAmount { get; set; }

    /// <summary>使用百分比 0-100（数据字段）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public double UsagePercent { get; set; }

    /// <summary>下次重置时间（数据字段，可空）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public DateTime? NextResetTime { get; set; }

    /// <summary>余额（数据字段，可空；API 模式常用）。</summary>
    [FieldUsage(FieldUsage.Data)]
    public decimal? Balance { get; set; }
}
