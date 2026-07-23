using System.Text.Json.Serialization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 用量信息模型 - 统一的AI服务用量数据结构
/// <para>
/// req-086-3.4：新增 <see cref="Quantity"/>（统一数量表示，兼容 UsedAmount/UsedTokens）与
/// <see cref="Error"/>（结构化错误，兼容 ErrorMessage）。旧字段标记 <c>[Obsolete]</c> 但保留，
/// 保证 JSON 序列化兼容旧配置与现有插件。
/// </para>
/// </summary>
public class UsageInfo
{
    /// <summary>服务商唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>服务商显示名称</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>req-109：账号 ID（req-109 拓展，多账号路由用）。null = 向后兼容（DisplayModule 用 FirstOrDefault 回退）。</summary>
    public string? AccountId { get; set; }

    /// <summary>req-109：卡片 ID（多卡片路由用）。null = 向后兼容。</summary>
    public string? CardId { get; set; }

    // ===================== req-086-3.4：新字段（推荐使用） =====================

    /// <summary>
    /// 统一数量表示（req-086-3.4）。非空时优先于 <see cref="UsedAmount"/>/<see cref="UsedTokens"/> 用于图表与展示。
    /// 旧插件可继续写 UsedAmount/UsedTokens，新插件建议直接写 Quantity。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Quantity? Quantity { get; set; }

    /// <summary>
    /// 结构化错误（req-086-3.4）。非空时表示查询失败，优先于 <see cref="ErrorMessage"/>。
    /// 与 <see cref="IsSuccess"/> 保持同步：设置 Error 时 IsSuccess 自动为 false。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UsageError? Error { get; set; }

    // ===================== 旧字段（[Obsolete] 但保留，向后兼容） =====================

    /// <summary>已用金额/额度（旧字段，推荐使用 <see cref="Quantity"/>）</summary>
    [Obsolete("请使用 Quantity 字段统一表示数量，保留仅为向后兼容", false)]
    public decimal UsedAmount { get; set; }

    /// <summary>总金额/额度（旧字段，推荐使用 <see cref="Quantity"/>）</summary>
    [Obsolete("请使用 Quantity 字段统一表示数量，保留仅为向后兼容", false)]
    public decimal TotalAmount { get; set; }

    /// <summary>金额单位（如 USD、CNY）（旧字段，推荐使用 <see cref="Quantity"/>）</summary>
    [Obsolete("请使用 Quantity 字段统一表示数量，保留仅为向后兼容", false)]
    public string Unit { get; set; } = "USD";

    /// <summary>已用Token数（旧字段，推荐使用 <see cref="Quantity"/>）</summary>
    [Obsolete("请使用 Quantity 字段统一表示数量，保留仅为向后兼容", false)]
    public long UsedTokens { get; set; }

    /// <summary>总Token数（-1表示不限制或未知）（旧字段，推荐使用 <see cref="Quantity"/>）</summary>
    [Obsolete("请使用 Quantity 字段统一表示数量，保留仅为向后兼容", false)]
    public long TotalTokens { get; set; } = -1;

    /// <summary>过期时间（null表示永不过期）</summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>扩展数据（用于存放插件特有的额外信息）</summary>
    public Dictionary<string, object> Extra { get; set; } = new();

    /// <summary>最后更新时间</summary>
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>是否查询成功</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>错误信息（查询失败时）（旧字段，推荐使用 <see cref="Error"/>）</summary>
    [Obsolete("请使用 Error 字段表示结构化错误，保留仅为向后兼容", false)]
    public string? ErrorMessage { get; set; }

    // ===================== 兼容访问器 =====================

    /// <summary>
    /// 获取金额使用百分比。优先使用 <see cref="Quantity"/>，否则回退到旧字段。
    /// </summary>
    public double GetUsagePercentage()
    {
#pragma warning disable CS0618 // 旧字段兼容访问
        if (Quantity.HasValue)
        {
            // Quantity 模式下：TotalAmount 仍由插件写入（或从 Quantity 推导）
            if (TotalAmount <= 0) return 0;
            return Math.Min(100, (double)(Quantity.Value.Value / TotalAmount * 100));
        }
        if (TotalAmount <= 0) return 0;
        return Math.Min(100, (double)(UsedAmount / TotalAmount * 100));
#pragma warning restore CS0618
    }

    /// <summary>
    /// 获取剩余额度。优先使用 <see cref="Quantity"/>，否则回退到旧字段。
    /// </summary>
    public decimal GetRemainingAmount()
    {
#pragma warning disable CS0618
        if (Quantity.HasValue)
            return Math.Max(0, TotalAmount - Quantity.Value.Value);
        return Math.Max(0, TotalAmount - UsedAmount);
#pragma warning restore CS0618
    }

