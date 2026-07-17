namespace UsageMonitor.Core.Models;

/// <summary>
/// Taskbar 环形图中心数字可切换的指标类型（REQ-003）。
/// <para>
/// 字符串键值用于 <see cref="AppSettings.RingChartMetricOrder"/> 与 <c>RingChartControl.MetricKey</c>：
/// 序列化保持人类可读、跨语言兼容；枚举值仅供类型安全引用，禁止把数字写入配置。
/// </para>
/// <list type="bullet">
///   <item><description><c>Percent</c>：已用百分比（默认）</description></item>
///   <item><description><c>Credits</c>：积分余额（来自 <c>BalanceValueText / BalanceUnitText</c>）</description></item>
///   <item><description><c>WeeklyLimit</c>：周限额剩余百分比</description></item>
///   <item><description><c>RemainingQuota</c>：剩余用量（金额或 Token，由 Provider 决定）</description></item>
///   <item><description><c>ApiTokenUsed</c>：API Token 已消耗量</description></item>
/// </list>
/// 未来扩展：插件可注册自定义 <c>IRingMetricProvider</c>，使用自定义字符串键（如 <c>"plugin.minimax.creditsBurnt"</c>）；
/// 当前实现只覆盖上述 5 种内置值。
/// </summary>
public enum RingChartMetricKind
{
    /// <summary>已用百分比（默认）。</summary>
    Percent,
    /// <summary>积分余额。</summary>
    Credits,
    /// <summary>周限额剩余百分比。</summary>
    WeeklyLimit,
    /// <summary>剩余用量（金额或 Token）。</summary>
    RemainingQuota,
    /// <summary>API Token 已消耗量。</summary>
    ApiTokenUsed
}

/// <summary>
/// <see cref="RingChartMetricKind"/> 与字符串键的转换工具（REQ-003）。
/// </summary>
public static class RingChartMetricKeys
{
    /// <summary>5 种内置 metric 的字符串常量，供 <see cref="AppSettings.RingChartMetricOrder"/> 默认值与序列化兼容使用。</summary>
    public const string Percent = "Percent";
    public const string Credits = "Credits";
    public const string WeeklyLimit = "WeeklyLimit";
    public const string RemainingQuota = "RemainingQuota";
    public const string ApiTokenUsed = "ApiTokenUsed";

    /// <summary>出厂默认顺序（与需求文档 §1 一致），作为设置首次加载时的回退。</summary>
    public static readonly string[] DefaultOrder = { Percent, Credits, WeeklyLimit, RemainingQuota, ApiTokenUsed };

    /// <summary>把字符串键规范化为枚举；未知字符串（含插件自定义值）一律回退到 <see cref="RingChartMetricKind.Percent"/> 以避免空指针。</summary>
    public static RingChartMetricKind Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return RingChartMetricKind.Percent;
        if (Enum.TryParse<RingChartMetricKind>(key, ignoreCase: true, out var v)) return v;
        return RingChartMetricKind.Percent;
    }
}
