using System;
using System.Collections.Generic;
using System.Globalization;

namespace UsageMonitor.Core.Plugins.Declarative;

/// <summary>
/// 抓取声明内置转换器（req-107 B3）：把网页抽取的原始字符串转换为 SDK 字段值。
/// <para>主程序内置、插件按名引用（extract.json 的 transform 字段），插件零翻译。
/// 百分比统一归一为 0-100 数字（"53%" → 53），金额/次数存原始数字。</para>
/// </summary>
public static class Transformers
{
    private static readonly Dictionary<string, Func<string?, object?>> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parsePercent"] = ParsePercent,
        ["parseNumber"] = ParseNumber,
        ["parseDate"] = ParseDate,
        ["fromUnixMs"] = FromUnixMs,
        ["parseFormattedToken"] = ParseFormattedToken,
        ["trim"] = Trim,
        ["stripNonNumeric"] = StripNonNumeric,
        ["identity"] = Identity
    };

    /// <summary>
    /// 按转换器名转换原始值；未知转换器按原样返回（trim 后）。
    /// </summary>
    /// <param name="transform">转换器名（parsePercent / parseNumber / parseDate / trim / stripNonNumeric / identity）。</param>
    /// <param name="raw">网页抽取的原始字符串。</param>
    public static object? Apply(string? transform, string? raw)
    {
        if (string.IsNullOrEmpty(transform)) return raw?.Trim();
        return Map.TryGetValue(transform, out var fn) ? fn(raw) : raw?.Trim();
    }

    /// <summary>判断转换器名是否已内置。</summary>
    public static bool IsKnown(string? transform) => !string.IsNullOrEmpty(transform) && Map.ContainsKey(transform);

    /// <summary>已内置的转换器名集合（供校验 / 文档）。</summary>
    public static IReadOnlyCollection<string> KnownNames => Map.Keys;

    /// <summary>百分比归一："53%" / "0.53" / "53" → 53（0-100 数字）。</summary>
    private static object? ParsePercent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        bool hadPercentSign = s.Contains('%');
        s = StripToNumeric(s);
        if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return null;
        // 带 % 号或值 >1 视为已是百分数；否则视为 0-1 比例，×100 归一
        if (!hadPercentSign && v > 0 && v <= 1) v *= 100;
        return NormalizeNumber((decimal)v);
    }

    /// <summary>数值提取："5.54B" 不解析（需插件先还原原始数字）；"1,234" / "42" → 数字。</summary>
    private static object? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = StripToNumeric(raw.Trim());
        if (!decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return null;
        return NormalizeNumber(v);
    }

    /// <summary>日期解析为 UTC 字符串（ISO 8601）；解析失败返回原值。</summary>
    private static object? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt.ToString("o");
        return raw.Trim();
    }

    /// <summary>Unix 毫秒时间戳 → 本地 DateTime（req-088 Phase3；供 5h/周重置时刻，end_time 等）。非正数返回 null。</summary>
    private static object? FromUnixMs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = StripToNumeric(raw.Trim());
        if (!long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var ms) || ms <= 0) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
    }

    /// <summary>格式化 Token 字符串 → 原始 long（req-088 Phase3；"552.49M"/"5.85B"/"123K"/"12345"，K=1e3 M=1e6 B=1e9）。</summary>
    private static object? ParseFormattedToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        double mult = 1;
        var last = char.ToUpperInvariant(s[s.Length - 1]);
        if (last == 'K') { mult = 1_000d; s = s.Substring(0, s.Length - 1); }
        else if (last == 'M') { mult = 1_000_000d; s = s.Substring(0, s.Length - 1); }
        else if (last == 'B') { mult = 1_000_000_000d; s = s.Substring(0, s.Length - 1); }
        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return null;
        return (long)Math.Round(num * mult);
    }

    /// <summary>去除首尾空白。</summary>
    private static object? Trim(string? raw) => raw?.Trim();

    /// <summary>剔除非数字字符（保留负号与小数点）后解析为数字。</summary>
    private static object? StripNonNumeric(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = StripToNumeric(raw.Trim());
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? NormalizeNumber(v) : null;
    }

    /// <summary>原样返回（trim 后）。</summary>
    private static object? Identity(string? raw) => raw?.Trim();

    /// <summary>剔除除数字、负号、小数点外的字符。</summary>
    private static string StripToNumeric(string s)
    {
        var chars = new List<char>(s.Length);
        foreach (var c in s)
        {
            if (char.IsDigit(c) || c == '-' || c == '.') chars.Add(c);
        }
        return new string(chars.ToArray());
    }

    /// <summary>整数值的 decimal 归一为 long，避免 JSON 出现多余的 .0。</summary>
    private static object NormalizeNumber(decimal v) => v == Math.Truncate(v) ? (object)(long)v : v;
}
