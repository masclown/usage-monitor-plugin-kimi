using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// SDK 字段改名历史表（req-107 B1）。
/// <para>记录字段的历史名称（旧名 / 别名 / Provider 前缀名）→ 现用标准名（<see cref="UsageFields"/> 常量）的映射，
/// 使加载插件声明、读取历史持久化数据时能把旧字段名解析到现名，支撑字段演进不丢数据。</para>
/// <para>典型来源：① req-107 统一去 <c>mm_</c>/<c>ds_</c> 前缀（如 <c>mm_5h_used_percent</c> → <c>five_hour_used_percent</c>）；
/// ② 插件 DOM 原始键（如 <c>mm_5hUsedPercent</c>）；③ 未来字段改名通过本表追加 alias 即可平滑迁移。</para>
/// </summary>
public static class UsageFieldAliases
{
    // 归一化键（小写、去下划线）→ 现用标准字段名。
    private static readonly Dictionary<string, string> Map = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // —— MiniMax 旧前缀字段（req-107 统一去前缀）——
        ["mm5husedpercent"] = UsageFields.FiveHourUsedPercent,
        ["mmweeklyusedpercent"] = UsageFields.WeeklyUsedPercent,
        ["mmremainingcredits"] = UsageFields.RemainingCredits,
        ["mmsubscriptiontitle"] = UsageFields.SubscriptionTier,
        ["mmsubscriptionactive"] = UsageFields.SubscriptionActive,
        // —— DeepSeek 旧前缀字段 ——
        ["dsspendamount"] = UsageFields.TotalCost,
        ["dsrequestcount"] = UsageFields.RequestCount,
        // —— 常见别名 / DOM 原始键 ——
        ["usagepercent"] = UsageFields.UsedPercent,
        ["usedpercent"] = UsageFields.UsedPercent,
        ["subscriptiontitle"] = UsageFields.SubscriptionTier,
        ["plantier"] = UsageFields.SubscriptionTier,
        ["totaltokenconsumed"] = UsageFields.UsedTokens,
        ["rankingpercent"] = UsageFields.UsageRankingPercent
    };

    /// <summary>
    /// 将历史 / 别名字段名解析为现用标准字段名。
    /// </summary>
    /// <param name="legacyName">旧字段名或别名（大小写、下划线不敏感）。</param>
    /// <returns>对应的现用标准字段名；若无别名记录则返回 <c>null</c>（调用方按原样处理）。</returns>
    public static string? Resolve(string? legacyName)
    {
        if (string.IsNullOrWhiteSpace(legacyName)) return null;
        var key = Normalize(legacyName);
        return Map.TryGetValue(key, out var current) ? current : null;
    }

    /// <summary>
    /// 判断某名称是否为已登记的历史别名。
    /// </summary>
    public static bool IsAlias(string? name) => !string.IsNullOrWhiteSpace(name) && Map.ContainsKey(Normalize(name!));

    /// <summary>
    /// 归一化字段名：去首尾空白并移除下划线，使 <c>mm_5h_used_percent</c> 与 <c>mm5husedpercent</c> 等价。
    /// </summary>
    private static string Normalize(string name) => name.Trim().Replace("_", string.Empty);
}
