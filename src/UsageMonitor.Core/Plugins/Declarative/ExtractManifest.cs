using System.Collections.Generic;

namespace UsageMonitor.Core.Plugins.Declarative;

/// <summary>
/// 单条提取指令（req-107 B3，原 req-106）：声明"用哪种提取器、从哪段来源、抽取哪个 SDK 字段、套哪个转换器"。
/// <para>对应 extract.json 的一个 extract 项。单值提取用 <see cref="Source"/>；
/// 表格提取用 <see cref="RowMapping"/>（tool=table）。插件零翻译——<see cref="TargetField"/> 必须是
/// <see cref="UsageMonitor.Core.Models.UsageFields"/> 标准字段名。</para>
/// </summary>
public sealed class ExtractDirective
{
    /// <summary>提取器类型：css / xpath / regex / jsonpath / table。</summary>
    public string Tool { get; init; } = "regex";

    /// <summary>来源表达式（CSS 选择器 / XPath / 正则 / JSONPath / 表格行选择器）。</summary>
    public string? Source { get; init; }

    /// <summary>转换器名（parsePercent / parseNumber / parseDate / trim 等，见 <see cref="Transformers"/>）。</summary>
    public string? Transform { get; init; }

    /// <summary>目标 SDK 标准字段名（<see cref="UsageMonitor.Core.Models.UsageFields"/> 常量）。</summary>
    public string? TargetField { get; init; }

    /// <summary>表格提取的行映射（tool=table 时使用）。</summary>
    public TableRowMapping? RowMapping { get; init; }
}

/// <summary>
/// 表格行映射（req-107 B3）：声明表格各列如何映射到 SDK 字段。
/// </summary>
public sealed class TableRowMapping
{
    /// <summary>列映射列表（按列索引）。</summary>
    public IReadOnlyList<ColumnMapping> Columns { get; init; } = System.Array.Empty<ColumnMapping>();
}

/// <summary>
/// 表格列映射（req-107 B3）：单个列索引 → SDK 字段 + 转换器。
/// </summary>
public sealed class ColumnMapping
{
    /// <summary>列索引（0 起）。</summary>
    public int Index { get; init; }

    /// <summary>目标 SDK 标准字段名。</summary>
    public string? TargetField { get; init; }

    /// <summary>转换器名。</summary>
    public string? Transform { get; init; }
}

/// <summary>
/// 抓取声明清单（req-107 B3）：extract.json 反序列化的强类型根。
/// <para>声明"网页数据 → SDK 标准字段"的全部提取指令。简单插件（未来目标）可纯声明零 C#；
/// 复杂插件（如 MiniMax 现阶段）保留 DLL 抓取，不使用本清单。</para>
/// </summary>
public sealed class ExtractManifest
{
    /// <summary>提取指令列表。</summary>
    public IReadOnlyList<ExtractDirective> Extract { get; init; } = System.Array.Empty<ExtractDirective>();
}
