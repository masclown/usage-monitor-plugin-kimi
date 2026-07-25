using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins.Declarative;

/// <summary>req-088 Phase3：声明式抓取结果——产出的 extras 字典 + 解析到的账号稳定身份 ID（供哈希 account_id）。</summary>
public sealed record CaptureResult(IReadOnlyDictionary<string, object> Extras, string? StableId);

/// <summary>
/// req-088 Phase3：声明式取数执行器。把 <see cref="FetchDeclaration"/> 施加到"已捕获的接口 JSON + DOM 结果"上，
/// 产出与旧手写提取器等价的 extras 字典，实现"新 Provider 只写声明、不写抓取代码"。
/// <para>支持：① 标量字段（jsonpath → extras 键 + 转换器）；② 对象数组展开（含二级嵌套 + 父项字段继承 → List&lt;Dictionary&gt;）；
/// ③ 并行强类型列表（date/value 对齐，产出 List&lt;long&gt;/List&lt;double&gt;/List&lt;string&gt;）；④ 账号稳定身份提取。</para>
/// </summary>
public static class DeclarativeCaptureExecutor
{
    /// <summary>
    /// 执行取数声明。
    /// </summary>
    /// <param name="decl">取数声明（来自 defaults.json 的 fetch 节）。</param>
    /// <param name="captured">已捕获的接口响应（key=响应 URL，value=JSON 文本）。</param>
    /// <param name="domResults">DOM 兜底结果（key=声明的 Target，value=已抓取文本）；可空。</param>
    /// <returns>extras 字典 + 账号稳定身份 ID。</returns>
    public static CaptureResult Execute(
        FetchDeclaration? decl,
        IReadOnlyDictionary<string, string> captured,
        IReadOnlyDictionary<string, string>? domResults = null)
    {
        var extras = new Dictionary<string, object>();
        if (decl == null || captured == null) return new CaptureResult(extras, null);

        // 1. 接口标量 + 数组
        foreach (var ep in decl.Endpoints)
        {
            var body = FindCaptured(captured, ep.UrlMatch);
            if (body == null) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                foreach (var f in ep.Fields)
                {
                    if (!TryNavigate(root, f.Path, out var el)) continue;
                    var raw = ToRaw(el);
                    if (raw == null) continue;
                    var val = Transformers.Apply(f.Transform, raw);
                    if (val != null) extras[f.Target] = val;
                }
                foreach (var arr in ep.Arrays)
                    ExpandArray(root, arr, extras);
                foreach (var find in ep.Finds)
                    ExecuteFind(root, find, extras);
            }
        }

        // 1b. 聚合（跨数组，如 token 加权平均缓存命中率）
        foreach (var agg in decl.Aggregates)
            ExecuteAggregate(agg, captured, extras);

        // 2. DOM 兜底（由浏览器端抓好文本后按 Target 传入）
        if (domResults != null)
        {
            foreach (var d in decl.Dom)
            {
                if (!domResults.TryGetValue(d.Target, out var text) || string.IsNullOrEmpty(text)) continue;
                var val = Transformers.Apply(d.Transform, text);
                if (val != null) extras[d.Target] = val;
            }
        }

        // 2b. 计算列（基于已产出的 extras，如视频 used = total - remains）
        foreach (var comp in decl.Computed)
            ExecuteComputed(comp, extras);

        // 3. 账号稳定身份（默认组优先由 jsonpath 指定 [0]；执行器额外尝试 is_default）
        string? stableId = ResolveStableId(decl.AccountId, captured);

