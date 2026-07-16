using System.Collections.Generic;
using System.Linq;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 用量分档色阶（单一数据源 / Single Source of Truth）。
/// <para>
/// 所有“按已用百分比换色”的地方——主界面各进度条、托盘悬浮窗、迷你折线图、年度热力图、历史窗口——
/// 都从这里取色。<b>需要新增 / 删除档位，或调整阈值、颜色时，只改本文件的 <see cref="Tiers"/> 表即可全局生效。</b>
/// </para>
/// <para>
/// 新增一档的步骤：① 在 <see cref="Tiers"/> 里按 MinPercent 升序插入一行；
/// ② 在 Themes/Tokens.xaml 补一个同名的 SolidColorBrush 资源（键名与 ResourceKey 一致）。
/// 删除一档：删掉对应行即可。调整阈值 / 配色：改该行的 MinPercent 或 ResourceKey / FallbackColor。
/// </para>
/// </summary>
public static class UsageTierScale
{
    /// <summary>
    /// 单个档位定义。
    /// </summary>
    /// <param name="MinPercent">下界（含）。已用百分比 ≥ 该值即命中此档（在更高档未命中时）。</param>
    /// <param name="ResourceKey">主题资源键（对应 Tokens.xaml 中的 SolidColorBrush）；优先取主题色，缺失时回退 <paramref name="FallbackColor"/>。</param>
    /// <param name="FallbackColor">主题资源缺失时的兜底颜色（ARGB）。</param>
    public sealed record Tier(double MinPercent, string ResourceKey, Color FallbackColor);

    /// <summary>
    /// 档位表：<b>必须按 MinPercent 升序排列</b>。
    /// 低（绿）→ 注意（金 #facd14）→ 中（橙）→ 高（红）。
    /// </summary>
    public static readonly IReadOnlyList<Tier> Tiers = new[]
    {
        new Tier(0,  "UsageLowBrush",    Color.FromRgb(0x22, 0xC5, 0x5E)), // 低：绿
        new Tier(50, "UsageNoticeBrush", Color.FromRgb(0xFA, 0xCD, 0x14)), // 注意：金黄 #facd14
        new Tier(60, "UsageMidBrush",    Color.FromRgb(0xF5, 0x9E, 0x0B)), // 中：橙
        new Tier(85, "UsageHighBrush",   Color.FromRgb(0xEF, 0x44, 0x44)), // 高：红
    };

    /// <summary>兜底画笔缓存（仅 UI 线程访问；避免热力图逐格重复创建画笔）。</summary>
    private static readonly Dictionary<string, SolidColorBrush> _fallbackCache = new();

    /// <summary>
    /// 按已用百分比命中对应档位：取“下界不超过 percent 的最高一档”。
    /// </summary>
    /// <param name="percent">已用百分比（0-100；越界不影响命中逻辑）。</param>
    /// <returns>命中的档位（percent 低于所有阈值时返回首档）。</returns>
    public static Tier Resolve(double percent)
    {
        var hit = Tiers[0];
        foreach (var tier in Tiers)
        {
            if (percent >= tier.MinPercent) hit = tier;
            else break; // Tiers 已升序，后续 MinPercent 只会更大，无需再比
        }
        return hit;
    }

    /// <summary>
    /// 按已用百分比取画笔：优先当前主题资源，缺失时回退内置兜底色（均已 Freeze）。
    /// </summary>
    /// <param name="percent">已用百分比（0-100）。</param>
    public static Brush ResolveBrush(double percent) => GetBrush(Resolve(percent));

    /// <summary>
    /// 取指定档位的画笔（供图例等“按档位枚举取色”的场景）。
    /// </summary>
    /// <param name="tier">档位定义。</param>
    public static Brush GetBrush(Tier tier)
    {
        if (Application.Current?.TryFindResource(tier.ResourceKey) is Brush themed)
            return themed;
        return GetFallbackBrush(tier);
    }

    /// <summary>
    /// 按主题资源键直接取画笔（供转换器的显式档位参数 low / mid / high 使用）。
    /// </summary>
    /// <param name="resourceKey">资源键（如 UsageLowBrush）。</param>
    /// <returns>命中档位则取其画笔；否则尝试主题资源，仍缺失时回退首档兜底色。</returns>
    public static Brush GetBrushByKey(string resourceKey)
    {
        var tier = Tiers.FirstOrDefault(t => t.ResourceKey == resourceKey);
        if (tier != null) return GetBrush(tier);
        return Application.Current?.TryFindResource(resourceKey) as Brush
               ?? GetFallbackBrush(Tiers[0]);
    }

    /// <summary>创建 / 缓存并冻结档位的兜底画笔。</summary>
    /// <param name="tier">档位定义。</param>
    private static SolidColorBrush GetFallbackBrush(Tier tier)
    {
        if (!_fallbackCache.TryGetValue(tier.ResourceKey, out var brush))
        {
            brush = new SolidColorBrush(tier.FallbackColor);
            brush.Freeze();
            _fallbackCache[tier.ResourceKey] = brush;
        }
        return brush;
    }
}
