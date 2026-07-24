# SDK 字段映射矩阵（req-107 B10）

> 自动生成自 `UsageFieldMetadataRegistry` + `UsageFieldsSchemaExporter`，供插件作者查阅 SDK 合法字段。

## 字段按类别分组

| 字段名 | Category | DataType | Visibility | LabelKey | 说明 |
|---|---|---|---|---|---|
| `code_used_percent` | Usage | Percent | Public | `Field.CodeUsedPercent` | Code 子维度用量百分比 |
| `five_hour_used_percent` | Usage | Percent | Public | `Field.FiveHourUsedPercent` | 5 小时窗口已用百分比 |
| `kimi_used_percent` | Usage | Percent | Public | `Field.KimiUsedPercent` | 派生：total - code |
| `seven_day_used_percent` | Usage | Percent | Public | `Field.SevenDayUsedPercent` | 7 天窗口已用百分比 |
| `total_used_percent` | Usage | Percent | Public | `Field.TotalUsedPercent` | 总用量百分比 |
| `used_percent` | Usage | Percent | Public | `Field.UsedPercent` | 通用已用百分比（0-100） |
| `weekly_used_percent` | Usage | Percent | Public | `Field.WeeklyUsedPercent` | 周窗口已用百分比 |
| `available_token_estimation` | Quota | Token | Public | `Field.AvailableTokenEstimation` | 可用 Token 估算 |
| `five_hour_lower_limit` | Quota | Number | Public | `Field.FiveHourLowerLimit` | 5h 用量下限（进度条 Lower） |
| `five_hour_upper_limit` | Quota | Number | Public | `Field.FiveHourUpperLimit` | 5h 用量上限（进度条 Upper） |
| `monthly_upper_limit` | Quota | Number | Public | `Field.MonthlyUpperLimit` | 月用量上限（进度条 Upper） |
| `remaining_amount` | Quota | Number | Public | `Field.RemainingAmount` | 通用剩余额度 |
| `remaining_credits` | Quota | Credit | Public | `Field.RemainingCredits` | 剩余积分 |
| `total_amount` | Quota | Number | Public | `Field.TotalAmount` | 通用总额度 |
| `total_credits` | Quota | Credit | Public | `Field.TotalCredits` | 总积分 |
| `total_tokens` | Quota | Token | Public | `Field.TotalTokens` | 总 Token 数（-1=不限） |
| `total_used_credits` | Quota | Credit | Public | `Field.TotalUsedCredits` | 总消耗积分 |
| `unit` | Quota | Text | Public | `Field.Unit` | 额度单位（credits/token/CNY） |
| `used_amount` | Quota | Number | Public | `Field.UsedAmount` | 通用已用额度 |
| `used_credits` | Quota | Credit | Public | `Field.UsedCredits` | 已用积分 |
| `used_tokens` | Quota | Token | Public | `Field.UsedTokens` | 已用 Token 数 |
| `video_quota` | Quota | Count | Public | `Field.VideoQuota` | 视频配额已用（进度条 Value） |
| `video_quota_limit` | Quota | Count | Public | `Field.VideoQuotaLimit` | 视频配额上限（进度条 Upper） |
| `weekly_lower_limit` | Quota | Number | Public | `Field.WeeklyLowerLimit` | 周用量下限（进度条 Lower） |
| `weekly_upper_limit` | Quota | Number | Public | `Field.WeeklyUpperLimit` | 周用量上限（进度条 Upper） |
| `balance_amount` | Cost | Currency | Public | `Field.BalanceAmount` | 充值余额 |
| `bonus_balance_amount` | Cost | Currency | Public | `Field.BonusBalanceAmount` | 赠送余额 |
| `currency` | Cost | Text | Public | `Field.Currency` | 货币（CNY/USD） |
| `monthly_cost` | Cost | Currency | Public | `Field.MonthlyCost` | 月消费 |
| `total_cost` | Cost | Currency | Public | `Field.TotalCost` | 累计消费 |
| `expire_date` | Reset | DateTime | Public | `Field.ExpireDate` | 通用过期时间（UTC） |
| `five_hour_reset_at` | Reset | DateTime | Public | `Field.FiveHourResetAt` | 5h 窗口重置时刻（UTC） |
| `last_updated` | Reset | DateTime | Public | `Field.LastUpdated` | 最后更新时间（UTC） |
| `quota_next_reset_at` | Reset | DateTime | Public | `Field.QuotaNextResetAt` | 配额刷新日期（UTC） |
| `quota_period_end_at` | Reset | DateTime | Public | `Field.QuotaPeriodEndAt` | 当月配额有效期止（UTC） |
| `quota_period_start_at` | Reset | DateTime | Public | `Field.QuotaPeriodStartAt` | 当月配额有效期起（UTC） |
| `seven_day_reset_at` | Reset | DateTime | Public | `Field.SevenDayResetAt` | 7 天窗口重置时刻（UTC） |
| `total_quota_reset_at` | Reset | DateTime | Public | `Field.TotalQuotaResetAt` | 总额度重置时刻（UTC） |
| `weekly_reset_at` | Reset | DateTime | Public | `Field.WeeklyResetAt` | 周窗口重置时刻（UTC） |
| `subscription_active` | Subscription | Bool | Public | `Field.SubscriptionActive` | 订阅是否激活 |
| `subscription_cycle_type` | Subscription | Text | Public | `Field.SubscriptionCycleType` | 计费周期（月/年） |
| `subscription_end_at` | Subscription | DateTime | Public | `Field.SubscriptionEndAt` | 订阅到期时间（UTC） |
| `subscription_level` | Subscription | Text | Public | `Field.SubscriptionLevel` | 会员等级 |
| `subscription_next_billing_at` | Subscription | DateTime | Public | `Field.SubscriptionNextBillingAt` | 下次续费时间（UTC） |
| `subscription_price` | Subscription | Currency | Public | `Field.SubscriptionPrice` | 订阅价格（分→元） |
| `subscription_tier` | Subscription | Text | Public | `Field.SubscriptionTier` | 订阅档位名称（从对应语言网页抓取） |
| `subscription_type` | Subscription | Text | Public | `Field.SubscriptionType` | 订阅类型（Token Plan / Coding Plan / Agent Plan / API） |
| `booster_balance` | Booster | Currency | Public | `Field.BoosterBalance` | 加油包余额（分→元） |
| `booster_enabled` | Booster | Bool | Public | `Field.BoosterEnabled` | 加油包是否开启 |
| `booster_monthly_used` | Booster | Currency | Public | `Field.BoosterMonthlyUsed` | 加油包本月消费 |
| `compensation_active` | Booster | Bool | Public | `Field.CompensationActive` | 补偿额度是否生效 |
| `compensation_credits` | Booster | Credit | Public | `Field.CompensationCredits` | 补偿额度 |
| `gift_credits` | Booster | Credit | Public | `Field.GiftCredits` | 赠送额度 |
| `gift_expires_at` | Booster | DateTime | Public | `Field.GiftExpiresAt` | 赠送额度有效期（UTC） |
| `video_total_count` | Media | Count | Public | `Field.VideoTotalCount` | 视频赠送总次数 |
| `video_used_count` | Media | Count | Public | `Field.VideoUsedCount` | 视频赠送已用次数（已用 = total - remains） |
| `cache_hit_percent` | Cache | Percent | Public | `Field.CacheHitPercent` | 缓存命中率（0-100） |
| `active_days` | State | Count | Public | `Field.ActiveDays` | 活跃天数 |
| `error_message` | State | Text | Internal | `Field.ErrorMessage` | 错误信息（主程序提供） |
| `is_success` | State | Bool | Internal | `Field.IsSuccess` | 查询是否成功 |
| `most_active_date` | State | DateTime | Public | `Field.MostActiveDate` | 单日峰值日期 |
| `most_active_token` | State | Token | Public | `Field.MostActiveToken` | 单日峰值 Token（原始数字） |
| `usage_ranking_percent` | State | Percent | Public | `Field.UsageRankingPercent` | 用量排名前 %（越小越靠前） |
| `account_display_name` | Account | Text | Public | `Field.AccountDisplayName` | 账号显示名（Provider 网页提供） |
| `access_token` | Meta | Text | Sensitive | `Field.AccessToken` | 访问令牌（敏感，永不入库/日志） |
| `account_id` | Meta | Text | Internal | `Field.AccountId` | 账号哈希 = hash(provider_id + 平台稳定ID)（系统列） |
| `api_key` | Meta | Text | Sensitive | `Field.ApiKey` | API 密钥明文（敏感，永不入库/日志） |
| `plan_type` | Meta | Text | Internal | `Field.PlanType` | 用量窗口/配额类型（FiveHour/Weekly/Daily/Total/Api/TokenPlan） |
| `provider_id` | Meta | Text | Internal | `Field.ProviderId` | 插件 ID（系统列） |
| `timestamp` | Meta | DateTime | Internal | `Field.Timestamp` | 采集时间（UTC，系统列） |
| `cache_miss_token` | Detail | Token | Public | `Field.CacheMissToken` | 缓存未命中 Token |
| `cache_read_token` | Detail | Token | Public | `Field.CacheReadToken` | 缓存读取 Token |
| `daily_cache_hit_date` | Detail | DateTime | Public | `Field.DailyCacheHitDate` | 每日缓存命中趋势日期（热力图 Meta） |
| `daily_cache_hit_percent` | Detail | Percent | Public | `Field.DailyCacheHitPercent` | 每日缓存命中率 |
| `daily_cache_hit_value` | Detail | Percent | Public | `Field.DailyCacheHitValue` | 每日缓存命中趋势值（热力图 Value，0-100） |
| `daily_token_date` | Detail | DateTime | Public | `Field.DailyTokenDate` | 每日 Token 趋势日期（折线图 Meta） |
| `daily_token_value` | Detail | Token | Public | `Field.DailyTokenValue` | 每日 Token 趋势值（折线图 Value，day 级 = input + output） |
| `date` | Detail | DateTime | Internal | `Field.Date` | 明细日期（趋势/模型×日去重键） |
| `input_token` | Detail | Token | Public | `Field.InputToken` | 输入 Token |
| `model_name` | Detail | Text | Public | `Field.ModelName` | 模型名（模型×日维度） |
| `output_token` | Detail | Token | Public | `Field.OutputToken` | 输出 Token |
| `request_client` | Detail | Text | Public | `Field.RequestClient` | 请求客户端（IDE/CLI 等） |
| `request_cost` | Detail | Currency | Public | `Field.RequestCost` | 请求费用 |
| `request_count` | Detail | Count | Public | `Field.RequestCount` | 请求次数 |
| `request_id` | Detail | Text | Internal | `Field.RequestId` | 请求 ID（逐请求流水去重键） |
| `request_status` | Detail | Text | Public | `Field.RequestStatus` | 请求状态（httpStatus） |
| `token_total` | Detail | Token | Public | `Field.TokenTotal` | 每日总计 Token（day 级 = input + output） |

## ChartKindSpec（图表能力规格）

| Kind | SupportedSlicerModes | RequiredRoles | OptionalRoles | AllowedValueTypes | SupportsColorTiers |
|---|---|---|---|---|---|
| Line | Period, DataGroup | Meta, Value | — | Percent, Number, Token, Credit, Currency, Count | False |
| Bar | DataGroup | Value | Upper, Lower, Meta | Percent, Number, Token, Credit, Currency, Count | True |
| HeatMap | Period | Meta, Value | — | Percent, Number, Token, Credit, Currency, Count | True |
| Ring | DataGroup | Value | — | Percent | True |
| Number | — | Value | — | Percent, Number, Token, Credit, Currency, Count | False |
| MiniRingChart | DataGroup | Value | — | Percent | True |
| MiniText | — | — | Reset, Meta, Value | — | False |

## Transformers（内置转换器）

`parsePercent`, `parseNumber`, `parseDate`, `trim`, `stripNonNumeric`, `identity`