        return new CaptureResult(extras, stableId);
    }

    /// <summary>在数组里找 MatchField==MatchValue 的首项，从中提取 Fields 到 extras（如 model_remains 的 general/video）。</summary>
    private static void ExecuteFind(JsonElement root, FetchArrayFind find, Dictionary<string, object> extras)
    {
        if (!TryNavigate(root, find.ItemsPath, out var items) || items.ValueKind != JsonValueKind.Array) return;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty(find.MatchField, out var mv)) continue;
            if (!string.Equals(ToRaw(mv), find.MatchValue, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var f in find.Fields)
            {
                if (!TryNavigate(item, f.Path, out var el)) continue;
                var raw = ToRaw(el);
                if (raw == null) continue;
                var val = Transformers.Apply(f.Transform, raw);
                if (val != null) extras[f.Target] = val;
            }
            break; // 命中首个即止
        }
    }

    /// <summary>对数组按 token 加权平均（或等权）聚合一个标量写入 extras（当前仅 weightedAvg）。</summary>
    private static void ExecuteAggregate(FetchAggregate agg, IReadOnlyDictionary<string, string> captured, Dictionary<string, object> extras)
    {
        if (!string.Equals(agg.Op, "weightedAvg", StringComparison.OrdinalIgnoreCase)) return;
        var body = FindCaptured(captured, agg.UrlMatch);
        if (body == null) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!TryNavigate(doc.RootElement, agg.ItemsPath, out var items) || items.ValueKind != JsonValueKind.Array) return;
            double num = 0, den = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (!TryNavigate(item, agg.ValuePath, out var vEl)) continue;
                var vRaw = ToRaw(vEl);
                if (vRaw == null) continue;
                var vObj = Transformers.Apply(agg.ValueTransform, vRaw);
                if (vObj == null) continue;
                double w = 1;
                if (!string.IsNullOrEmpty(agg.WeightPath) && TryNavigate(item, agg.WeightPath!, out var wEl))
                {
                    var wRaw = ToRaw(wEl);
                    if (wRaw != null && double.TryParse(wRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var wp)) w = wp;
                }
                if (w <= 0) continue;
                double v;
                try { v = Convert.ToDouble(vObj, CultureInfo.InvariantCulture); } catch { continue; }
                num += v * w; den += w;
            }
            if (den > 0) extras[agg.Target] = num / den;
        }
        catch (JsonException) { }
    }

    /// <summary>基于已有 extras 键派生新键（subtract / splitBefore / splitAfter / constant / coalesce / template；见 <see cref="FetchComputed"/> 语义）。</summary>
    private static void ExecuteComputed(FetchComputed comp, Dictionary<string, object> extras)
    {
        // 统一条件门：WhenPresent 键缺失则跳过；OnlyIfAbsent 且 Target 已存在则跳过。
        if (!string.IsNullOrEmpty(comp.WhenPresent) && !extras.ContainsKey(comp.WhenPresent!)) return;
        if (comp.OnlyIfAbsent && extras.ContainsKey(comp.Target)) return;

        switch (comp.Op?.ToLowerInvariant())
        {
            case "subtract":
                ExecuteSubtract(comp, extras);
                break;
            case "splitbefore":
            case "splitafter":
                ExecuteSplit(comp, extras);
                break;
            case "constant":
                ExecuteConstant(comp, extras);
                break;
            case "coalesce":
                foreach (var key in comp.Operands)
                {
                    if (extras.TryGetValue(key, out var v) && v != null) { extras[comp.Target] = v; break; }
                }
                break;
            case "template":
                ExecuteTemplate(comp, extras);
                break;
        }
    }

    /// <summary>subtract 算子：operands[0] - operands[1] - ...；均为整数时产出 long。</summary>
    private static void ExecuteSubtract(FetchComputed comp, Dictionary<string, object> extras)
    {
        if (comp.Operands.Count == 0) return;
        if (!extras.TryGetValue(comp.Operands[0], out var first) || first == null) return;
        double acc;
        try { acc = Convert.ToDouble(first, CultureInfo.InvariantCulture); } catch { return; }
        var allIntegral = IsIntegral(first);
        for (var i = 1; i < comp.Operands.Count; i++)
        {
            if (!extras.TryGetValue(comp.Operands[i], out var op) || op == null) continue;
            try { acc -= Convert.ToDouble(op, CultureInfo.InvariantCulture); allIntegral &= IsIntegral(op); } catch { }
        }
        extras[comp.Target] = allIntegral ? (object)(long)acc : acc;
    }

    /// <summary>splitBefore/splitAfter 算子：按首个分隔符拆分字符串；无分隔符时 before 不产出、after 产出整串（trim）。</summary>
    private static void ExecuteSplit(FetchComputed comp, Dictionary<string, object> extras)
    {
        if (comp.Operands.Count == 0 || string.IsNullOrEmpty(comp.Separators)) return;
        if (!extras.TryGetValue(comp.Operands[0], out var src) || src is not string raw || string.IsNullOrWhiteSpace(raw)) return;
        var sepIdx = raw.IndexOfAny(comp.Separators!.ToCharArray());
        var isBefore = string.Equals(comp.Op, "splitBefore", StringComparison.OrdinalIgnoreCase);
        if (sepIdx > 0 && sepIdx < raw.Length - 1)
        {
            var part = isBefore ? raw.Substring(0, sepIdx) : raw.Substring(sepIdx + 1);
            extras[comp.Target] = part.Trim();
        }
        else if (!isBefore)
        {
            extras[comp.Target] = raw.Trim();
        }
    }

    /// <summary>constant 算子：把声明字面量（字符串/数值/布尔）写入 Target。</summary>
    private static void ExecuteConstant(FetchComputed comp, Dictionary<string, object> extras)
    {
        if (comp.Value == null) return;
        var el = comp.Value.Value;
        object? val = el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
        if (val != null) extras[comp.Target] = val;
    }

    /// <summary>template 算子：把 <c>{键名}</c> 占位符替换为 extras 值（缺失补空串）后写入 Target。</summary>
    private static void ExecuteTemplate(FetchComputed comp, Dictionary<string, object> extras)
    {
        if (string.IsNullOrEmpty(comp.Template)) return;
        var text = System.Text.RegularExpressions.Regex.Replace(comp.Template!, @"\{([A-Za-z0-9_]+)\}",
            m => extras.TryGetValue(m.Groups[1].Value, out var v) && v != null
                ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty);
        extras[comp.Target] = text;
    }

    /// <summary>判断值是否为整型（供计算列结果类型推断）。</summary>
    private static bool IsIntegral(object o) => o is long or int or short or byte;

    /// <summary>展开一个数组声明到 extras（objects=字典列表；parallel=多条对齐的强类型列表）。</summary>
    private static void ExpandArray(JsonElement root, FetchArray arr, Dictionary<string, object> extras)
    {
        if (!TryNavigate(root, arr.ItemsPath, out var items) || items.ValueKind != JsonValueKind.Array) return;

        if (string.Equals(arr.Mode, "parallel", StringComparison.OrdinalIgnoreCase))
        {
            // 每个 ItemField 一条并行列表；逐项追加，保证跨列表按索引对齐。
            var lists = new Dictionary<string, System.Collections.IList>();
            foreach (var f in arr.ItemFields)
                lists[f.Target] = MakeTypedList(f.ElementType);
            foreach (var item in items.EnumerateArray())
            {
                foreach (var f in arr.ItemFields)
                {
                    object? cell = null;
                    if (TryNavigate(item, f.Path, out var el))
                    {
                        var raw = ToRaw(el);
                        if (raw != null) cell = Transformers.Apply(f.Transform, raw);
                    }
                    lists[f.Target].Add(CoerceElement(cell, f.ElementType));
                }
            }
            foreach (var f in arr.ItemFields)
                if (lists[f.Target].Count > 0) extras[f.Target] = lists[f.Target];
            return;
        }

        // objects 模式：字典列表（支持二级嵌套 + 父项字段继承）。
        var rows = new List<Dictionary<string, object>>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (string.IsNullOrEmpty(arr.NestedItems))
            {
                var row = MapItem(item, arr.ItemFields, null, arr.InheritFromParent);
                if (row.Count > 0) rows.Add(row);
            }
            else if (item.TryGetProperty(arr.NestedItems, out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in nested.EnumerateArray())
                {
                    if (child.ValueKind != JsonValueKind.Object) continue;
                    var row = MapItem(child, arr.ItemFields, item, arr.InheritFromParent);
                    if (row.Count > 0) rows.Add(row);
                }
            }
        }
        if (rows.Count > 0) extras[arr.Target] = rows;
    }

    /// <summary>把一项（对象数组模式）映射为字典，含从父项继承的成员。</summary>
    private static Dictionary<string, object> MapItem(
        JsonElement item, IReadOnlyList<FetchField> fields, JsonElement? parent,
        IReadOnlyDictionary<string, string> inherit)
    {
        var row = new Dictionary<string, object>();
        foreach (var f in fields)
        {
            if (!TryNavigate(item, f.Path, out var el)) continue;
            var raw = ToRaw(el);
            if (raw == null) continue;
            var val = Transformers.Apply(f.Transform, raw);
            if (val != null) row[f.Target] = val;
        }
        if (parent.HasValue && inherit != null)
        {
            foreach (var kv in inherit)
            {
                if (parent.Value.TryGetProperty(kv.Key, out var pv))
                {
                    var raw = ToRaw(pv);
                    if (raw != null) row[kv.Value] = raw;
                }
            }
        }
        return row;
    }

    /// <summary>解析账号稳定身份：按 UrlMatch 找响应，导航 Path；若为 groups[] 且存在 is_default 组则优先默认组。</summary>
    private static string? ResolveStableId(FetchAccountId? acc, IReadOnlyDictionary<string, string> captured)
    {
        if (acc == null || string.IsNullOrEmpty(acc.UrlMatch)) return null;
        var body = FindCaptured(captured, acc.UrlMatch);
        if (body == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // 优先默认组：groups[] 里 is_default=true 的 group_id
            if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                string? firstId = null;
                foreach (var g in groups.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.Object) continue;
                    var gid = g.TryGetProperty("group_id", out var giv) && giv.ValueKind == JsonValueKind.String ? giv.GetString() : null;
                    if (string.IsNullOrWhiteSpace(gid)) continue;
                    var isDefault = g.TryGetProperty("is_default", out var idf) && idf.ValueKind == JsonValueKind.True;
                    if (isDefault) return gid;
                    firstId ??= gid;
                }
                if (firstId != null) return firstId;
            }
            if (TryNavigate(root, acc.Path, out var el)) return ToRaw(el);
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>按 URL 子串在已捕获响应里找 JSON 文本。</summary>
    private static string? FindCaptured(IReadOnlyDictionary<string, string> captured, string urlMatch)
    {
        if (string.IsNullOrEmpty(urlMatch)) return null;
        foreach (var kv in captured)
            if (kv.Key.IndexOf(urlMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                return kv.Value;
        return null;
    }

    /// <summary>创建强类型并行列表。</summary>
    private static System.Collections.IList MakeTypedList(string? elementType) => (elementType?.ToLowerInvariant()) switch
    {
        "long" => new List<long>(),
        "double" => new List<double>(),
        _ => new List<string>()
    };

    /// <summary>把转换后的值强制为并行列表的元素类型（缺失时用类型默认值，保持索引对齐）。</summary>
    private static object CoerceElement(object? val, string? elementType) => (elementType?.ToLowerInvariant()) switch
    {
        "long" => val == null ? 0L : Convert.ToInt64(val, CultureInfo.InvariantCulture),
        "double" => val == null ? 0d : Convert.ToDouble(val, CultureInfo.InvariantCulture),
        _ => val?.ToString() ?? string.Empty
    };

    /// <summary>标量 JsonElement 转字符串（供 Transformers 处理）；对象/数组返回 null。</summary>
    private static string? ToRaw(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    /// <summary>简化版 JSONPath 导航：<c>$</c> 根 + <c>.member</c> 成员 + <c>[index]</c> 数组索引 / <c>['prop']</c>。</summary>
    private static bool TryNavigate(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrEmpty(path)) return false;
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
}
