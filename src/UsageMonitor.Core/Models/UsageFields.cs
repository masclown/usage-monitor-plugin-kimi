namespace UsageMonitor.Core.Models;

/// <summary>
/// SDK 标准字段名常量定义 - 插件上报数据时使用的标准字段名（req-107 B1 统一字段体系）。
/// <para>设计原则（见 <c>docs/sdk-unified-fields.md</c>）：
/// ① 去 Provider 前缀——同语义字段跨 Provider 共用一个名字（如 <see cref="FiveHourUsedPercent"/>，而非带 Provider 私有前缀的名字）；
/// ② 字段名 = 入库列名，反向生成 SQLite schema，额外系统列 ProviderId/AccountId/PlanType/Timestamp；
/// ③ 百分比存数字 0-100、比例归一、金额分→元；④ 敏感字段（<see cref="ApiKey"/> 等）永不入库；
/// ⑤ 多窗口字段用 plan_type 区分归属。</para>
/// <para>插件通过 <see cref="IUsageProvider.MapToStandardFields"/> 将原始数据映射为标准字段名，
/// 差异检测引擎按标准字段名进行字段级对比，仅保存有变化的字段。旧前缀字段经 <see cref="UsageFieldAliases"/> 解析到现名。</para>
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

    // ===================== 系统列（每行必带，非插件上报） =====================

    /// <summary>插件 ID（系统列）</summary>
    public const string ProviderId = "provider_id";

    /// <summary>账号哈希 = hash(provider_id + 平台稳定ID)（系统列，不明文存个人 ID）</summary>
    public const string AccountId = "account_id";

    /// <summary>用量窗口/配额类型（FiveHour/Weekly/Daily/Total/Api/TokenPlan，系统列）</summary>
    public const string PlanType = "plan_type";

    /// <summary>采集时间（UTC，系统列）</summary>
    public const string Timestamp = "timestamp";

    // ===================== 用量百分比（多窗口，plan_type 区分） =====================

    /// <summary>总用量百分比（0-100）</summary>
    public const string TotalUsedPercent = "total_used_percent";

    /// <summary>Code 子维度用量百分比</summary>
    public const string CodeUsedPercent = "code_used_percent";

    /// <summary>5 小时窗口已用百分比（0-100）</summary>
    public const string FiveHourUsedPercent = "five_hour_used_percent";

    /// <summary>周窗口已用百分比（0-100）</summary>
    public const string WeeklyUsedPercent = "weekly_used_percent";

    /// <summary>7 天窗口已用百分比（0-100）</summary>
    public const string SevenDayUsedPercent = "seven_day_used_percent";

    // ===================== 配额额度 =====================

    /// <summary>通用剩余额度</summary>
    public const string RemainingAmount = "remaining_amount";

    /// <summary>已用积分</summary>
    public const string UsedCredits = "used_credits";

    /// <summary>总积分</summary>
    public const string TotalCredits = "total_credits";

    /// <summary>剩余积分</summary>
    public const string RemainingCredits = "remaining_credits";

    /// <summary>总消耗积分</summary>
    public const string TotalUsedCredits = "total_used_credits";

    /// <summary>可用 Token 估算</summary>
    public const string AvailableTokenEstimation = "available_token_estimation";

    // ===================== 费用 / 钱包（货币） =====================

    /// <summary>充值余额</summary>
    public const string BalanceAmount = "balance_amount";

    /// <summary>赠送余额</summary>
    public const string BonusBalanceAmount = "bonus_balance_amount";

    /// <summary>月消费</summary>
    public const string MonthlyCost = "monthly_cost";

    /// <summary>累计消费</summary>
    public const string TotalCost = "total_cost";

    /// <summary>货币（CNY/USD）</summary>
    public const string Currency = "currency";

    // ===================== 重置 / 有效期时间（UTC） =====================

    /// <summary>5h 窗口重置时刻（UTC）</summary>
    public const string FiveHourResetAt = "five_hour_reset_at";

    /// <summary>周窗口重置时刻（UTC）</summary>
    public const string WeeklyResetAt = "weekly_reset_at";

    /// <summary>7 天窗口重置时刻（UTC）</summary>
    public const string SevenDayResetAt = "seven_day_reset_at";

    /// <summary>总额度重置时刻（UTC）</summary>
    public const string TotalQuotaResetAt = "total_quota_reset_at";

    /// <summary>配额刷新日期（UTC）</summary>
    public const string QuotaNextResetAt = "quota_next_reset_at";

    /// <summary>当月配额有效期起（UTC）</summary>
    public const string QuotaPeriodStartAt = "quota_period_start_at";

    /// <summary>当月配额有效期止（UTC）</summary>
    public const string QuotaPeriodEndAt = "quota_period_end_at";

    // ===================== 订阅信息 =====================

    /// <summary>订阅档位名称（从对应语言网页抓取）</summary>
    public const string SubscriptionTier = "subscription_tier";

    /// <summary>订阅类型（如 Token Plan / Coding Plan / Agent Plan / API；与档位并列显示）</summary>
    public const string SubscriptionType = "subscription_type";

    /// <summary>会员等级</summary>
    public const string SubscriptionLevel = "subscription_level";

    /// <summary>订阅价格（分→元）</summary>
    public const string SubscriptionPrice = "subscription_price";

    /// <summary>计费周期（月/年）</summary>
    public const string SubscriptionCycleType = "subscription_cycle_type";

    /// <summary>订阅是否激活</summary>
    public const string SubscriptionActive = "subscription_active";

    /// <summary>下次续费时间（UTC）</summary>
    public const string SubscriptionNextBillingAt = "subscription_next_billing_at";

    /// <summary>订阅到期时间（UTC）</summary>
    public const string SubscriptionEndAt = "subscription_end_at";

    // ===================== 加油包 / 运营额度 =====================

    /// <summary>加油包是否开启</summary>
    public const string BoosterEnabled = "booster_enabled";

    /// <summary>加油包余额（分→元）</summary>
    public const string BoosterBalance = "booster_balance";

    /// <summary>加油包本月消费</summary>
    public const string BoosterMonthlyUsed = "booster_monthly_used";

    /// <summary>赠送额度</summary>
    public const string GiftCredits = "gift_credits";

    /// <summary>赠送额度有效期（UTC）</summary>
    public const string GiftExpiresAt = "gift_expires_at";

    /// <summary>补偿额度</summary>
    public const string CompensationCredits = "compensation_credits";

    /// <summary>补偿额度是否生效</summary>
    public const string CompensationActive = "compensation_active";

    // ===================== 媒体次数 =====================

    /// <summary>视频赠送已用次数（已用 = total - remains）</summary>
    public const string VideoUsedCount = "video_used_count";

    /// <summary>视频赠送总次数</summary>
    public const string VideoTotalCount = "video_total_count";

    // ===================== 阈值 / 配额上下限（进度条 Upper/Lower） =====================

    /// <summary>5h 用量上限（进度条 Upper）</summary>
    public const string FiveHourUpperLimit = "five_hour_upper_limit";

    /// <summary>5h 用量下限（进度条 Lower）</summary>
    public const string FiveHourLowerLimit = "five_hour_lower_limit";

    /// <summary>周用量上限（进度条 Upper）</summary>
    public const string WeeklyUpperLimit = "weekly_upper_limit";

    /// <summary>月用量上限（进度条 Upper）</summary>
    public const string MonthlyUpperLimit = "monthly_upper_limit";

    /// <summary>周用量下限（进度条 Lower）</summary>
    public const string WeeklyLowerLimit = "weekly_lower_limit";

    /// <summary>视频配额已用（进度条 Value）</summary>
    public const string VideoQuota = "video_quota";

    /// <summary>视频配额上限（进度条 Upper）</summary>
    public const string VideoQuotaLimit = "video_quota_limit";

    // ===================== 缓存命中 =====================

    /// <summary>缓存命中率（0-100）</summary>
    public const string CacheHitPercent = "cache_hit_percent";

    // ===================== 状态 / 元信息 =====================

    /// <summary>活跃天数</summary>
    public const string ActiveDays = "active_days";

    /// <summary>注册以来总天数（Stage C 新增：与 active_days 组成“活跃/总”比）</summary>
    public const string TotalDays = "total_days";

    /// <summary>用量排名前 %（越小越靠前）</summary>
    public const string UsageRankingPercent = "usage_ranking_percent";

    /// <summary>单日峰值日期</summary>
    public const string MostActiveDate = "most_active_date";

    /// <summary>单日峰值 Token（原始数字）</summary>
    public const string MostActiveToken = "most_active_token";

    // ===================== 账号元数据 =====================

    /// <summary>账号显示名（Provider 网页提供，如 group_name；手机号等敏感值需脱敏后再写入）</summary>
    public const string AccountDisplayName = "account_display_name";

    // ===================== 时序明细（趋势 / 模型×日 / 逐请求） =====================

    /// <summary>明细日期（趋势/模型×日去重键）</summary>
    public const string Date = "date";

    /// <summary>每日总计 Token（day 级 = input + output）</summary>
    public const string TokenTotal = "token_total";

    /// <summary>每日缓存命中率（0-100）</summary>
    public const string DailyCacheHitPercent = "daily_cache_hit_percent";

    /// <summary>每日 Token 趋势日期（折线图 Meta，来自 date_model_usage.date）</summary>
    public const string DailyTokenDate = "daily_token_date";

    /// <summary>每日 Token 趋势值（折线图 Value，day 级 = input + output）</summary>
    public const string DailyTokenValue = "daily_token_value";

    /// <summary>每日缓存命中趋势日期（热力图 Meta）</summary>
    public const string DailyCacheHitDate = "daily_cache_hit_date";

    /// <summary>每日缓存命中趋势值（热力图 Value，0-100）</summary>
    public const string DailyCacheHitValue = "daily_cache_hit_value";

    /// <summary>模型名（模型×日维度）</summary>
    public const string ModelName = "model_name";

    /// <summary>输入 Token</summary>
    public const string InputToken = "input_token";

    /// <summary>输出 Token</summary>
    public const string OutputToken = "output_token";

    /// <summary>缓存读取 Token</summary>
    public const string CacheReadToken = "cache_read_token";

    /// <summary>缓存未命中 Token</summary>
    public const string CacheMissToken = "cache_miss_token";

    /// <summary>请求次数</summary>
    public const string RequestCount = "request_count";

    /// <summary>请求 ID（逐请求流水去重键）</summary>
    public const string RequestId = "request_id";

    /// <summary>请求客户端（IDE/CLI 等）</summary>
    public const string RequestClient = "request_client";

    /// <summary>请求状态（httpStatus）</summary>
    public const string RequestStatus = "request_status";

    /// <summary>请求费用</summary>
    public const string RequestCost = "request_cost";

    // ===================== 敏感字段（永不入库 / 日志） =====================

    /// <summary>API 密钥明文（敏感，永不入库/日志）</summary>
    public const string ApiKey = "api_key";

    /// <summary>访问令牌（敏感，永不入库/日志）</summary>
    public const string AccessToken = "access_token";

    // Stage C：旧 Provider 前缀遗留常量已全部删除——历史库旧名由 UsageFieldAliases 解析到现名。

    // ===================== 扩展字段前缀 =====================

    /// <summary>插件自定义扩展字段前缀（extras 字典中的字段）</summary>
    public const string ExtraPrefix = "extra_";

    /// <summary>
    /// 获取所有标准字段名（用于验证和调试）
    /// </summary>
    public static IReadOnlyList<string> AllStandardFields { get; } = new[]
    {
        // 通用字段
        UsedPercent, UsedAmount, TotalAmount, UsedTokens, TotalTokens,
        Unit, ExpireDate, LastUpdated, IsSuccess, ErrorMessage,
        // 系统列
        ProviderId, AccountId, PlanType, Timestamp,
        // 用量百分比（多窗口）
        TotalUsedPercent, CodeUsedPercent,
        FiveHourUsedPercent, WeeklyUsedPercent, SevenDayUsedPercent,
        // 配额额度
        RemainingAmount, UsedCredits, TotalCredits, RemainingCredits,
        TotalUsedCredits, AvailableTokenEstimation,
        // 费用 / 钱包
        BalanceAmount, BonusBalanceAmount, MonthlyCost, TotalCost, Currency,
        // 重置 / 有效期时间
        FiveHourResetAt, WeeklyResetAt, SevenDayResetAt, TotalQuotaResetAt,
        QuotaNextResetAt, QuotaPeriodStartAt, QuotaPeriodEndAt,
        // 订阅信息
        SubscriptionTier, SubscriptionType, SubscriptionLevel, SubscriptionPrice, SubscriptionCycleType,
        SubscriptionActive, SubscriptionNextBillingAt, SubscriptionEndAt,
        // 加油包 / 运营额度
        BoosterEnabled, BoosterBalance, BoosterMonthlyUsed,
        GiftCredits, GiftExpiresAt, CompensationCredits, CompensationActive,
        // 媒体次数
        VideoUsedCount, VideoTotalCount,
        // 阈值 / 配额上下限
        FiveHourUpperLimit, FiveHourLowerLimit, WeeklyUpperLimit, MonthlyUpperLimit, WeeklyLowerLimit,
        VideoQuota, VideoQuotaLimit,
        // 缓存命中
        CacheHitPercent,
        // 状态 / 元信息
        ActiveDays, TotalDays, UsageRankingPercent, MostActiveDate, MostActiveToken,
        // 账号元数据
        AccountDisplayName,
        // 时序明细
        Date, TokenTotal, DailyCacheHitPercent, ModelName,
        DailyTokenDate, DailyTokenValue, DailyCacheHitDate, DailyCacheHitValue,
        InputToken, OutputToken, CacheReadToken, CacheMissToken,
        RequestCount, RequestId, RequestClient, RequestStatus, RequestCost,
        // 敏感字段（仅用于白名单识别与入库拦截）
        ApiKey, AccessToken
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
            // Stage C：mm_/ds_ 旧名分支已删除——统一经下方 UsageFieldAliases 解析到现用通用字段。
            // 其余名称尝试经字段改名历史表解析到现用统一字段（req-107 B1），无别名则保持原样
            _ => UsageFieldAliases.Resolve(rawFieldName) ?? rawFieldName
        };
    }
}
