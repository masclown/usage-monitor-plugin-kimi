namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B3：迷你图色阶配置（引用 req-009 UsageTierScale）。
/// <para>
/// 用 1-3 档色阶决定迷你图在不同用量区段的颜色。复用 Core 项目的 <see cref="Models.UsageTierConfig"/>
/// 色阶定义，避免重复实现。
/// </para>
/// <para>构造函数要求 <see cref="Tiers"/> 至少 1 档、最多 6 档。如需"使用全局色阶"语义，请传 <c>null</c> 到
/// <see cref="MiniChartDescriptor.ColorTier"/>，不要引用本类的静态 <see cref="Default"/>。</para>
/// </summary>
public sealed class MiniChartColorTier
{
    /// <summary>档位定义（按阈值升序，从低用量到高用量）。至少 1 档，最多 6 档。</summary>
    public IReadOnlyList<Models.UsageTierConfig> Tiers { get; }

    public MiniChartColorTier(IReadOnlyList<Models.UsageTierConfig> tiers)
    {
        if (tiers == null) throw new ArgumentNullException(nameof(tiers));
        if (tiers.Count < 1 || tiers.Count > 6)
            throw new ArgumentException($"色阶档位必须在 1-6 之间（实际：{tiers.Count}）", nameof(tiers));
        Tiers = tiers;
    }
}