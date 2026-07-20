using System.Globalization;

namespace UsageMonitor.Plugin.Qoder;

/// <summary>
/// req-087-B3：Qoder 动态表格 7 字段解析。
/// 将表格行数据解析为结构化记录。
/// </summary>
public static class QoderTableParser
{
    /// <summary>
    /// 解析表格行数据。
    /// </summary>
    /// <param name="cells">7 个单元格文本数组</param>
    /// <returns>解析后的记录，失败返回 null</returns>
    public static QoderUsageRecord? ParseRow(string[] cells)
    {
        if (cells == null || cells.Length < 7)
        {
            return null;
        }

        var record = new QoderUsageRecord
        {
            Time = ParseTime(cells[0]),
            Source = cells[1]?.Trim() ?? string.Empty,
            Operation = cells[2]?.Trim() ?? string.Empty,
            ModelTier = cells[3]?.Trim() ?? string.Empty,
            RequestType = cells[4]?.Trim() ?? string.Empty,
            CreditsRaw = cells[5]?.Trim() ?? string.Empty,
            CostRaw = cells[6]?.Trim() ?? string.Empty
        };

        // 解析 Credits 斜杠字段
        var creditsResult = CreditsFieldParser.ParseSlashField(record.CreditsRaw);
        record.CreditsBefore = creditsResult.BeforeDiscount;
        record.CreditsAfter = creditsResult.AfterDiscount;
        record.CreditsParsed = creditsResult.IsSuccess;

        // 解析费用斜杠字段
        var costResult = CreditsFieldParser.ParseSlashField(record.CostRaw);
        record.CostBefore = costResult.BeforeDiscount;
        record.CostAfter = costResult.AfterDiscount;
        record.CostParsed = costResult.IsSuccess;

        return record;
    }

    /// <summary>
    /// 解析时间戳。
    /// </summary>
    private static DateTime ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateTime.MinValue;
        }

        var trimmed = text.Trim();

        // 尝试多种时间格式
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "MM-dd HH:mm:ss",
            "MM-dd HH:mm",
            "HH:mm:ss",
            "HH:mm"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                // 如果只有时间没有日期，补充今天日期
                if (format.StartsWith("HH"))
                {
                    return DateTime.Today.Add(result.TimeOfDay);
                }
                // 如果只有月-日没有年，补充今年
                if (format.StartsWith("MM"))
                {
                    return new DateTime(DateTime.Now.Year, result.Month, result.Day,
                        result.Hour, result.Minute, result.Second);
                }
                return result;
            }
        }

        // 尝试自然解析
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var natural))
        {
            return natural;
        }

        return DateTime.MinValue;
    }
}

/// <summary>
/// Qoder 用量记录（表格一行）。
/// </summary>
public class QoderUsageRecord
{
    /// <summary>时间（消耗发生时间）</summary>
    public DateTime Time { get; set; }

    /// <summary>来源（请求来源/接口）</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>操作（具体操作类型）</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>模型分级（模型等级分类）</summary>
    public string ModelTier { get; set; } = string.Empty;

    /// <summary>类型（请求类型）</summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>Credits 原始文本</summary>
    public string CreditsRaw { get; set; } = string.Empty;

    /// <summary>费用原始文本</summary>
    public string CostRaw { get; set; } = string.Empty;

    /// <summary>Credits 折前值</summary>
    public decimal CreditsBefore { get; set; }

    /// <summary>Credits 折后值（实际消耗）</summary>
    public decimal CreditsAfter { get; set; }

    /// <summary>Credits 是否成功解析</summary>
    public bool CreditsParsed { get; set; }

    /// <summary>费用折前值</summary>
    public decimal CostBefore { get; set; }

    /// <summary>费用折后值（实际扣费）</summary>
    public decimal CostAfter { get; set; }

    /// <summary>费用是否成功解析</summary>
    public bool CostParsed { get; set; }
}
