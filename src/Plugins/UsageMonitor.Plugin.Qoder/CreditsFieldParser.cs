using System.Globalization;

namespace UsageMonitor.Plugin.Qoder;

/// <summary>
/// req-087-B4：斜杠字段解析工具。
/// 将 "205.72/4.11" 格式拆分为折前/折后两个数值。
/// </summary>
public static class CreditsFieldParser
{
    /// <summary>
    /// 解析斜杠分隔的字段。
    /// </summary>
    /// <param name="text">原始文本，如 "205.72/4.11" 或 "100" 或 "abc/xyz"</param>
    /// <returns>解析结果，包含折前值、折后值、是否成功解析</returns>
    public static SlashParseResult ParseSlashField(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SlashParseResult(0, 0, false, text);
        }

        var trimmed = text.Trim();

        // 无斜杠：尝试直接解析为数字
        if (!trimmed.Contains('/'))
        {
            if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var singleValue))
            {
                return new SlashParseResult(singleValue, singleValue, true, text);
            }
            return new SlashParseResult(0, 0, false, text);
        }

        // 有斜杠：拆分折前/折后
        var parts = trimmed.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return new SlashParseResult(0, 0, false, text);
        }

        var beforeOk = decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var before);
        var afterOk = decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var after);

        if (beforeOk && afterOk)
        {
            return new SlashParseResult(before, after, true, text);
        }

        // 部分解析成功也算失败（语义不明确）
        return new SlashParseResult(0, 0, false, text);
    }

    /// <summary>
    /// 获取折后值（实际扣费）。
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>折后值，解析失败返回 0</returns>
    public static decimal GetAfterDiscountValue(string? text)
    {
        var result = ParseSlashField(text);
        return result.IsSuccess ? result.AfterDiscount : 0;
    }
}

/// <summary>
/// 斜杠字段解析结果。
/// </summary>
/// <param name="BeforeDiscount">折前值（"/" 前面的数字）</param>
/// <param name="AfterDiscount">折后值（"/" 后面的数字，即实际扣费）</param>
/// <param name="IsSuccess">是否成功解析</param>
/// <param name="OriginalText">原始文本</param>
public record SlashParseResult(decimal BeforeDiscount, decimal AfterDiscount, bool IsSuccess, string? OriginalText);
