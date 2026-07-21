namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B1：迷你图描述符 DTO（插件注册载体）。
/// <para>
/// 5 个核心字段：
/// <list type="number">
///   <item><description><see cref="ProviderId"/>：唯一标识（对应 IUsageProvider.ProviderId）。</description></item>
///   <item><description><see cref="Kind"/>：图类型枚举（决定 DataTemplateSelector 选择哪个模板）。</description></item>
///   <item><description><see cref="Style"/>：视觉样式（紧凑 / 详细 / 浅色 / 深色）。</description></item>
///   <item><description><see cref="ColorTier"/>：色阶配置（复用 req-009 UsageTierScale）。</description></item>
///   <item><description><see cref="DataSource"/>：数据源（具体类型取决于 Kind，如 double 用量百分比 / IReadOnlyList&lt;double&gt; 时间序列）。</description></item>
/// </list>
/// 可选字段：
/// <list type="bullet">
///   <item><description><see cref="CycleConfig"/>：循环切换配置（默认 5 秒）。</description></item>
///   <item><description><see cref="Tooltip"/>：hover Tooltip 模板。</description></item>
/// </list>
/// </para>
/// <para>设计动机：Taskbar 当前每个 Provider 通过单独 VM + 模板硬编码，新增 Provider 必须改
/// TaskbarWindow.xaml。统一为 DTO + 注册中心后，Taskbar 渲染层只需遍历 Registry.GetAll()，
/// 插件差异通过 descriptor 字段表达。</para>
/// </summary>
public sealed class MiniChartDescriptor
{
    /// <summary>Provider 唯一标识（必填，对应 IUsageProvider.ProviderId）。</summary>
    public required string ProviderId { get; init; }

    /// <summary>图类型枚举（必填）。</summary>
    public required MiniChartKind Kind { get; init; }

    /// <summary>视觉样式（默认 Compact）。</summary>
    public MiniChartStyle Style { get; init; } = MiniChartStyle.Compact;

    /// <summary>色阶配置（默认沿用 req-009 全局色阶）。</summary>
    public MiniChartColorTier? ColorTier { get; init; }

    /// <summary>
    /// 数据源（object 装箱，因为不同图类型的数据结构差异大）。
    /// <list type="bullet">
    ///   <item><description>MiniRingChart / MiniText：<c>double?</c>（0-100 用量百分比）</description></item>
    ///   <item><description>MiniLineChart：<c>IReadOnlyList&lt;double&gt;</c>（历史时间序列）</description></item>
    ///   <item><description>MiniBarChart / MiniHeatMap：自定义结构（见各自 Factory）</description></item>
    /// </list>
    /// </summary>
    public object? DataSource { get; init; }

    /// <summary>循环切换配置（默认不循环）。</summary>
    public MiniChartCycleConfig? CycleConfig { get; init; }

    /// <summary>Tooltip 模板（默认显示 Provider 名 + 百分比）。</summary>
    public MiniChartTooltip? Tooltip { get; init; }

    /// <summary>
    /// 辅助构造：用最常用字段快速构造一个 Text 类型描述符。
    /// </summary>
    public static MiniChartDescriptor ForText(string providerId, double? usagePercent = null)
        => new()
        {
            ProviderId = providerId,
            Kind = MiniChartKind.MiniText,
            DataSource = usagePercent,
            Style = MiniChartStyle.Compact,
            Tooltip = MiniChartTooltip.Default
        };

    /// <summary>
    /// 辅助构造：快速构造一个 RingChart 类型描述符。
    /// </summary>
    public static MiniChartDescriptor ForRingChart(string providerId, double? usagePercent = null)
        => new()
        {
            ProviderId = providerId,
            Kind = MiniChartKind.MiniRingChart,
            DataSource = usagePercent,
            Style = MiniChartStyle.Compact,
            // 传 null 而非 MiniChartColorTier.Default，让渲染层走全局 UsageTierScale 色阶
            ColorTier = null,
            Tooltip = MiniChartTooltip.Default
        };
}