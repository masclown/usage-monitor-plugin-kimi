using System.Collections.Generic;
using System.Linq;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Controls;

/// <summary>
/// REQ-003 内置 5 种 Taskbar 环形图中心数字 metric provider 工厂。
/// <para>
/// 控件 <see cref="RingChartControl"/> 在 DataContext 变为 <see cref="ProviderUsageViewModel"/> 时
/// 自动调用 <see cref="BuildDefault"/> 装配 5 个 <see cref="IRingMetricProvider"/>，每个实现均
/// 把 <c>GetText()</c> 委托到 VM 的当前字段上（即时反映刷新数据，无需事件通知）。
/// </para>
/// <para>
/// 未提供数据（字段为 0 / 空字符串 / "--"）时，文本仍可返回该占位，便于 hover tooltip 区分
/// "未取到数据"与 "数据 = 0"。插件可覆盖逻辑。 </para>
/// </summary>
public static class RingChartControlMetricProviders
{
    /// <summary>基于指定 <see cref="ProviderUsageViewModel"/> 构造 5 个内置 IRingMetricProvider（Percent/Credits/WeeklyLimit/RemainingQuota/ApiTokenUsed）。</summary>
    public static IReadOnlyList<IRingMetricProvider> BuildDefault(ProviderUsageViewModel vm)
    {
        return new IRingMetricProvider[]
        {
            new DelegateMetricProvider(RingChartMetricKeys.Percent, () =>
            {
                var p = vm.UsagePercentage;
                return p == 0 ? "0%" : p.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }),
            new DelegateMetricProvider(RingChartMetricKeys.Credits, () =>
            {
                // req-051：从 BalanceItems 集合中找"积分余额"项（默认 4 项之一）的 Value。
                // 无该项时（如失败场景）回退为 "-"。
                var credits = vm.BalanceItems.FirstOrDefault(b =>
                    string.Equals(b.Label, "积分余额", System.StringComparison.OrdinalIgnoreCase));
                var v = credits == null || string.IsNullOrWhiteSpace(credits.Value) ? "-" : credits.Value;
                return v;
            }),
            new DelegateMetricProvider(RingChartMetricKeys.WeeklyLimit, () =>
            {
                // 周限额"剩余"百分比 = 100 - 已用
                var remaining = System.Math.Max(0.0, 100.0 - vm.WeeklyBarPercent);
                return remaining.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }),
            new DelegateMetricProvider(RingChartMetricKeys.RemainingQuota, () =>
            {
                // req-051：ProviderUsageViewModel.RemainingText 已包含单位。
                // 无数据时显示 "-"。
                return string.IsNullOrWhiteSpace(vm.RemainingText) ? "-" : vm.RemainingText;
            }),
            new DelegateMetricProvider(RingChartMetricKeys.ApiTokenUsed, () =>
            {
                // req-051：ProviderUsageViewModel.UsedText 同样已包含单位。
                // 无数据时显示 "-"。
                return string.IsNullOrWhiteSpace(vm.UsedText) ? "-" : vm.UsedText;
            }),
        };
    }

    /// <summary>REQ-003：把一个无参取文本方法包装为 IRingMetricProvider（避免在多处重复类）。</summary>
    private sealed class DelegateMetricProvider : IRingMetricProvider
    {
        private readonly System.Func<string> _getter;
        public DelegateMetricProvider(string key, System.Func<string> getter)
        {
            Key = key;
            _getter = getter;
        }
        public string Key { get; }
        public string GetText() => _getter();
    }
}
