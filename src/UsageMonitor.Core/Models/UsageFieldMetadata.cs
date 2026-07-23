using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// SDK 字段用途分类（req-107 B1）。
/// <para>对应 <c>docs/sdk-unified-fields.md</c> 的字段 Category 维度，供显示声明、入库分组与 UI 渲染按类聚合。</para>
/// </summary>
public enum UsageFieldCategory
{
    /// <summary>用量（百分比 / 次数等）。</summary>
    Usage,
    /// <summary>配额额度（金额 / Token / 积分）。</summary>
    Quota,
    /// <summary>费用 / 钱包（货币）。</summary>
    Cost,
    /// <summary>重置 / 有效期时间。</summary>
    Reset,
    /// <summary>订阅信息。</summary>
    Subscription,
    /// <summary>加油包 / 运营额度。</summary>
    Booster,
    /// <summary>媒体次数（视频赠送等）。</summary>
    Media,
    /// <summary>缓存命中。</summary>
    Cache,
    /// <summary>状态 / 元信息（活跃天、排名、成功标志）。</summary>
    State,
    /// <summary>账号元数据（账号 ID / 显示名 / 昵称）。</summary>
    Account,
    /// <summary>系统 / 元列（ProviderId / AccountId / PlanType / Timestamp）。</summary>
    Meta,
    /// <summary>时序明细（趋势 / 模型×日 / 逐请求流水）。</summary>
    Detail
}

/// <summary>
/// SDK 字段可见性（req-107 B1）。
/// </summary>
public enum UsageFieldVisibility
{
    /// <summary>可显示 / 可入库。</summary>
    Public,
    /// <summary>仅内部使用（可入库但不直接展示）。</summary>
    Internal,
    /// <summary>敏感字段：永不入库 / 日志（api_key / token / 手机号等）。</summary>
    Sensitive
}

/// <summary>
/// SDK 字段值的数据类型（req-107 B1）。决定入库列类型与显示格式化策略。
/// </summary>
public enum UsageFieldDataType
{
    /// <summary>百分比（0-100 数字，不存 "53%" 字符串）。</summary>
    Percent,
    /// <summary>普通数值。</summary>
    Number,
    /// <summary>Token 数（-1 表示不限）。</summary>
    Token,
    /// <summary>积分。</summary>
    Credit,
    /// <summary>货币金额（分→元归一）。</summary>
    Currency,
    /// <summary>次数 / 计数。</summary>
    Count,
    /// <summary>日期时间（UTC）。</summary>
    DateTime,
    /// <summary>文本。</summary>
    Text,
    /// <summary>布尔。</summary>
    Bool
}

/// <summary>
/// SDK 字段元数据（req-107 B1）：描述一个标准字段的语义与约束。
/// <para>插件零翻译原则：字段标签 / 单位 / 翻译由本元数据的 <see cref="LabelKey"/> 指向主程序 i18n 资源，
/// 插件只上报 <see cref="UsageFields"/> 标准字段名与原始值，不承担翻译责任。</para>
/// </summary>
/// <param name="FieldName">标准字段名（= <see cref="UsageFields"/> 常量 = 入库列名）。</param>
/// <param name="Category">用途分类。</param>
/// <param name="Visibility">可见性（敏感字段永不入库）。</param>
/// <param name="DataType">值数据类型。</param>
/// <param name="LabelKey">主程序 i18n 资源键（用于显示字段标签）。</param>
/// <param name="Description">字段语义说明（供文档 / 校验报告使用）。</param>
public sealed record UsageFieldMetadata(
    string FieldName,
    UsageFieldCategory Category,
    UsageFieldVisibility Visibility,
    UsageFieldDataType DataType,
    string LabelKey,
    string Description);

