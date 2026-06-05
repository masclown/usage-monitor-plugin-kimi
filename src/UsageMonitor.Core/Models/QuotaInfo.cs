namespace UsageMonitor.Core.Models;

/// <summary>
/// 配额信息模型 - 表示AI服务的配额/套餐详情
/// </summary>
public class QuotaInfo
{
    /// <summary>配额名称（如 "Pro Plan"、"Free Tier"）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>配额类型（如 "monthly"、"yearly"、"lifetime"）</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>总额度</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>已用额度</summary>
    public decimal UsedAmount { get; set; }

    /// <summary>额度单位</summary>
    public string Unit { get; set; } = "USD";

    /// <summary>总Token配额（-1表示不限）</summary>
    public long TotalTokens { get; set; } = -1;

    /// <summary>已用Token数</summary>
    public long UsedTokens { get; set; }

    /// <summary>配额生效时间</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>配额过期时间</summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>是否处于有效期内</summary>
    public bool IsActive =>
        (!StartDate.HasValue || DateTime.Now >= StartDate.Value) &&
        (!ExpireDate.HasValue || DateTime.Now <= ExpireDate.Value);

    /// <summary>扩展数据</summary>
    public Dictionary<string, object> Extra { get; set; } = new();
}