    /// <summary>
    /// 获取剩余Token数。优先使用 <see cref="Quantity"/>（当单位为 TokenUnit 时），否则回退到旧字段。
    /// </summary>
    public long GetRemainingTokens()
    {
#pragma warning disable CS0618
        if (Quantity.HasValue && Quantity.Value.Unit is TokenUnit)
        {
            if (TotalTokens < 0) return -1;
            return Math.Max(0, TotalTokens - (long)Quantity.Value.Value);
        }
        if (TotalTokens < 0) return -1;
        return Math.Max(0, TotalTokens - UsedTokens);
#pragma warning restore CS0618
    }

    /// <summary>
    /// 获取用于系统托盘图标原生悬停提示（NotifyIcon.Text）的简短文本。
    /// 首分支（有总额度）返回的是"剩余"额度/百分比（MiniMax 为剩余百分比），故加"剩余"前缀，
    /// 与任务栏文字模式保持一致，避免被误读为已用；已用 tokens 分支为"已用"值故不加前缀。
    /// </summary>
    public string GetShortDisplayText()
    {
#pragma warning disable CS0618
        if (!IsSuccess) return $"{ProviderName}: 错误";

        if (TotalAmount > 0)
        {
            // 剩余百分比（Unit=="%"）取整显示、不带小数；其它单位（如货币 USD）仍保留两位小数
            var remainingText = Unit == "%"
                ? GetRemainingAmount().ToString("F0")
                : GetRemainingAmount().ToString("F2");
            return $"{ProviderName}: 剩余 {remainingText} {Unit}";
        }

        if (UsedTokens > 0)
            return $"{ProviderName}: {FormatTokenCount(UsedTokens)} tokens";

        return $"{ProviderName}: --";
#pragma warning restore CS0618
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
    /// req-005-011：由旧字段（<see cref="UsedAmount"/> + <see cref="Unit"/>）派生强类型 <see cref="Quantity"/>，
    /// 供各 Provider 在构建 UsageInfo 末尾统一调用，完成“输出写 Quantity”的过渡（旧字段仍保留）。
    /// <para>
    /// 单位映射刻意避开 <see cref="TokenUnit"/>（Token 计数走 <see cref="UsedTokens"/> / <see cref="GetRemainingTokens"/>），
    /// 且 Quantity.Value 恒等于 UsedAmount，保证 <see cref="GetUsagePercentage"/> 的 UsedAmount/TotalAmount 数学
    /// 与 <see cref="GetRemainingTokens"/> 分支均不变（零回归）。
    /// </para>
    /// </summary>
    public void PopulateQuantityFromLegacy()
    {
#pragma warning disable CS0618 // 过渡期读取旧字段
        UnitBase unit = Unit switch
        {
            "%" => new PercentUnit(),
            "USD" or "CNY" or "EUR" or "JPY" or "GBP" or "HKD" => new CurrencyUnit(Unit),
            null or "" => new UnknownUnit(),
            _ => new CreditUnit(Unit)   // "Credits" / "次" / "Tokens"(旧标签) / 其它自定义计数
        };
        Quantity = new Quantity(UsedAmount, unit);
#pragma warning restore CS0618
    }

    /// <summary>
    /// 创建一个错误状态的UsageInfo（旧签名，保留向后兼容）
    /// </summary>
    public static UsageInfo CreateError(string providerId, string providerName, string errorMessage)
    {
#pragma warning disable CS0618
        return new UsageInfo
        {
            ProviderId = providerId,
            ProviderName = providerName,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Error = UsageError.Unknown(errorMessage),
            LastUpdated = DateTime.Now
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// req-109：创建错误状态 UsageInfo（包含账号/卡片路由信息）。
    /// </summary>
    public static UsageInfo CreateError(string providerId, string providerName, string errorMessage, string? accountId, string? cardId)
    {
#pragma warning disable CS0618
        return new UsageInfo
        {
            ProviderId = providerId,
            ProviderName = providerName,
            AccountId = accountId,
            CardId = cardId,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Error = UsageError.Unknown(errorMessage),
            LastUpdated = DateTime.Now
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// 创建一个错误状态的UsageInfo（新签名，推荐）
    /// </summary>
    public static UsageInfo CreateError(string providerId, string providerName, UsageError error)
    {
        return new UsageInfo
        {
            ProviderId = providerId,
            ProviderName = providerName,
            IsSuccess = false,
            Error = error,
#pragma warning disable CS0618
            ErrorMessage = error.Message,
#pragma warning restore CS0618
            LastUpdated = DateTime.Now
        };
    }
}
