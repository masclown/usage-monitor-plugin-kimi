namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B8：迷你图循环切换配置。
/// <para>
/// 当 Taskbar 内有多个 Provider 的迷你图时，按 <see cref="IntervalSeconds"/> 间隔循环显示，
/// 避免水平空间紧张时迷你图被裁剪。参考 req-003 sticky timer 默认 5 秒。
/// </para>
/// </summary>
public sealed class MiniChartCycleConfig
{
    /// <summary>是否启用循环切换。false（默认）= 全部并排显示。</summary>
    public bool Enabled { get; init; }

    /// <summary>切换间隔（秒）。建议 3-10 秒，过短用户看不清，过长失去切换意义。默认 5 秒。</summary>
    public int IntervalSeconds { get; init; } = 5;

    /// <summary>不循环切换（默认）。</summary>
    public static MiniChartCycleConfig Disabled { get; } = new() { Enabled = false };

    /// <summary>5 秒间隔循环切换。</summary>
    public static MiniChartCycleConfig Default5s { get; } = new() { Enabled = true, IntervalSeconds = 5 };

    /// <summary>构造校验：间隔必须在 1-60 秒之间。</summary>
    public MiniChartCycleConfig()
    {
        if (Enabled && (IntervalSeconds < 1 || IntervalSeconds > 60))
            throw new ArgumentException($"循环间隔必须在 1-60 秒之间（实际：{IntervalSeconds}）");
    }
}