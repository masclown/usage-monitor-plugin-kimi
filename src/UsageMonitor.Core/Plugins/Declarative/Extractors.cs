using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageMonitor.Core.Plugins.Declarative;

/// <summary>
/// 提取上下文（req-107 B3）：承载待提取的网页内容（HTML 或 JSON）。
/// </summary>
public sealed class ExtractionContext
{
    /// <summary>网页原始内容（HTML 文档或 JSON 响应体）。</summary>
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// 提取器接口（req-107 B3）：把网页内容按提取指令转换为 SDK 字段字典。
/// <para>五种提取器：css / xpath / regex / jsonpath / table。Core 内置可执行 regex（纯文本）+ jsonpath（System.Text.Json 导航 XHR JSON）；
/// css / xpath / table 依赖 DOM 解析，由浏览器端（App/Playwright）实现，
/// 复杂插件（如 MiniMax）现阶段仍走 DLL 抓取。</para>
/// </summary>
public interface IFieldExtractor
{
    /// <summary>提取器工具名（css / xpath / regex / jsonpath / table）。</summary>
    string ToolName { get; }

    /// <summary>本提取器是否可在当前运行环境执行（Core 内无 DOM 解析器，css/xpath/table 返回 false）。</summary>
    bool CanExecuteInCore { get; }

    /// <summary>
    /// 执行属于本工具的一组提取指令，返回 SDK 字段字典（key = 标准字段名）。
    /// </summary>
    IReadOnlyDictionary<string, object?> Extract(IReadOnlyList<ExtractDirective> directives, ExtractionContext context);
}

/// <summary>
/// 正则提取器（req-107 B3，Core 内置可执行）。
/// <para>支持两种声明形态：① 每条指令一个正则 + 命名捕获组（组名 = 目标字段名）；
/// ② 每条指令一个正则取首个匹配整体。抽取值经 <see cref="Transformers"/> 转换后写入目标字段。</para>
/// </summary>
public sealed class RegexFieldExtractor : IFieldExtractor
{
    /// <inheritdoc />
    public string ToolName => "regex";

    /// <inheritdoc />
    public bool CanExecuteInCore => true;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Extract(IReadOnlyList<ExtractDirective> directives, ExtractionContext context)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var directive in directives)
        {
            if (string.IsNullOrEmpty(directive.Source)) continue;
            var match = Regex.Match(context.Content, directive.Source, RegexOptions.Singleline);
            if (!match.Success) continue;

            // 形态①：命名捕获组（组名 = 目标字段名），一次正则抽多个字段
            var named = match.Groups.Values.Where(g => !string.IsNullOrEmpty(g.Name) && g.Name != "0" && g.Success).ToList();
            if (named.Count > 0)
            {
                foreach (var group in named)
                {
                    var field = Models.UsageFields.MapToStandardFieldName(group.Name);
                    result[field] = Transformers.Apply(directive.Transform, group.Value);
                }
            }
            // 形态②：取首个匹配整体写入 TargetField
            else if (!string.IsNullOrEmpty(directive.TargetField))
            {
                var field = Models.UsageFields.MapToStandardFieldName(directive.TargetField);
                result[field] = Transformers.Apply(directive.Transform, match.Value);
            }
        }
        return result;
    }
}

/// <summary>
/// JSONPath 提取器（req-107 B3，Core 内置可执行）。
/// <para>用 System.Text.Json 导航 JSON 响应体（如 XHR 捕获的 JSON），支持
/// <c>$.a.b.c</c> 成员访问与 <c>$.items[0].c</c> 数组索引。抽取标量值经
/// <see cref="Transformers"/> 转换后写入目标 SDK 字段，覆盖“数据不在 DOM、在 XHR JSON”的场景。</para>
/// </summary>
public sealed class JsonPathFieldExtractor : IFieldExtractor
{
    /// <inheritdoc />
    public string ToolName => "jsonpath";

