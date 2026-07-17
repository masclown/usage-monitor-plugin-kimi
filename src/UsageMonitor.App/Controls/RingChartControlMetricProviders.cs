using System.Collections.Generic;
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
                var v = string.IsNullOrWhiteSpace(vm.BalanceValueText) ? "--" : vm.BalanceValueText;
                var u = string.IsNullOrWhiteSpace(vm.BalanceUnitText) ? string.Empty : " " + vm.BalanceUnitText;
                return v + u;
            }),
            new DelegateMetricProvider(RingChartMetricKeys.WeeklyLimit, () =>
            {
                // 周限额"剩余"百分比 = 100 - 已用
                var remaining = System.Math.Max(0.0, 100.0 - vm.WeeklyBarPercent);
                return remaining.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }),
            new DelegateMetricProvider(RingChartMetricKeys.RemainingQuota, () =>
            {
                // ProviderUsageViewModel.RemainingText 已包含单位（"12.50 USD" / "剩余 45%" / "1.2K"）。
                // 取数原样返回，避免重复拼接。
                return string.IsNullOrWhiteSpace(vm.RemainingText) ? "--" : vm.RemainingText;
            }),
            new DelegateMetricProvider(RingChartMetricKeys.ApiTokenUsed, () =>
            {
                // ProviderUsageViewModel.UsedText 同样已包含单位（"{value:F2} {Unit}" 或 FormatTokens 输出的 "1.2M"）。
                return string.IsNullOrWhiteSpace(vm.UsedText) ? "--" : vm.UsedText;
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
