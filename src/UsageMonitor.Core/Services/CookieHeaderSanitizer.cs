using System.Text.RegularExpressions;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-065 B6：Cookie HTTP头注入防护工具类。
/// 清理Cookie字符串中的控制字符（CR/LF/TAB等），防止HTTP Header Injection攻击。
/// </summary>
public static class CookieHeaderSanitizer
{
    /// <summary>
    /// 匹配所有控制字符：\r\n\t\x00-\x1F
    /// </summary>
    private static readonly Regex ControlChars = new(@"[\r\n\t\x00-\x1F]", RegexOptions.Compiled);

    /// <summary>
    /// 清理Cookie字符串，移除所有控制字符。
    /// </summary>
    /// <param name="cookie">原始Cookie字符串</param>
    /// <returns>清理后的安全Cookie字符串；若输入为空则返回空字符串</returns>
    public static string Sanitize(string? cookie)
    {
        if (string.IsNullOrEmpty(cookie)) return string.Empty;
        return ControlChars.Replace(cookie, "");
    }
}
