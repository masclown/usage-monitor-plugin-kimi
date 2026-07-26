using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 插件文案解析器（req-116）：统一处理声明包里的 <c>i18n:</c> 前缀文案键。
/// <para>约定：manifest 字符串值写作 <c>"i18n:plugin.&lt;providerId&gt;.xxx"</c> 时视为 i18n key，
/// 经 <see cref="I18n.T"/> 按当前语言解析；无前缀的字面量原样返回——旧插件零改动兼容。</para>
/// <para><see cref="ResolveJson"/> 在 manifest 反序列化前做 JSON 文本级替换，
/// 使 configFields / errorGuidance / chart display 等全部消费点零侵入获得多语言能力；
/// 语言切换后由宿主触发插件重载即可重新解析。</para>
/// </summary>
public static class PluginTextResolver
{
    /// <summary>i18n key 前缀。</summary>
    public const string I18nPrefix = "i18n:";

    /// <summary>JSON 字符串值形态的 i18n key 匹配（键限定安全字符集，防止跨值误匹配）。</summary>
    private static readonly Regex I18nJsonValuePattern =
        new("\"i18n:([A-Za-z0-9_.\\-]{1,200})\"", RegexOptions.Compiled);

    /// <summary>
    /// 解析单个文案：<c>i18n:</c> 前缀 → I18n 按当前语言解析（缺键回退 key 本身）；否则原样返回。
    /// </summary>
    /// <param name="raw">原始文案（可空）。</param>
    public static string? Resolve(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return raw.StartsWith(I18nPrefix, StringComparison.Ordinal)
            ? I18n.T(raw.Substring(I18nPrefix.Length))
            : raw;
    }

    /// <summary>
    /// JSON 文本级解析：把所有 <c>"i18n:&lt;key&gt;"</c> 字符串值替换为当前语言译文
    /// （替换值经 <see cref="JsonSerializer.Serialize(string, JsonSerializerOptions?)"/> 转义，保证 JSON 合法性）。
    /// </summary>
    /// <param name="json">manifest 原始 JSON 文本。</param>
    public static string ResolveJson(string json)
    {
        if (string.IsNullOrEmpty(json) || !json.Contains(I18nPrefix, StringComparison.Ordinal)) return json;
        return I18nJsonValuePattern.Replace(json, m => JsonSerializer.Serialize(I18n.T(m.Groups[1].Value)));
    }

    /// <summary>
    /// 从 JSON 文本中提取全部 i18n key（供校验器检查语言包键完整性）。
    /// </summary>
    /// <param name="json">manifest 原始 JSON 文本。</param>
    public static System.Collections.Generic.List<string> ExtractKeys(string json)
    {
        var keys = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(json)) return keys;
        foreach (Match m in I18nJsonValuePattern.Matches(json))
            keys.Add(m.Groups[1].Value);
        return keys;
    }
}
