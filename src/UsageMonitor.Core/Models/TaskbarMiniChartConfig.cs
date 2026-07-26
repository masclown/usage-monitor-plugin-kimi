using UsageMonitor.Core.Plugins.MiniChart;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-098：用户对单个 Provider 任务栏迷你图表的配置 DTO。
/// <para>
/// 按 ProviderId 索引持久化在 <see cref="AppSettings.TaskbarMiniChartConfigs"/>。
/// 每个字段都提供默认值，未配置时 <c>MiniChartRegistryBootstrapper</c> 走默认注册路径。
/// </para>
/// <para>
/// 设计动机：req-088 B5 仅插件硬编码 descriptor，用户无法切换。
/// 本类把"是否显示 / 图类型 / 内容 / Logo"四要素下放到用户配置层，
/// 插件继续负责"我能产哪些内容（<see cref="IUsageProvider.SupportedMiniCharts"/> +
/// <see cref="IUsageProvider.MiniChartDataTypes"/>）"，宿主按配置 + 声明合并出最终 descriptor。
/// </para>
/// </summary>
public class TaskbarMiniChartConfig
{
    /// <summary>是否在任务栏显示。false 时 <c>MiniChartRegistryBootstrapper</c> 跳过注册。</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>图表类型（半圆环 / 文字 / 折线等）。默认 <see cref="MiniChartKind.MiniRingChart"/>。</summary>
    public MiniChartKind ChartKind { get; set; } = MiniChartKind.MiniRingChart;

    /// <summary>主显示内容。默认 <see cref="MiniChartContentKind.PrimaryMetric"/>（已用百分比）。</summary>
    public MiniChartContentKind ContentKind { get; set; } = MiniChartContentKind.PrimaryMetric;

    /// <summary>副显示内容（可选）。null 表示不显示副内容。</summary>
    public MiniChartContentKind? SecondaryKind { get; set; }

    /// <summary>是否显示 Provider Logo。默认 true。</summary>
    public bool ShowLogo { get; set; } = true;

    /// <summary>用户覆盖的图表宽度（DIP，40-400 有效；null = 使用插件声明值或宿主默认 120）。</summary>
    public int? Width { get; set; }
}