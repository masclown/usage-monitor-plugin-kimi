namespace UsageMonitor.Core.Models;

/// <summary>
/// SDK 标准字段名常量定义 - 插件上报数据时使用的标准字段名
/// <para>req-092：用量数据差异持久化，SDK 字段映射标准化。</para>
/// <para>插件通过 <see cref="IUsageProvider.MapToStandardFields"/> 将原始数据映射为标准字段名，
/// 差异检测引擎按标准字段名进行字段级对比，仅保存有变化的字段。</para>
/// </summary>
public static class UsageFields
{
    // ===================== 通用字段 =====================

    /// <summary>已用百分比（0-100）</summary>
    public const string UsedPercent = "used_percent";

    /// <summary>已用金额/额度</summary>
    public const string UsedAmount = "used_amount";

    /// <summary>总金额/额度</summary>
    public const string TotalAmount = "total_amount";

    /// <summary>已用 Token 数</summary>
    public const string UsedTokens = "used_tokens";

    /// <summary>总 Token 数（-1 表示不限制或未知）</summary>
    public const string TotalTokens = "total_tokens";

    /// <summary>金额单位（如 USD、CNY）</summary>
    public const string Unit = "unit";

    /// <summary>过期时间（ISO 8601 格式字符串）</summary>
    public const string ExpireDate = "expire_date";

    /// <summary>最后更新时间（ISO 8601 格式字符串）</summary>
    public const string LastUpdated = "last_updated";

    /// <summary>是否查询成功</summary>
    public const string IsSuccess = "is_success";

    /// <summary>错误信息</summary>
    public const string ErrorMessage = "error_message";

    // ===================== MiniMax 特有字段 =====================

    /// <summary>MiniMax 5小时额度已用百分比</summary>
    public const string Mm5hUsedPercent = "mm_5h_used_percent";

    /// <summary>MiniMax 周额度已用百分比</summary>
    public const string MmWeeklyUsedPercent = "mm_weekly_used_percent";

    /// <summary>MiniMax 剩余额度（Credits）</summary>
    public const string MmRemainingCredits = "mm_remaining_credits";

    /// <summary>MiniMax 订阅标题</summary>
    public const string MmSubscriptionTitle = "mm_subscription_title";

    /// <summary>MiniMax 订阅是否激活</summary>
    public const string MmSubscriptionActive = "mm_subscription_active";

    // ===================== DeepSeek 特有字段 =====================

    /// <summary>DeepSeek 消费金额</summary>
    public const string DsSpendAmount = "ds_spend_amount";

    /// <summary>DeepSeek 请求次数</summary>
    public const string DsRequestCount = "ds_request_count";

    // ===================== 扩展字段前缀 =====================

    /// <summary>插件自定义扩展字段前缀（extras 字典中的字段）</summary>
    public const string ExtraPrefix = "extra_";

    /// <summary>
    /// 获取所有标准字段名（用于验证和调试）
    /// </summary>
    public static IReadOnlyList<string> AllStandardFields => new[]
    {
        UsedPercent, UsedAmount, TotalAmount, UsedTokens, TotalTokens,
        Unit, ExpireDate, LastUpdated, IsSuccess, ErrorMessage,
        Mm5hUsedPercent, MmWeeklyUsedPercent, MmRemainingCredits,
        MmSubscriptionTitle, MmSubscriptionActive,
        DsSpendAmount, DsRequestCount
    };

    /// <summary>
    /// 判断字段名是否为标准字段
    /// </summary>
    public static bool IsStandardField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return false;
        return AllStandardFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 将插件原始字段名映射为标准字段名（如果已存在映射）
    /// </summary>
    public static string MapToStandardFieldName(string rawFieldName)
    {
        if (string.IsNullOrWhiteSpace(rawFieldName)) return rawFieldName;

        // 常见映射规则
        return rawFieldName.ToLowerInvariant() switch
        {
            "usedpercent" or "usagepercent" => UsedPercent,
            "usedamount" => UsedAmount,
            "totalamount" => TotalAmount,
            "usedtokens" => UsedTokens,
            "totaltokens" => TotalTokens,
            "unit" => Unit,
            "expiredate" => ExpireDate,
            "lastupdated" => LastUpdated,
            "issuccess" => IsSuccess,
            "errormessage" => ErrorMessage,
            "mm_5husedpercent" or "mm5husedpercent" => Mm5hUsedPercent,
            "mm_weeklyusedpercent" or "mmweeklyusedpercent" => MmWeeklyUsedPercent,
            "mm_remainingcredits" or "mmremainingcredits" => MmRemainingCredits,
            "mm_subscriptiontitle" or "mmsubscriptiontitle" => MmSubscriptionTitle,
            "mm_subscriptionactive" or "mmsubscriptionactive" => MmSubscriptionActive,
            "ds_spendamount" or "dsspendamount" => DsSpendAmount,
            "ds_requestcount" or "dsrequestcount" => DsRequestCount,
            _ => rawFieldName // 未匹配时保持原样
        };
    }
}
