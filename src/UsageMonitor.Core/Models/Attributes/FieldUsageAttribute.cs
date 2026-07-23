using System;

namespace UsageMonitor.Core.Models.Attributes;

/// <summary>
/// 字段用途特性（req-100 B6，已废弃：req-107 声明式框架后由 CardDeclaration + UsageFieldMetadata 替代，保留类型以避免破坏外部引用）。
/// <para>
/// 标注在字段属性上声明该字段的用途（<see cref="FieldUsage.Data"/> / <see cref="FieldUsage.Theme"/> / <see cref="FieldUsage.Setting"/>）；
/// 已无生产消费方，保留为 SDK 兼容层。
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
