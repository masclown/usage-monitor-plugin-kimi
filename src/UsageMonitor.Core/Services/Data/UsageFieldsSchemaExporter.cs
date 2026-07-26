using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;

namespace UsageMonitor.Core.Services.Data;

/// <summary>
/// SDK 字段 Schema 导出器（req-107 B10）：导出当前 SDK 全部合法字段 + ChartKindSpec + 转换器，
/// 生成 <c>docs/usage-fields-schema.json</c>，供插件作者在 VSCode 编辑 defaults.json / extract.json 时自动补全与校验。
/// </summary>
public static class UsageFieldsSchemaExporter
{
    /// <summary>
    /// 导出 SDK 字段 Schema 为 JSON 文本（含字段白名单/元数据、图表能力规格、转换器名）。
    /// </summary>
    /// <param name="sdkVersion">当前 SDK 版本字符串。</param>
    public static string Export(string sdkVersion)
    {
        var fields = UsageFieldMetadataRegistry.All
            .OrderBy(m => m.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(m => new Dictionary<string, object?>
            {
                ["name"] = m.FieldName,
                ["category"] = m.Category.ToString(),
                ["visibility"] = m.Visibility.ToString(),
                ["dataType"] = m.DataType.ToString(),
                ["labelKey"] = m.LabelKey,
                ["description"] = m.Description
            })
            .ToList();

        var chartKinds = Enum.GetValues<DeclarativeChartKind>()
            .Select(k => ChartKindSpecRegistry.GetSpec(k))
            .Where(s => s != null)
            .Select(s => new Dictionary<string, object?>
            {
                ["kind"] = s!.Kind.ToString(),
                ["supportedSlicerModes"] = s.SupportedSlicerModes.Select(m => m.ToString()).ToArray(),
                ["requiredRoles"] = s.RequiredRoles.Select(r => r.ToString()).ToArray(),
                ["optionalRoles"] = s.OptionalRoles.Select(r => r.ToString()).ToArray(),
                ["allowedValueTypes"] = s.AllowedValueTypes.Select(t => t.ToString()).ToArray(),
                ["supportsColorTiers"] = s.SupportsColorTiers
            })
            .ToList();

        var schema = new Dictionary<string, object?>
        {
            ["sdkVersion"] = sdkVersion,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["fields"] = fields,
            ["chartKinds"] = chartKinds,
            ["transformers"] = Transformers.KnownNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
            // req-116：稳定错误码清单（供 errorGuidance.matchCodes 声明时自动补全/校对）
            ["errorCodes"] = new[]
            {
                UsageMonitor.Core.Models.UsageErrorCodes.CredentialMissing,
                UsageMonitor.Core.Models.UsageErrorCodes.AuthInvalid,
                UsageMonitor.Core.Models.UsageErrorCodes.NetworkError,
                UsageMonitor.Core.Models.UsageErrorCodes.Timeout,
                UsageMonitor.Core.Models.UsageErrorCodes.Cancelled,
                UsageMonitor.Core.Models.UsageErrorCodes.DataEmpty,
                UsageMonitor.Core.Models.UsageErrorCodes.ConfigMissing
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(schema, options);
    }
}
