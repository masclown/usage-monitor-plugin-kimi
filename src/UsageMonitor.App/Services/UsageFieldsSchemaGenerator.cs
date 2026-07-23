using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;
using UsageMonitor.Core.Services.Data;

namespace UsageMonitor.App.Services;

/// <summary>
/// req-107 B10 产出：生成 <c>docs/usage-fields-schema.json</c>（SDK 字段 + 图表规格 + 转换器）+ <c>docs/usage-fields-matrix.md</c>（字段映射矩阵）。
/// <para>工具方法供主程序发布时调用，或开发者手动 <c>UsageFieldsSchemaGenerator.GenerateAll(...)</c> 生成；
/// 供插件作者在 VSCode 编辑 defaults.json / extract.json 时获得补全与白名单校验（VSCode JSON Schema 引用）。</para>
/// </summary>
public static class UsageFieldsSchemaGenerator
{
    /// <summary>
    /// 生成 docs/usage-fields-schema.json + docs/usage-fields-matrix.md。
    /// </summary>
    /// <param name="docsDirectory">文档输出目录（通常 <c>./docs/</c>）。</param>
    /// <param name="sdkVersion">当前 SDK 版本字符串。</param>
    public static void GenerateAll(string docsDirectory, string sdkVersion = "0.24.3")
    {
        Directory.CreateDirectory(docsDirectory);
        var schemaJson = UsageFieldsSchemaExporter.Export(sdkVersion);
        var schemaPath = Path.Combine(docsDirectory, "usage-fields-schema.json");
        File.WriteAllText(schemaPath, schemaJson, new UTF8Encoding(false));

        var matrixMd = BuildFieldMatrixMarkdown();
        var matrixPath = Path.Combine(docsDirectory, "usage-fields-matrix.md");
        File.WriteAllText(matrixPath, matrixMd, new UTF8Encoding(false));
    }

    /// <summary>
    /// 构建字段映射矩阵 markdown：按 Category 分组，每字段列出 name/category/dataType/visibility/labelKey/description。
    /// </summary>
    public static string BuildFieldMatrixMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SDK 字段映射矩阵（req-107 B10）");
        sb.AppendLine();
        sb.AppendLine("> 自动生成自 `UsageFieldMetadataRegistry` + `UsageFieldsSchemaExporter`，供插件作者查阅 SDK 合法字段。");
        sb.AppendLine();
        sb.AppendLine("## 字段按类别分组");
        sb.AppendLine();
        sb.AppendLine("| 字段名 | Category | DataType | Visibility | LabelKey | 说明 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var category in Enum.GetValues<UsageFieldCategory>())
        {
            var fields = UsageFieldMetadataRegistry.All
                .Where(m => m.Category == category)
                .OrderBy(m => m.FieldName, StringComparer.OrdinalIgnoreCase);
            foreach (var m in fields)
            {
                sb.AppendLine($"| `{m.FieldName}` | {m.Category} | {m.DataType} | {m.Visibility} | `{m.LabelKey}` | {m.Description} |");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## ChartKindSpec（图表能力规格）");
        sb.AppendLine();
        sb.AppendLine("| Kind | SupportedSlicerModes | RequiredRoles | OptionalRoles | AllowedValueTypes | SupportsColorTiers |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var kind in Enum.GetValues<DeclarativeChartKind>())
        {
            var spec = ChartKindSpecRegistry.GetSpec(kind);
            if (spec == null) continue;
            sb.AppendLine($"| {kind} | {FormatList(spec.SupportedSlicerModes)} | {FormatList(spec.RequiredRoles)} | {FormatList(spec.OptionalRoles)} | {FormatList(spec.AllowedValueTypes)} | {spec.SupportsColorTiers} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Transformers（内置转换器）");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", Transformers.KnownNames.Select(n => $"`{n}`")));
        sb.AppendLine();
        return sb.ToString();
    }

    private static string FormatList<T>(IReadOnlyList<T> items) where T : Enum
        => items.Count == 0 ? "—" : string.Join(", ", items.Select(e => e.ToString()));

    private static string FormatList(IReadOnlyList<string> items)
        => items.Count == 0 ? "—" : string.Join(", ", items);
}