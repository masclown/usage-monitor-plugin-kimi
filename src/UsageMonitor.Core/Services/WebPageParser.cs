using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-086：网页内容提取器，支持 CSS Selector / XPath / Regex 三种模式。
/// <para>
/// 为网页插件提供统一的页面数据提取能力，子类插件只需声明提取规则即可。
/// </para>
/// </summary>
public static class WebPageParser
{
    /// <summary>req-067-002：数字清洗正则（去千分位/空白/百分号/单位后缀）提为 static readonly + Compiled。</summary>
    private static readonly Regex _numberCleanRegex =
        new(@"[,\s%次个条]", RegexOptions.Compiled);

    /// <summary>提取模式</summary>
    public enum ExtractMode
    {
        /// <summary>CSS Selector（如 ".usage-value"）</summary>
        CssSelector,
        /// <summary>XPath（如 "//div[@class='usage']/span"）</summary>
        XPath,
        /// <summary>正则表达式（从页面文本中提取）</summary>
        Regex,
    }

    /// <summary>
    /// 从页面提取文本内容。
    /// </summary>
    /// <param name="page">Playwright IPage 实例</param>
    /// <param name="pattern">提取模式（CSS Selector / XPath / Regex）</param>
    /// <param name="mode">提取模式类型</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提取的文本，未找到返回 null</returns>
    public static async Task<string?> ExtractAsync(
        IPage page,
        string pattern,
        ExtractMode mode,
        CancellationToken ct = default)
    {
        try
        {
            return mode switch
            {
                ExtractMode.CssSelector => await ExtractByCssAsync(page, pattern),
                ExtractMode.XPath => await ExtractByXPathAsync(page, pattern),
                ExtractMode.Regex => await ExtractByRegexAsync(page, pattern),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }
        catch (Exception ex)
        {
            FileLogger.Warn("WebPageParser", $"提取失败 [{mode}] {pattern}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从页面提取数值（自动解析数字）。
    /// </summary>
    /// <param name="page">Playwright IPage 实例</param>
    /// <param name="pattern">提取模式</param>
    /// <param name="mode">提取模式类型</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>解析后的数值，失败返回 null</returns>
    public static async Task<decimal?> ExtractNumberAsync(
        IPage page,
        string pattern,
        ExtractMode mode,
        CancellationToken ct = default)
    {
        var text = await ExtractAsync(page, pattern, mode, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 移除千分位逗号、百分号、单位后缀
        var cleaned = _numberCleanRegex.Replace(text, "");
        if (decimal.TryParse(cleaned, out var value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// 从页面提取百分比（自动解析 "66%" → 66.0）。
    /// </summary>
    public static async Task<double?> ExtractPercentAsync(
        IPage page,
        string pattern,
        ExtractMode mode,
        CancellationToken ct = default)
    {
        var text = await ExtractAsync(page, pattern, mode, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim().TrimEnd('%');
        if (double.TryParse(trimmed, out var value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// 批量提取多个字段。
    /// </summary>
    /// <param name="page">Playwright IPage 实例</param>
    /// <param name="rules">提取规则字典（key = 字段名, value = (pattern, mode)）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提取结果字典</returns>
    public static async Task<Dictionary<string, string?>> ExtractBatchAsync(
        IPage page,
        Dictionary<string, (string pattern, ExtractMode mode)> rules,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, string?>();
        foreach (var (key, (pattern, mode)) in rules)
        {
            results[key] = await ExtractAsync(page, pattern, mode, ct);
        }
        return results;
    }

    // ============== 私有实现 ==============

    private static async Task<string?> ExtractByCssAsync(IPage page, string selector)
    {
        var element = await page.QuerySelectorAsync(selector);
        if (element == null) return null;
        return await element.InnerTextAsync();
    }

    private static async Task<string?> ExtractByXPathAsync(IPage page, string xpath)
    {
        var element = await page.QuerySelectorAsync($"xpath={xpath}");
        if (element == null) return null;
        return await element.InnerTextAsync();
    }

    private static async Task<string?> ExtractByRegexAsync(IPage page, string pattern)
    {
        var content = await page.ContentAsync();
        var match = Regex.Match(content, pattern, RegexOptions.Singleline);
        if (!match.Success) return null;
        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }
}
