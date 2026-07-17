using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 单个用量档位的可配置定义（阈值 + 颜色 + 是否启用）。
/// <para>
/// 由用户/设置项控制：通过 <see cref="AppSettings.UsageTierConfig"/> 持久化，
/// 由 <c>UsageTierScale</c> 在运行时读取并应用到所有按百分比换色的进度条 / 图表 / 单元。
/// </para>
/// <para>
/// 字段语义：
/// <list type="bullet">
///   <item><description><see cref="MinPercent"/>：下界（含）。已用百分比 ≥ 该值即命中此档（在更高档未命中时）。</description></item>
///   <item><description><see cref="ColorArgb"/>：ARGB 32 位整数（0xAARRGGBB）；UI 序列化为 #AARRGGBB 字符串便于编辑。</description></item>
///   <item><description><see cref="IsEnabled"/>：是否参与选色。禁用档仍可保留在配置中（用户临时"关闭"某一档），UI 上以低饱和灰显示。</description></item>
/// </list>
/// </para>
/// </summary>
public class UsageTierConfig
{
    /// <summary>下界（含）。已用百分比 ≥ 该值即命中此档。</summary>
    public double MinPercent { get; set; }

    /// <summary>
    /// ARGB 32 位整数（0xAARRGGBB）。UI 序列化为 #AARRGGBB 字符串便于编辑。
    /// 默认不透明（Alpha = 0xFF）。
    /// </summary>
    public uint ColorArgb { get; set; }

    /// <summary>是否参与选色（true = 启用，false = 禁用）。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>出厂默认：低（绿）。</summary>
    public static UsageTierConfig LowDefault()
        => new() { MinPercent = 0, ColorArgb = 0xFF22C55E, IsEnabled = true };

    /// <summary>出厂默认：注意（金 #FACD14）。</summary>
    public static UsageTierConfig NoticeDefault()
        => new() { MinPercent = 50, ColorArgb = 0xFFFACD14, IsEnabled = true };

    /// <summary>出厂默认：中（橙）。</summary>
    public static UsageTierConfig MidDefault()
        => new() { MinPercent = 60, ColorArgb = 0xFFF59E0B, IsEnabled = true };

    /// <summary>出厂默认：高（红）。</summary>
    public static UsageTierConfig HighDefault()
        => new() { MinPercent = 85, ColorArgb = 0xFFEF4444, IsEnabled = true };

    /// <summary>出厂默认档位集合（升序：低/注意/中/高）。</summary>
    public static List<UsageTierConfig> Defaults()
        => new() { LowDefault(), NoticeDefault(), MidDefault(), HighDefault() };
}