namespace UsageMonitor.Core.Models;

/// <summary>
/// 用量信息模型 - 统一的AI服务用量数据结构
/// </summary>
public class UsageInfo
{
    /// <summary>服务商唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>服务商显示名称</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>已用金额/额度</summary>
    public decimal UsedAmount { get; set; }

    /// <summary>总金额/额度</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>金额单位（如 USD、CNY）</summary>
    public string Unit { get; set; } = "USD";

    /// <summary>已用Token数</summary>
    public long UsedTokens { get; set; }

    /// <summary>总Token数（-1表示不限制或未知）</summary>
    public long TotalTokens { get; set; } = -1;

    /// <summary>过期时间（null表示永不过期）</summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>扩展数据（用于存放插件特有的额外信息）</summary>
    public Dictionary<string, object> Extra { get; set; } = new();

    /// <summary>最后更新时间</summary>
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>是否查询成功</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>错误信息（查询失败时）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 获取金额使用百分比
    /// </summary>
    public double GetUsagePercentage()
    {
        if (TotalAmount <= 0) return 0;
        return Math.Min(100, (double)(UsedAmount / TotalAmount * 100));
    }

    /// <summary>
    /// 获取剩余额度
    /// </summary>
    public decimal GetRemainingAmount()
    {
        return Math.Max(0, TotalAmount - UsedAmount);
    }

    /// <summary>
    /// 获取剩余Token数
    /// </summary>
    public long GetRemainingTokens()
    {
        if (TotalTokens < 0) return -1;
        return Math.Max(0, TotalTokens - UsedTokens);
    }

    /// <summary>
    /// 获取用于任务栏显示的简短文本
    /// </summary>
    public string GetShortDisplayText()
    {
        if (!IsSuccess) return $"{ProviderName}: 错误";

        if (TotalAmount > 0)
            return $"{ProviderName}: {GetRemainingAmount():F2} {Unit}";

        if (UsedTokens > 0)
            return $"{ProviderName}: {FormatTokenCount(UsedTokens)} tokens";

        return $"{ProviderName}: --";
    }

    /// <summary>
    /// 格式化Token数量显示（如 1500000 -> 1.5M）
    /// </summary>
    private static string FormatTokenCount(long count)
    {
        if (count >= 1_000_000_000)
            return $"{count / 1_000_000_000.0:F1}B";
        if (count >= 1_000_000)
            return $"{count / 1_000_000.0:F1}M";
        if (count >= 1_000)
            return $"{count / 1_000.0:F1}K";
        return count.ToString();
    }

    /// <summary>
    /// 创建一个错误状态的UsageInfo
    /// </summary>
    public static UsageInfo CreateError(string providerId, string providerName, string errorMessage)
    {
        return new UsageInfo
        {
            ProviderId = providerId,
            ProviderName = providerName,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            LastUpdated = DateTime.Now
        };
    }
}
