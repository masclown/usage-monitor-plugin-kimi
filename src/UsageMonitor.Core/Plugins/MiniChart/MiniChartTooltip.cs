namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B9：迷你图 Tooltip 模板配置。
/// <para>
/// hover 迷你图时显示的 Tooltip 模板。模板字符串支持 <c>{ProviderName}</c> / <c>{Value}</c> /
/// <c>{Timestamp}</c> / <c>{Percent}</c> 占位符，渲染时由宿主替换。
/// </para>
/// <para>参考 req-002/034/046 的 Tooltip 模式：宿主在 DataTemplate 中用 Binding 渲染，
/// 本类仅提供模板字符串 + 可选格式化委托。</para>
/// </summary>
public sealed class MiniChartTooltip
{
    /// <summary>标题模板（如 "MiniMax · 已用 {Percent}%"）。</summary>
    public string? TitleTemplate { get; init; }

    /// <summary>正文模板（如 "5h 重置：{ResetTime}\n本周已用：{Value}%"）。</summary>
    public string? BodyTemplate { get; init; }

    /// <summary>显示延迟（毫秒）。默认 0 = 立即显示。负数表示禁用 Tooltip。</summary>
    public int ShowDelayMs { get; init; } = 0;

    /// <summary>无 Tooltip（默认）。</summary>
    public static MiniChartTooltip None { get; } = new() { ShowDelayMs = -1 };

    /// <summary>默认 Tooltip：显示 Provider 名称 + 当前百分比。</summary>
    public static MiniChartTooltip Default { get; } = new()
    {
        TitleTemplate = "{ProviderName}",
        BodyTemplate = "已用 {Percent}%",
        ShowDelayMs = 200
    };
}