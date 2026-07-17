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
/// 所有"按已用百分比换色"的地方——主界面各进度条、托盘悬浮窗、迷你折线图、年度热力图、历史窗口——
/// 都从这里取色。<b>需要新增 / 删除档位，或调整阈值、颜色时，可在设置页"用量色阶" Tab 实时修改，
/// 也可改本文件的 <see cref="BuiltInDefaults"/> 修改出厂默认。</b>
/// </para>
/// <para>
/// 运行时会话内可调用 <see cref="ApplyConfig"/> 重新加载档位（按 MinPercent 升序），随后触发
/// <see cref="TierChanged"/> 事件：订阅方可调用 <c>InvalidateVisual</c> / 触发 <c>PropertyChanged</c>
/// 让绑定到 Brush 的 UI 重新取色。
/// </para>
/// </summary>
public static class UsageTierScale
{
    /// <summary>
    /// 单个档位的运行时定义（不可变快照）。
    /// </summary>
    /// <param name="MinPercent">下界（含）。已用百分比 ≥ 该值即命中此档（在更高档未命中时）。</param>
    /// <param name="Color">档位颜色（含 Alpha）。</param>
    public sealed record Tier(double MinPercent, Color Color);

    /// <summary>出厂默认档位（升序：低绿 / 注意金 #FACD14 / 中橙 / 高红）。</summary>
    public static readonly IReadOnlyList<Tier> BuiltInDefaults = new[]
    {
        new Tier(0,  Color.FromRgb(0x22, 0xC5, 0x5E)),  // 低：绿
        new Tier(50, Color.FromRgb(0xFA, 0xCD, 0x14)),  // 注意：金黄 #FACD14
        new Tier(60, Color.FromRgb(0xF5, 0x9E, 0x0B)),  // 中：橙
        new Tier(85, Color.FromRgb(0xEF, 0x44, 0x44)),  // 高：红
    };

    /// <summary>
    /// 当前生效的档位表（升序，按 <see cref="ApplyConfig"/> 设置）。
    /// 初始为 <see cref="BuiltInDefaults"/>；设置项加载后会覆盖。
    /// </summary>
    public static IReadOnlyList<Tier> Tiers { get; private set; } = BuiltInDefaults;

    /// <summary>
    /// 显式档位键（low / mid / high）→ 当前档位的索引映射，供 <c>PercentToBrushConverter</c> 用 ConverterParameter 显式取色。
    /// </summary>
    private static readonly string[] _levelKeys = { "low", "mid", "high" };

    /// <summary>
    /// 档位表刷新后触发，订阅方应让相关 UI 重绘（如 InvalidateVisual、触发 PropertyChanged 让 PercentToBrushConverter 重算）。
    /// </summary>
    public static event EventHandler? TierChanged;

    /// <summary>
    /// 用外部配置重新初始化档位表。
    /// <para>
    /// 行为：
    /// <list type="bullet">
    ///   <item><description>空集合或全部禁用 → 回退到出厂默认（避免无档可用）。</description></item>
    ///   <item><description>按 MinPercent 升序排序；重复阈值保留前者。</description></item>
    ///   <item><description>禁用档从运行时表剔除，但保留配置项以便用户在 UI 取消勾选后能再启用。</description></item>
    ///   <item><description>应用完成后触发 <see cref="TierChanged"/>，UI 可同步重绘。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="config">持久化配置（可为 null；为 null 等同于"恢复默认"）。</param>
    public static void ApplyConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig>? config)
    {
        IReadOnlyList<Tier> next;
        if (config == null || config.Count == 0)
        {
            next = BuiltInDefaults;
        }
        else
        {
            var enabled = config.Where(t => t != null && t.IsEnabled).ToList();
            if (enabled.Count == 0)
            {
                next = BuiltInDefaults;
            }
            else
            {
                enabled.Sort((a, b) => a.MinPercent.CompareTo(b.MinPercent));
                next = enabled.Select(t => new Tier(t.MinPercent, ColorFromArgb(t.ColorArgb))).ToList();
            }
        }

        Tiers = next;
        TierChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 按已用百分比命中对应档位：取"下界不超过 percent 的最高一档"。
    /// </summary>
    /// <param name="percent">已用百分比（0-100；越界不影响命中逻辑）。</param>
    /// <returns>命中的档位（percent 低于所有阈值时返回首档）。</returns>
    public static Tier Resolve(double percent)
    {
        if (Tiers.Count == 0) return BuiltInDefaults[0];
        var hit = Tiers[0];
        foreach (var tier in Tiers)
        {
            if (percent >= tier.MinPercent) hit = tier;
            else break;
        }
        return hit;
    }

    /// <summary>
    /// 按已用百分比取画笔：每个 Tier 直接构造 SolidColorBrush（**不 Freeze**，便于热力图等自定义渲染控件
    /// 在 <see cref="TierChanged"/> 时正确刷新；进度条场景每次 binding 重新解析即可接受代价）。
    /// </summary>
    /// <param name="percent">已用百分比（0-100）。</param>
    public static Brush ResolveBrush(double percent)
        => new SolidColorBrush(Resolve(percent).Color);

    /// <summary>
    /// 按显式档位键（low / mid / high）取档：按 Tiers 升序的第 0 / 中间 / 末位。
    /// 供 <c>PercentToBrushConverter</c> 的 ConverterParameter 显式档位使用，
    /// 保持与新增档位数量解耦（3 档时等价于 0/1/2；4+ 档时取首/中/末）。
    /// </summary>
    /// <param name="level">low / mid / high（大小写不敏感）。</param>
    public static Tier? ResolveByLevel(string? level)
    {
        if (Tiers.Count == 0 || string.IsNullOrWhiteSpace(level)) return null;
        var key = level.Trim().ToLowerInvariant();
        return key switch
        {
            "low" => Tiers[0],
            "high" => Tiers[^1],
            "mid" => Tiers[Tiers.Count / 2],
            _ => null,
        };
    }

    /// <summary>从 ARGB 32 位整数还原为 WPF Color（兼容任意 Alpha，包括完全透明）。</summary>
    private static Color ColorFromArgb(uint argb)
    {
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return Color.FromArgb(a, r, g, b);
    }
}