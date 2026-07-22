using System;

namespace UsageMonitor.Core.Models.Attributes;

/// <summary>
/// 字段用途特性（req-100 B6）。
/// <para>
/// 标注在 SDK 字段组契约类（如 <c>CardContract</c>、<c>MiniRingChartContract</c>）的属性上，
/// 声明该字段的用途（<see cref="FieldUsage.Data"/> / <see cref="FieldUsage.Theme"/> / <see cref="FieldUsage.Setting"/>）。
/// 主程序可通过反射读取此特性，对未匹配字段按用途分类展示（见 req-100 B7）。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FieldUsageAttribute : Attribute
{
    /// <summary>字段用途。</summary>
    public FieldUsage Usage { get; }

    /// <summary>创建字段用途特性。</summary>
    /// <param name="usage">字段用途枚举值。</param>
    public FieldUsageAttribute(FieldUsage usage) => Usage = usage;
}
