using System;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 图表宿主主题契约（REQ-005 SDK）。颜色用 object 携带（Core 不依赖 WPF）。
/// <para>
/// 实际使用时 WPF 宿主会在 adapter 层把 Brush 装箱为 object，
/// 控件实现端通过类型判断还原为 <c>System.Windows.Media.Brush</c>。
/// 这样 Core SDK 与 WPF 解耦，二期（k2）拆 NuGet 时本接口可直接作为公共契约。
/// </para>
/// </summary>
public interface IChartTheme
{
    /// <summary>低用量色（&lt;60%）。可由宿主装箱 WPF Brush 或任何 IBrush-like 类型。</summary>
    object LowBrush { get; }

    /// <summary>中用量色（60~85%）。</summary>
    object MidBrush { get; }

    /// <summary>高用量色（&gt;85%）。</summary>
    object HighBrush { get; }

    /// <summary>背景轨道色（柱状 / 折线 / 圆环底环）。</summary>
    object TrackBrush { get; }

    /// <summary>轴 / 标签文字色。</summary>
    object TextBrush { get; }
}

/// <summary>
/// 通用图表抽象（REQ-005 SDK）。
/// <para>
/// 一期（k1）约束：宿主调用 <see cref="Bind"/> 注入数据与主题，控件据此重渲；
/// 二期（k2）拆 NuGet 后此接口作为公共契约保持稳定。
/// </para>
/// </summary>
public interface IUsageChart
{
    /// <summary>图表种类（与 <see cref="IChartData.Kind"/> 一一对应）。</summary>
    ChartKind Kind { get; }

    /// <summary>WPF 控件类型，供宿主用反射 / DataTemplate 绑定（不用强引用 WPF 程序集时取 typeof()）。</summary>
    Type ControlType { get; }

    /// <summary>人类可读的展示名（"折线图 / Line Chart"），供插件配置窗口 / 日志显示。</summary>
    string DisplayName { get; }

    /// <summary>由宿主在创建 / 数据更新时调用，注入当前展示的数据 + 主题；控件据此重渲。</summary>
    /// <param name="data">强类型图表数据（实现 <see cref="IChartData"/>）。</param>
    /// <param name="theme">当前主题；为 null 时控件沿用最近一次主题或自身默认色。</param>
    void Bind(IChartData data, IChartTheme? theme);
}

/// <summary>
/// 图表工厂：宿主按 <see cref="Kind"/> 注册的"图表实例化器"（REQ-005 SDK）。
/// <para>
/// 默认实现见 <c>UsageMonitor.Core.Charts</c> 下的 5 个内置工厂。
/// 插件可在自己的 <c>IUsageProvider</c> 实现里追加 <c>ChartFactories</c>，宿主在装配时合并。
/// </para>
/// </summary>
public interface IUsageChartFactory
{
    /// <summary>本工厂生产的图表种类。</summary>
    ChartKind Kind { get; }

    /// <summary>创建图表实例（宿主负责 UI 生命周期）。</summary>
    IUsageChart Create();
}

/// <summary>
/// 图表工厂 v2（REQ-082 SDK v2）：接收 <see cref="ChartContext"/> 以适配不同展示位置。
/// <para>
/// 保留 <see cref="IUsageChartFactory"/> 不变以兼容现有 5 个内置工厂，
/// 新控件（StackedBar / Area / Grouped / MetricBar / MetricGrid）实现本接口。
/// 宿主优先检测实现方是否为本接口，是则传入 context；否则 fallback 到 <see cref="IUsageChartFactory.Create"/>。
/// </para>
/// </summary>
public interface IUsageChartFactory2 : IUsageChartFactory
{
    /// <summary>创建图表实例，接收 <see cref="ChartContext"/> 以适配不同展示位置。</summary>
    /// <param name="context">运行时上下文（位置/尺寸/主题/色板）。</param>
    IUsageChart Create(ChartContext context);
}