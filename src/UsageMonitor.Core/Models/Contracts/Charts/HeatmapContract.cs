namespace UsageMonitor.Core.Models.Contracts.Charts;

/// <summary>
/// 热力图契约（req-101 B6）。
/// <para>声明热力图日期字段、数值字段与色阶设置（复用 <see cref="ColorTierSettings"/>）。</para>
/// </summary>
public class HeatmapContract
{
    /// <summary>日期字段名。</summary>
    public string DateField { get; set; } = string.Empty;

    /// <summary>数值字段名。</summary>
    public string ValueField { get; set; } = string.Empty;

    /// <summary>色阶设置（可空，null 时使用全局色阶）。</summary>
    public ColorTierSettings? ColorTiers { get; set; }
}