    /// <inheritdoc />
    public bool CanExecuteInCore => true;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Extract(IReadOnlyList<ExtractDirective> directives, ExtractionContext context)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(context.Content)) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(context.Content); }
        catch (JsonException) { return result; } // 内容非合法 JSON：容错跳过，交其他提取器 / DLL 兵底

        using (doc)
        {
            foreach (var directive in directives)
            {
                if (string.IsNullOrEmpty(directive.Source) || string.IsNullOrEmpty(directive.TargetField)) continue;
                if (!TryNavigate(doc.RootElement, directive.Source, out var element)) continue;
                var raw = ToRawString(element);
                if (raw == null) continue;
                var field = Models.UsageFields.MapToStandardFieldName(directive.TargetField);
                result[field] = Transformers.Apply(directive.Transform, raw);
            }
        }
        return result;
    }

    /// <summary>简化版 JSONPath 导航：<c>$</c> 根 + <c>.member</c> 成员 + <c>[index]</c> 数组索引 / <c>['prop']</c>。</summary>
    private static bool TryNavigate(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        var p = path.Trim();
        if (p.StartsWith("$")) p = p.Substring(1);
        var current = root;
        var i = 0;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '.') { i++; continue; }
            if (c == '[')
            {
                var end = p.IndexOf(']', i);
                if (end < 0) return false;
                var token = p.Substring(i + 1, end - i - 1).Trim().Trim('\'', '"');
                i = end + 1;
                if (int.TryParse(token, out var idx))
                {
                    if (current.ValueKind != JsonValueKind.Array || idx < 0 || idx >= current.GetArrayLength()) return false;
                    current = current[idx];
                }
                else if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(token, out current))
                {
                    return false;
                }
                continue;
            }
            var start = i;
            while (i < p.Length && p[i] != '.' && p[i] != '[') i++;
            var name = p.Substring(start, i - start);
            if (name.Length == 0) continue;
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return false;
        }
        value = current;
        return true;
    }

    /// <summary>标量 JsonElement 转字符串供 Transformers 处理；对象 / 数组不支持返回 null。</summary>
    private static string? ToRawString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}

/// <summary>
/// 提取器注册表 + 提取引擎（req-107 B3）。
/// <para>按 <see cref="ExtractDirective.Tool"/> 把指令分组分发到对应提取器；
/// Core 注册 regex + jsonpath（可执行），css / xpath / table 登记为已知工具但 Core 内不可执行
/// （需浏览器端 DOM 支持），其指令在 Core 内被跳过，由 DLL 抓取或 App 端提取器兵底。</para>
/// </summary>
public static class ExtractorRegistry
{
    /// <summary>已知工具名集合（含 Core 暂不可执行的 DOM 类）。</summary>
    public static readonly IReadOnlyList<string> KnownTools = new[] { "css", "xpath", "regex", "jsonpath", "table" };

    private static readonly Dictionary<string, IFieldExtractor> Executors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["regex"] = new RegexFieldExtractor(),
        ["jsonpath"] = new JsonPathFieldExtractor()
    };

    /// <summary>判断工具名是否为已知提取器（用于声明校验）。</summary>
    public static bool IsKnownTool(string? tool) => !string.IsNullOrEmpty(tool) && KnownTools.Contains(tool, StringComparer.OrdinalIgnoreCase);

    /// <summary>判断工具是否可在 Core 内执行（目前仅 regex）。</summary>
    public static bool CanExecute(string? tool) => !string.IsNullOrEmpty(tool) && Executors.ContainsKey(tool);

    /// <summary>
    /// 按提取清单执行全部可在 Core 内执行的指令，返回合并后的 SDK 字段字典。
    /// </summary>
    /// <param name="manifest">抓取声明清单。</param>
    /// <param name="context">提取上下文（网页内容）。</param>
    /// <returns>SDK 标准字段名 → 值 的字典。</returns>
    public static IReadOnlyDictionary<string, object?> Run(ExtractManifest manifest, ExtractionContext context)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        // 按工具分组，分发到可执行的提取器
        foreach (var group in manifest.Extract.Where(d => CanExecute(d.Tool)).GroupBy(d => d.Tool, StringComparer.OrdinalIgnoreCase))
        {
            var extractor = Executors[group.Key];
            foreach (var kv in extractor.Extract(group.ToList(), context))
            {
                result[kv.Key] = kv.Value;
            }
        }
        return result;
    }
}
