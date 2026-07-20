using System.Collections.Generic;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 图表色板契约（REQ-082 SDK v2）。
/// <para>
/// 设计目标：把"颜色"从控件中解耦。
/// <list type="bullet">
/// <item>主题实现默认色板（含深/浅色切换）。</item>
/// <item>插件可覆盖（"插件为主"模式）。</item>
/// <item>支持动态 N 色分配，色阶不足时自动 fallback 到插件预定义色。</item>
/// </list>
/// </para>
/// </summary>
public interface IChartPalette
{
    /// <summary>
    /// 获取 N 个系列颜色（数量动态）。
    /// </summary>
    /// <param name="count">需要的颜色数量（&gt; 0）。</param>
    /// <param name="paletteHint">可选提示（如 "deepseek"），用于插件指定专属色板。</param>
    /// <returns>长度为 <paramref name="count"/> 的颜色字符串列表（"#RRGGBB" 格式）。</returns>
    IReadOnlyList<string> GetSeriesColors(int count, string? paletteHint = null);

    /// <summary>
    /// 获取度量条颜色（根据百分比和色阶规则）。
    /// </summary>
    /// <param name="percent">0-100 的用量百分比。</param>
    /// <param name="colorHint">可选提示（插件指定颜色），非空时优先返回。</param>
    /// <returns>颜色字符串（"#RRGGBB" 格式）。</returns>
    string GetMetricColor(double percent, string? colorHint = null);
}