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
    /// <summary>基于指定 <see cref="ProviderUsageViewModel"/> 构造 5 个内置 IRingMetricProvider（Percent/Credits/WeeklyLimit/RemainingQuota/ApiTokenUsed）。
    /// <para>req-093：每个 provider 同时附带 <c>IsInverted</c> 与阈值元信息——</para>
    /// <list type="bullet">
    ///   <item><description>Percent（已用百分比）：<c>IsInverted=false</c>，阈值 60/85（高%危险）</description></item>
    ///   <item><description>Credits（积分余额，剩余量）：<c>IsInverted=true</c>，阈值 30/10（低%危险）</description></item>
    ///   <item><description>WeeklyLimit（周限额剩余百分比）：<c>IsInverted=true</c>，阈值 40/20（低%危险）</description></item>
    ///   <item><description>RemainingQuota（剩余用量）：<c>IsInverted=true</c>，阈值 30/10</description></item>
    ///   <item><description>ApiTokenUsed（API Token 已消耗）：<c>IsInverted=true</c>，阈值 30/10</description></item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<IRingMetricProvider> BuildDefault(ProviderUsageViewModel vm)
    {
        return new IRingMetricProvider[]
        {
            new DelegateMetricProvider(RingChartMetricKeys.Percent,
                getText: () =>
                {
                    var p = vm.UsagePercentage;
                    return p == 0 ? "0%" : p.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
                },
                getPercent: () => vm.UsagePercentage,
                isInverted: false,
                warningThreshold: 60.0,
                dangerThreshold: 85.0),
            new DelegateMetricProvider(RingChartMetricKeys.Credits,
                getText: () =>
                {
                    // req-051：从 BalanceItems 集合中找"积分余额"项（默认 4 项之一）的 Value。
                    // 无该项时（如失败场景）回退为 "-"。
                    var credits = vm.BalanceItems.FirstOrDefault(b =>
                        string.Equals(b.Label, "积分余额", System.StringComparison.OrdinalIgnoreCase));
                    var v = credits == null || string.IsNullOrWhiteSpace(credits.Value) ? "-" : credits.Value;
                    return v;
                },
                // Credits 不映射到 Percentage（文本值），但 req-094 需要非零值避免动画跳变；回退 0。
                getPercent: () => 0.0,
                isInverted: true,        // 剩余量越低越危险
                warningThreshold: 30.0,  // 剩余低于 30% 警告
                dangerThreshold: 10.0),  // 剩余低于 10% 危险
            new DelegateMetricProvider(RingChartMetricKeys.WeeklyLimit,
                getText: () =>
                {
                    // 周限额"剩余"百分比 = 100 - 已用
                    var remaining = System.Math.Max(0.0, 100.0 - vm.WeeklyBarPercent);
                    return remaining.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
                },
                getPercent: () => System.Math.Max(0.0, 100.0 - vm.WeeklyBarPercent),
                isInverted: true,        // 剩余量越低越危险
                warningThreshold: 40.0,  // 剩余低于 40% 警告
                dangerThreshold: 20.0),  // 剩余低于 20% 危险
            new DelegateMetricProvider(RingChartMetricKeys.RemainingQuota,
                getText: () =>
                {
                    // req-051：ProviderUsageViewModel.RemainingText 已包含单位。
                    // 无数据时显示 "-"。
                    return string.IsNullOrWhiteSpace(vm.RemainingText) ? "-" : vm.RemainingText;
                },
                // RemainingQuota 是文本值，无百分比含义，回退 0 保持弧度不变。
                getPercent: () => 0.0,
                isInverted: true,
                warningThreshold: 30.0,
                dangerThreshold: 10.0),
            new DelegateMetricProvider(RingChartMetricKeys.ApiTokenUsed,
                getText: () =>
                {
                    // req-051：ProviderUsageViewModel.UsedText 同样已包含单位。
                    // 无数据时显示 "-"。
                    return string.IsNullOrWhiteSpace(vm.UsedText) ? "-" : vm.UsedText;
                },
                getPercent: () => 0.0,
                isInverted: true,
                warningThreshold: 30.0,
                dangerThreshold: 10.0),
        };
    }

    /// <summary>REQ-003：把"取文本方法 + 色阶元数据"包装为 IRingMetricProvider（避免在多处重复类）。req-093：扩展支持元信息参数。</summary>
    private sealed class DelegateMetricProvider : IRingMetricProvider
    {
        private readonly System.Func<string> _getter;
        private readonly System.Func<double> _getPercent;
        private readonly bool _isInverted;
        private readonly double _warningThreshold;
        private readonly double _dangerThreshold;

        public DelegateMetricProvider(string key,
            System.Func<string> getText,
            System.Func<double> getPercent,
            bool isInverted,
            double warningThreshold,
            double dangerThreshold)
        {
            Key = key;
            _getter = getText;
            _getPercent = getPercent;
            _isInverted = isInverted;
            _warningThreshold = warningThreshold;
            _dangerThreshold = dangerThreshold;
        }
        public string Key { get; }
        public string GetText() => _getter();
        public double GetPercent() => _getPercent();
        public bool IsInverted => _isInverted;
        public double? GetWarningThreshold() => _warningThreshold;
        public double? GetDangerThreshold() => _dangerThreshold;
    }
}
