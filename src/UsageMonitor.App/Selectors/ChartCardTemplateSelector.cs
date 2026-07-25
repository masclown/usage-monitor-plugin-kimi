using System.Windows;
using System.Windows.Controls;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Selectors;

/// <summary>
/// 卡片模板选择器（REQ-083 SDK v2）。
/// <para>
/// 根据当前 DataContext（通常是 <c>ProviderUsageViewModel</c>）是否提供 V2 数据模型，
/// 自动选择旧模板（fallback）或新模板：
/// <list type="bullet">
/// <item><c>LimitBars</c>：<see cref="MetricBarData"/> 非空时用 <c>MetricBarTemplate</c>，否则用 <c>CardLimitBarsTemplate</c></item>
/// <item><c>Balance</c>：<see cref="MetricGridData"/> 非空时用 <c>MetricGridTemplate</c>，否则用 <c>CardBalanceTemplate</c></item>
/// </list>
/// </para>
/// <para>
/// 当前轮两个数据属性均为 null，因此 selector 永远走 fallback 路径，等价于原行为。
/// 后续 sprint 在 Provider 返回非 null 数据时自动切换到新模板，无需改动 XAML 调用方。
/// </para>
/// </summary>
public sealed class ChartCardTemplateSelector : DataTemplateSelector
{
    /// <summary>限额进度条模板选择模式。</summary>
    public enum SelectorKind { LimitBars, Balance }

    /// <summary>选择模式（由 XAML 通过构造函数注入）。</summary>
    public SelectorKind Kind { get; set; } = SelectorKind.LimitBars;

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (container is FrameworkElement fe)
        {
            // 检查 VM 是否提供 V2 数据
            var hasV2 = Kind switch
            {
                SelectorKind.LimitBars => GetMetricBarData(item) != null,
                // 声明式插件的 Number 图表已作为槽位并入图表区有序列表，余额快照区回退默认余额模板避免重复渲染。
                SelectorKind.Balance => !GetHasDeclarativeCardCharts(item)
                                        && (GetDeclarativeNumber(item) ?? GetMetricGridData(item)) is not null,
                _ => false
            };

            string templateKey = (Kind, hasV2) switch
            {
                (SelectorKind.LimitBars, true) => "MetricBarTemplate",
                (SelectorKind.LimitBars, false) => "CardLimitBarsTemplate",
                (SelectorKind.Balance, true) => "MetricGridTemplate",
                (SelectorKind.Balance, false) => "CardBalanceTemplate",
                _ => "CardLimitBarsTemplate"
            };

            return fe.TryFindResource(templateKey) as DataTemplate
                ?? fe.TryFindResource("CardLimitBarsTemplate") as DataTemplate;
        }
        return base.SelectTemplate(item, container);
    }

    /// <summary>通过反射安全获取 VM 的 <c>CardMetricBarData</c> 属性（避免在 App 层强引用 Core 的具体子类）。</summary>
    private static MetricBarData? GetMetricBarData(object? vm)
    {
        if (vm == null) return null;
        var prop = vm.GetType().GetProperty("CardMetricBarData");
        return prop?.GetValue(vm) as MetricBarData;
    }

    /// <summary>通过反射安全获取 VM 的 <c>CardMetricGridData</c> 属性。</summary>
    private static MetricGridData? GetMetricGridData(object? vm)
    {
        if (vm == null) return null;
        var prop = vm.GetType().GetProperty("CardMetricGridData");
        return prop?.GetValue(vm) as MetricGridData;
    }

    /// <summary>req-107 B8：声明式数字图表（Card 声明含 Number chart 时）。优先级高于 CardMetricGridData（默认余额快照），无声明时返回 null。</summary>
    private static MetricGridData? GetDeclarativeNumber(object? vm)
    {
        if (vm == null) return null;
        var prop = vm.GetType().GetProperty("DeclarativeNumber");
        return prop?.GetValue(vm) as MetricGridData;
    }

    /// <summary>反射读取 VM 的 <c>HasDeclarativeCardCharts</c> 属性（声明式插件判定，Number 图表已并入槽位）。</summary>
    private static bool GetHasDeclarativeCardCharts(object? vm)
    {
        if (vm == null) return false;
        var prop = vm.GetType().GetProperty("HasDeclarativeCardCharts");
        return prop?.GetValue(vm) is bool b && b;
    }
}