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

    /// <summary>
    /// req-109：Mini 图表唯一 ID（对应 <c>taskbar.miniCharts[].chartId</c>，如 "mm.mini.ring"）。
    /// <para>null = 旧注册路径（未按 mini chart 声明注册，向后兼容）；非 null 时供渲染端按 chartId 精确过滤。</para>
    /// </summary>
    public string? ChartId { get; init; }

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
    /// req-098：主显示内容。决定 <c>MiniChartItemViewModel</c> 从
    /// <see cref="UsageMonitor.Core.Plugins.IUsageProvider"/> 拉哪种数据填充主区域。
    /// 默认 <see cref="MiniChartContentKind.PrimaryMetric"/>。
    /// </summary>
    public MiniChartContentKind ContentKind { get; init; } = MiniChartContentKind.PrimaryMetric;

    /// <summary>
    /// req-098：副显示内容（可选）。null 表示不显示副内容。典型用法：
    /// <c>ContentKind = PrimaryMetric</c> + <c>SecondaryKind = ResetTime</c> → 半圆环图旁附重置倒计时。
    /// </summary>
    public MiniChartContentKind? SecondaryKind { get; init; }

    /// <summary>
    /// req-098：是否显示 Provider Logo。false 时宿主应隐藏图标只显示数字 / 文本。
    /// 默认 true。
    /// </summary>
    public bool ShowLogo { get; init; } = true;

    /// <summary>
    /// req-098：插件声明的迷你图表 Tooltip 字段列表（<c>ProviderName / DataName / CurrentValue / RefreshCountdown</c>）。
    /// <para>
    /// 默认 4 项全开，向后兼容。宿主在 <c>MiniChartItemViewModel.ResolveTooltipTemplate</c>
    /// 中按此列表从模板字符串剔除未启用字段（未来扩展点，本批次未实现剔除逻辑）。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ToolTipFields { get; init; } = new[]
    {
        "ProviderName",
        "DataName",
        "CurrentValue",
        "RefreshCountdown"
    };

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