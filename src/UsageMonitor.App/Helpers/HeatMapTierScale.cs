using System.Windows.Media;
using UsageMonitor.Core.Models;
// WPF+WinForms 混合项目下 Color / Brush 出现在两个命名空间里，alias 到 WPF 侧。
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 热力图色阶的运行时档位（req-009）。
/// <para>
/// 持久化模型 <see cref="HeatMapTierConfig"/>（Core 项目）只持有 <c>MinTokens / ColorHex / IsEnabled</c>，
/// 运行时本 record 持有解析后的 <see cref="Color"/>，避免热力图渲染时重复解析 hex 字符串。
/// </para>
/// </summary>
/// <param name="MinTokens">档位下界（包含），单位 tokens。0 档为兜底色。</param>
/// <param name="Color">档位颜色。</param>
/// <param name="IsEnabled">档位是否启用；false 时在 <c>ResolveBrush</c> 中被跳过。</param>
public sealed record HeatMapTier(long MinTokens, Color Color, bool IsEnabled = true)
{
    /// <summary>把 <see cref="Color"/> 转为已冻结的 <see cref="SolidColorBrush"/>，可直接绑定到 UI 元素。</summary>
    public Brush ToBrush()
    {
        var b = new SolidColorBrush(Color);
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}

/// <summary>
/// 热力图色阶运行时表（req-009）。
/// <para>
/// 设计参考：与 <see cref="UsageTierScale"/>（按百分比 4 档的全局进度条色阶）类似，
/// 但本表是按 <c>token</c> 绝对值分档（Provider 可经声明包 card.heatMapTiers 自定义档位），
/// 且按 <c>ProviderId</c> 索引（不同 Provider 数据规模差异大）。
/// </para>
/// <para>
/// 订阅 <see cref="TierChanged"/> 事件可在色阶变更时强制热力图控件重绘（参考
/// <c>ProviderUsageViewModel.RecolorHeatMapCells</c>）。
/// </para>
/// </summary>
public static class HeatMapTierScale
{
    // Stage E：MiniMax 专名出厂默认已删除——Provider 默认色阶改由声明包 card.heatMapTiers 声明，
    // 启动时经 <see cref="RegisterDeclaredDefaults"/> 注册（宿主零专名硬编码）。

    /// <summary>通用 4 档兜底（适配 K~M 级别数据；无声明、无持久化配置时使用）。</summary>
    public static readonly IReadOnlyList<HeatMapTier> GenericDefaults = new[]
    {
        new HeatMapTier(0,             Color.FromRgb(0xF3, 0xF4, 0xF6)),
        new HeatMapTier(1_000_000L,    Color.FromRgb(0xFF, 0xE7, 0xE2)),  // ≥1M
        new HeatMapTier(10_000_000L,   Color.FromRgb(0xFF, 0xC6, 0xBB)),  // ≥10M
        new HeatMapTier(100_000_000L,  Color.FromRgb(0xFF, 0xA5, 0x95)),  // ≥100M
    };

    /// <summary>当前生效的色阶表（按 ProviderId 索引；key 不区分大小写；来自用户持久化配置）。</summary>
    public static System.Collections.Generic.IReadOnlyDictionary<string, IReadOnlyList<HeatMapTier>> ProviderTiers { get; private set; }
        = new System.Collections.Generic.Dictionary<string, IReadOnlyList<HeatMapTier>>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>声明包默认色阶表（来自插件 card.heatMapTiers 声明；优先级低于用户持久化配置）。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, IReadOnlyList<HeatMapTier>> DeclaredDefaults
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个 Provider 的声明默认色阶（启动时从插件 card.heatMapTiers 装配）。
    /// <para>仅作为 ResolveBrush / 编辑器回显的声明级兜底；用户持久化配置（ProviderTiers）始终优先。</para>
    /// </summary>
    /// <param name="providerId">插件 ID。</param>
    /// <param name="tiers">声明的色阶档位（空集合则忽略）。</param>
    public static void RegisterDeclaredDefaults(string providerId, System.Collections.Generic.IEnumerable<HeatMapTierConfig>? tiers)
    {
        if (string.IsNullOrWhiteSpace(providerId) || tiers == null) return;
        var list = new System.Collections.Generic.List<HeatMapTier>();
        foreach (var c in tiers)
        {
            if (c == null) continue;
            list.Add(new HeatMapTier(c.MinTokens, ColorStringHelper.Parse(c.ColorHex), c.IsEnabled));
        }
        if (list.Count > 0) DeclaredDefaults[providerId.Trim()] = list;
    }

    /// <summary>取指定 Provider 的声明默认色阶（未声明返回 null；供设置页编辑器回显兜底）。</summary>
    /// <param name="providerId">插件 ID。</param>
    public static IReadOnlyList<HeatMapTier>? GetDeclaredDefaults(string? providerId)
    {
        var key = (providerId ?? string.Empty).Trim();
        return key.Length > 0 && DeclaredDefaults.TryGetValue(key, out var tiers) ? tiers : null;
    }

    /// <summary>色阶变更事件（设置页保存 / 启动加载完成后触发）。订阅者应强制重绘热力图。</summary>
    public static event System.EventHandler? TierChanged;

    /// <summary>
    /// 应用指定配置（来自 <see cref="ConfigService"/>）覆盖当前色阶表。
    /// <para>
    /// key 为 ProviderId（不区分大小写）；value 为该 Provider 的色阶档位列表。
    /// 缺失的 Provider 仍保留之前的色阶；空的 Provider 列表等同于"删除"该 Provider 的色阶。
    /// 转换过程：把每个 <see cref="HeatMapTierConfig"/> 解析为 <see cref="HeatMapTier"/>。
    /// </para>
    /// </summary>
    public static void ApplyConfig(System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IList<HeatMapTierConfig>>? config)
    {
        if (config == null) return;
        var dict = new System.Collections.Generic.Dictionary<string, IReadOnlyList<HeatMapTier>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var kv in config)
        {
            if (kv.Value == null) continue;
            var list = new System.Collections.Generic.List<HeatMapTier>(kv.Value.Count);
            foreach (var c in kv.Value)
            {
                if (c == null) continue;
                list.Add(new HeatMapTier(c.MinTokens, ColorStringHelper.Parse(c.ColorHex), c.IsEnabled));
            }
            dict[kv.Key] = list;
        }
        ProviderTiers = dict;
        TierChanged?.Invoke(null, System.EventArgs.Empty);
    }

    /// <summary>
    /// 按 token 命中档位（取下界不超过 token 的最高档；未命中或全禁用时回退到首档颜色）。
    /// 优先级：用户持久化配置 → 插件声明默认 → <see cref="GenericDefaults"/>。
    /// </summary>
    public static Brush ResolveBrush(long token, string? providerId)
    {
        var key = (providerId ?? string.Empty).Trim();
        IReadOnlyList<HeatMapTier>? tiers;
        if (!string.IsNullOrEmpty(key) && ProviderTiers.TryGetValue(key, out var t))
            tiers = t;
        else
            tiers = GetDeclaredDefaults(key) ?? GenericDefaults;
        if (tiers == null || tiers.Count == 0)
            return GenericDefaults[0].ToBrush();
        HeatMapTier? hit = null;
        foreach (var tier in tiers)
        {
            if (!tier.IsEnabled) continue;
            if (token >= tier.MinTokens) hit = tier;
        }
        return (hit ?? tiers[0]).ToBrush();
    }
}
