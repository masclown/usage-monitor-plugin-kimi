using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-026：环形图中心数字 metric 解析器。
/// <para>
/// 给定 <see cref="AppSettings"/> + providerId，按"先 Provider 单独配置 → 后全局默认"优先级
/// 返回该 Provider 已启用的 metric key 集合。如果 Provider 单独配置为空列表（用户全部取消勾选），
/// 返回空集合（保持用户意图，**不回退**到全局默认）。
/// </para>
/// <para>
/// 入口：
/// <list type="bullet">
/// <item><description><see cref="GetEnabledMetrics"/>：返回已启用的 key 集合（用于 RingChartControl.EnabledMetrics）</description></item>
/// <item><description><see cref="IsMetricEnabled"/>：单 metric 是否启用（用于控制显浅灰 vs 正常色）</description></item>
/// </list>
/// </para>
/// </summary>
public static class RingChartMetricResolver
{
    /// <summary>
    /// 解析某 Provider 已启用的 metric key 集合。
    /// </summary>
    public static IReadOnlyList<string> GetEnabledMetrics(AppSettings settings, string providerId)
    {
        if (settings == null) return Array.Empty<string>();
        if (!string.IsNullOrEmpty(providerId) &&
            settings.ProviderEnabledRingChartMetrics.TryGetValue(providerId, out var list) &&
            list != null)
        {
            return list;
        }
        return settings.GlobalEnabledRingChartMetrics ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>
    /// 给定 metric key + 已启用集合，判断该 metric 当前是否启用。
    /// </summary>
    public static bool IsMetricEnabled(IReadOnlyList<string> enabledMetrics, string metricKey)
    {
        if (enabledMetrics == null || string.IsNullOrEmpty(metricKey)) return false;
        return enabledMetrics.Any(m => string.Equals(m, metricKey, StringComparison.OrdinalIgnoreCase));
    }
}