/// <summary>
/// SDK 字段元数据注册表（req-107 B1）。
/// <para>以 <see cref="UsageFields"/> 标准字段名为键提供元数据查询，支撑：
/// ① 数据库 schema 反向生成（列类型由 <see cref="UsageFieldDataType"/> 推导）；
/// ② 显示声明字段白名单校验；③ 主程序 i18n 标签解析；④ 敏感字段入库拦截。</para>
/// </summary>
public static class UsageFieldMetadataRegistry
{
    private static readonly Dictionary<string, UsageFieldMetadata> ByField = new(BuildRegistry(), System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构建全部标准字段的元数据（依据 <c>docs/sdk-unified-fields.md</c>）。
    /// </summary>
    private static Dictionary<string, UsageFieldMetadata> BuildRegistry()
    {
        var list = new List<UsageFieldMetadata>
        {
            // —— 系统 / 元列 ——
            new(UsageFields.ProviderId, UsageFieldCategory.Meta, UsageFieldVisibility.Internal, UsageFieldDataType.Text, "Field.ProviderId", "插件 ID（系统列）"),
            new(UsageFields.AccountId, UsageFieldCategory.Meta, UsageFieldVisibility.Internal, UsageFieldDataType.Text, "Field.AccountId", "账号哈希 = hash(provider_id + 平台稳定ID)（系统列）"),
            new(UsageFields.PlanType, UsageFieldCategory.Meta, UsageFieldVisibility.Internal, UsageFieldDataType.Text, "Field.PlanType", "用量窗口/配额类型（FiveHour/Weekly/Daily/Total/Api/TokenPlan）"),
            new(UsageFields.Timestamp, UsageFieldCategory.Meta, UsageFieldVisibility.Internal, UsageFieldDataType.DateTime, "Field.Timestamp", "采集时间（UTC，系统列）"),
            // —— 用量百分比（多窗口）——
            new(UsageFields.UsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.UsedPercent", "通用已用百分比（0-100）"),
            new(UsageFields.TotalUsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.TotalUsedPercent", "总用量百分比"),
            new(UsageFields.CodeUsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.CodeUsedPercent", "Code 子维度用量百分比"),
            new(UsageFields.KimiUsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.KimiUsedPercent", "派生：total - code"),
            new(UsageFields.FiveHourUsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.FiveHourUsedPercent", "5 小时窗口已用百分比"),
            new(UsageFields.WeeklyUsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.WeeklyUsedPercent", "周窗口已用百分比"),
            new(UsageFields.SevenDayUsedPercent, UsageFieldCategory.Usage, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.SevenDayUsedPercent", "7 天窗口已用百分比"),
            // —— 配额额度 ——
            new(UsageFields.UsedAmount, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.UsedAmount", "通用已用额度"),
            new(UsageFields.TotalAmount, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.TotalAmount", "通用总额度"),
            new(UsageFields.RemainingAmount, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.RemainingAmount", "通用剩余额度"),
            new(UsageFields.UsedTokens, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.UsedTokens", "已用 Token 数"),
            new(UsageFields.TotalTokens, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.TotalTokens", "总 Token 数（-1=不限）"),
            new(UsageFields.UsedCredits, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Credit, "Field.UsedCredits", "已用积分"),
            new(UsageFields.TotalCredits, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Credit, "Field.TotalCredits", "总积分"),
            new(UsageFields.RemainingCredits, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Credit, "Field.RemainingCredits", "剩余积分"),
            new(UsageFields.TotalUsedCredits, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Credit, "Field.TotalUsedCredits", "总消耗积分"),
            new(UsageFields.AvailableTokenEstimation, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.AvailableTokenEstimation", "可用 Token 估算"),
            new(UsageFields.Unit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.Unit", "额度单位（credits/token/CNY）"),
            // —— 费用 / 钱包 ——
            new(UsageFields.BalanceAmount, UsageFieldCategory.Cost, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.BalanceAmount", "充值余额"),
            new(UsageFields.BonusBalanceAmount, UsageFieldCategory.Cost, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.BonusBalanceAmount", "赠送余额"),
            new(UsageFields.MonthlyCost, UsageFieldCategory.Cost, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.MonthlyCost", "月消费"),
            new(UsageFields.TotalCost, UsageFieldCategory.Cost, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.TotalCost", "累计消费"),
            new(UsageFields.Currency, UsageFieldCategory.Cost, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.Currency", "货币（CNY/USD）"),
            // —— 重置 / 有效期时间 ——
            new(UsageFields.FiveHourResetAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.FiveHourResetAt", "5h 窗口重置时刻（UTC）"),
            new(UsageFields.WeeklyResetAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.WeeklyResetAt", "周窗口重置时刻（UTC）"),
            new(UsageFields.SevenDayResetAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.SevenDayResetAt", "7 天窗口重置时刻（UTC）"),
            new(UsageFields.TotalQuotaResetAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.TotalQuotaResetAt", "总额度重置时刻（UTC）"),
            new(UsageFields.QuotaNextResetAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.QuotaNextResetAt", "配额刷新日期（UTC）"),
            new(UsageFields.QuotaPeriodStartAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.QuotaPeriodStartAt", "当月配额有效期起（UTC）"),
            new(UsageFields.QuotaPeriodEndAt, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.QuotaPeriodEndAt", "当月配额有效期止（UTC）"),
            new(UsageFields.ExpireDate, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.ExpireDate", "通用过期时间（UTC）"),
            new(UsageFields.LastUpdated, UsageFieldCategory.Reset, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.LastUpdated", "最后更新时间（UTC）"),
            // —— 订阅信息 ——
            new(UsageFields.SubscriptionTier, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.SubscriptionTier", "订阅档位名称（从对应语言网页抓取）"),
            new(UsageFields.SubscriptionLevel, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.SubscriptionLevel", "会员等级"),
            new(UsageFields.SubscriptionPrice, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.SubscriptionPrice", "订阅价格（分→元）"),
            new(UsageFields.SubscriptionCycleType, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.SubscriptionCycleType", "计费周期（月/年）"),
            new(UsageFields.SubscriptionActive, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.Bool, "Field.SubscriptionActive", "订阅是否激活"),
            new(UsageFields.SubscriptionNextBillingAt, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.SubscriptionNextBillingAt", "下次续费时间（UTC）"),
            new(UsageFields.SubscriptionEndAt, UsageFieldCategory.Subscription, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.SubscriptionEndAt", "订阅到期时间（UTC）"),
            // —— 加油包 / 运营额度 ——
            new(UsageFields.BoosterEnabled, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.Bool, "Field.BoosterEnabled", "加油包是否开启"),
            new(UsageFields.BoosterBalance, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.BoosterBalance", "加油包余额（分→元）"),
            new(UsageFields.BoosterMonthlyUsed, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.BoosterMonthlyUsed", "加油包本月消费"),
            new(UsageFields.GiftCredits, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.Credit, "Field.GiftCredits", "赠送额度"),
            new(UsageFields.GiftExpiresAt, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.GiftExpiresAt", "赠送额度有效期（UTC）"),
            new(UsageFields.CompensationCredits, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.Credit, "Field.CompensationCredits", "补偿额度"),
            new(UsageFields.CompensationActive, UsageFieldCategory.Booster, UsageFieldVisibility.Public, UsageFieldDataType.Bool, "Field.CompensationActive", "补偿额度是否生效"),
            // —— 媒体次数 ——
            new(UsageFields.VideoUsedCount, UsageFieldCategory.Media, UsageFieldVisibility.Public, UsageFieldDataType.Count, "Field.VideoUsedCount", "视频赠送已用次数（已用 = total - remains）"),
            new(UsageFields.VideoTotalCount, UsageFieldCategory.Media, UsageFieldVisibility.Public, UsageFieldDataType.Count, "Field.VideoTotalCount", "视频赠送总次数"),
            // —— 阈值 / 配额上下限 ——
            new(UsageFields.FiveHourUpperLimit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.FiveHourUpperLimit", "5h 用量上限（进度条 Upper）"),
            new(UsageFields.FiveHourLowerLimit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.FiveHourLowerLimit", "5h 用量下限（进度条 Lower）"),
            new(UsageFields.WeeklyUpperLimit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.WeeklyUpperLimit", "周用量上限（进度条 Upper）"),
                        new(UsageFields.MonthlyUpperLimit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.MonthlyUpperLimit", "月用量上限（进度条 Upper）"),
            new(UsageFields.WeeklyLowerLimit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Number, "Field.WeeklyLowerLimit", "周用量下限（进度条 Lower）"),
            new(UsageFields.VideoQuota, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Count, "Field.VideoQuota", "视频配额已用（进度条 Value）"),
            new(UsageFields.VideoQuotaLimit, UsageFieldCategory.Quota, UsageFieldVisibility.Public, UsageFieldDataType.Count, "Field.VideoQuotaLimit", "视频配额上限（进度条 Upper）"),
            // —— 缓存命中 ——
            new(UsageFields.CacheHitPercent, UsageFieldCategory.Cache, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.CacheHitPercent", "缓存命中率（0-100）"),
            // —— 状态 / 元信息 ——
            new(UsageFields.IsSuccess, UsageFieldCategory.State, UsageFieldVisibility.Internal, UsageFieldDataType.Bool, "Field.IsSuccess", "查询是否成功"),
            new(UsageFields.ErrorMessage, UsageFieldCategory.State, UsageFieldVisibility.Internal, UsageFieldDataType.Text, "Field.ErrorMessage", "错误信息（主程序提供）"),
            new(UsageFields.ActiveDays, UsageFieldCategory.State, UsageFieldVisibility.Public, UsageFieldDataType.Count, "Field.ActiveDays", "活跃天数"),
            new(UsageFields.UsageRankingPercent, UsageFieldCategory.State, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.UsageRankingPercent", "用量排名前 %（越小越靠前）"),
            new(UsageFields.MostActiveDate, UsageFieldCategory.State, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.MostActiveDate", "单日峰值日期"),
            new(UsageFields.MostActiveToken, UsageFieldCategory.State, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.MostActiveToken", "单日峰值 Token（原始数字）"),
            // —— 账号元数据 ——
            new(UsageFields.AccountDisplayName, UsageFieldCategory.Account, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.AccountDisplayName", "账号显示名（Provider 网页提供）"),
            new(UsageFields.AccountNickname, UsageFieldCategory.Account, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.AccountNickname", "账号昵称（用户自定义，Provider 内唯一）"),
            // —— 时序明细（趋势 / 模型×日 / 逐请求）——
            new(UsageFields.Date, UsageFieldCategory.Detail, UsageFieldVisibility.Internal, UsageFieldDataType.DateTime, "Field.Date", "明细日期（趋势/模型×日去重键）"),
            new(UsageFields.TokenTotal, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.TokenTotal", "每日总计 Token（day 级 = input + output）"),
            new(UsageFields.DailyCacheHitPercent, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.DailyCacheHitPercent", "每日缓存命中率"),
            new(UsageFields.DailyTokenDate, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.DailyTokenDate", "每日 Token 趋势日期（折线图 Meta）"),
            new(UsageFields.DailyTokenValue, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.DailyTokenValue", "每日 Token 趋势值（折线图 Value，day 级 = input + output）"),
            new(UsageFields.DailyCacheHitDate, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.DateTime, "Field.DailyCacheHitDate", "每日缓存命中趋势日期（热力图 Meta）"),
            new(UsageFields.DailyCacheHitValue, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Percent, "Field.DailyCacheHitValue", "每日缓存命中趋势值（热力图 Value，0-100）"),
            new(UsageFields.ModelName, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.ModelName", "模型名（模型×日维度）"),
            new(UsageFields.InputToken, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.InputToken", "输入 Token"),
            new(UsageFields.OutputToken, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.OutputToken", "输出 Token"),
            new(UsageFields.CacheReadToken, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.CacheReadToken", "缓存读取 Token"),
            new(UsageFields.CacheMissToken, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Token, "Field.CacheMissToken", "缓存未命中 Token"),
            new(UsageFields.RequestCount, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Count, "Field.RequestCount", "请求次数"),
            new(UsageFields.RequestId, UsageFieldCategory.Detail, UsageFieldVisibility.Internal, UsageFieldDataType.Text, "Field.RequestId", "请求 ID（逐请求流水去重键）"),
            new(UsageFields.RequestClient, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.RequestClient", "请求客户端（IDE/CLI 等）"),
            new(UsageFields.RequestStatus, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Text, "Field.RequestStatus", "请求状态（httpStatus）"),
            new(UsageFields.RequestCost, UsageFieldCategory.Detail, UsageFieldVisibility.Public, UsageFieldDataType.Currency, "Field.RequestCost", "请求费用"),
            // —— 敏感字段（永不入库）——
            new(UsageFields.ApiKey, UsageFieldCategory.Meta, UsageFieldVisibility.Sensitive, UsageFieldDataType.Text, "Field.ApiKey", "API 密钥明文（敏感，永不入库/日志）"),
            new(UsageFields.AccessToken, UsageFieldCategory.Meta, UsageFieldVisibility.Sensitive, UsageFieldDataType.Text, "Field.AccessToken", "访问令牌（敏感，永不入库/日志）")
        };
        var dict = new Dictionary<string, UsageFieldMetadata>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var m in list) dict[m.FieldName] = m;
        return dict;
    }

    /// <summary>
    /// 按标准字段名获取元数据；未注册字段返回 <c>null</c>。
    /// </summary>
    /// <param name="fieldName">标准字段名（<see cref="UsageFields"/> 常量）。</param>
    public static UsageFieldMetadata? Get(string fieldName)
        => !string.IsNullOrWhiteSpace(fieldName) && ByField.TryGetValue(fieldName, out var m) ? m : null;

    /// <summary>
    /// 判断字段是否已注册元数据（等价于"是否为 SDK 合法字段"，供白名单校验）。
    /// </summary>
    public static bool IsRegistered(string fieldName) => Get(fieldName) != null;

    /// <summary>
    /// 获取全部已注册元数据（供 schema 导出 / 字段矩阵文档生成）。
    /// </summary>
    public static IReadOnlyCollection<UsageFieldMetadata> All => ByField.Values;
}
