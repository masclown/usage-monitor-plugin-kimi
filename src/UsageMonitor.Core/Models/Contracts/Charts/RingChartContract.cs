namespace UsageMonitor.Core.Models.Contracts.Charts;

/// <summary>
/// 环形图契约（req-101 B6）。
/// <para>声明环形图百分比字段、中心显示字段与可选的警告/危险阈值。</para>
/// </summary>
public class RingChartContract
{
    /// <summary>百分比字段名（0-100）。</summary>
    public string PercentField { get; set; } = string.Empty;

    /// <summary>中心显示文本字段名（可空，如剩余额度/重置时间）。</summary>
    public string? CenterLabelField { get; set; }

    /// <summary>警告阈值（可空，达到后切换警告色）。</summary>
    public double? WarningThreshold { get; set; }

    /// <summary>危险阈值（可空，达到后切换危险色）。</summary>
    public double? DangerThreshold { get; set; }
}
