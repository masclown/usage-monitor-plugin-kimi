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
/// 但本表是按 <c>token</c> 绝对值分档（MiniMax 默认 6 档：0/20M/100M/200M/300M），
/// 且按 <c>ProviderId</c> 索引（不同 Provider 数据规模差异大）。
/// </para>
/// <para>
/// 订阅 <see cref="TierChanged"/> 事件可在色阶变更时强制热力图控件重绘（参考
/// <c>ProviderUsageViewModel.RecolorHeatMapCells</c>）。
/// </para>
/// </summary>
public static class HeatMapTierScale
{
    /// <summary>MiniMax 出厂默认 6 档（无用量 / 极小 / 中等 / 大量 / 爆量）。</summary>
    public static readonly IReadOnlyList<HeatMapTier> MiniMaxDefaults = new[]
    {
        new HeatMapTier(0,             Color.FromRgb(0xF3, 0xF4, 0xF6)),  // 无用量 #f3f4f6
        new HeatMapTier(1,             Color.FromRgb(0xFF, 0xE7, 0xE2)),  // ≥1 (有用量) #ffe7e2
        new HeatMapTier(20_000_000L,   Color.FromRgb(0xFF, 0xC6, 0xBB)),  // ≥20M #ffc6bb
        new HeatMapTier(100_000_000L,  Color.FromRgb(0xFF, 0xA5, 0x95)),  // ≥100M #ffa595
        new HeatMapTier(200_000_000L,  Color.FromRgb(0xFF, 0x7B, 0x64)),  // ≥200M #ff7b64
        new HeatMapTier(300_000_000L,  Color.FromRgb(0xFF, 0x5A, 0x3D)),  // ≥300M #ff5a3d
    };

    /// <summary>其他 Provider 通用 4 档兜底（阈值更低，适配 K~M 级别数据）。</summary>
    public static readonly IReadOnlyList<HeatMapTier> GenericDefaults = new[]
    {
        new HeatMapTier(0,             Color.FromRgb(0xF3, 0xF4, 0xF6)),
        new HeatMapTier(1_000_000L,    Color.FromRgb(0xFF, 0xE7, 0xE2)),  // ≥1M
        new HeatMapTier(10_000_000L,   Color.FromRgb(0xFF, 0xC6, 0xBB)),  // ≥10M
        new HeatMapTier(100_000_000L,  Color.FromRgb(0xFF, 0xA5, 0x95)),  // ≥100M
    };

    /// <summary>当前生效的色阶表（按 ProviderId 索引；key 不区分大小写）。</summary>
    public static System.Collections.Generic.IReadOnlyDictionary<string, IReadOnlyList<HeatMapTier>> ProviderTiers { get; private set; }
        = new System.Collections.Generic.Dictionary<string, IReadOnlyList<HeatMapTier>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["minimax"] = MiniMaxDefaults,
        };

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
    /// 无 providerId 时用 <see cref="GenericDefaults"/> 兜底。
    /// </summary>
    public static Brush ResolveBrush(long token, string? providerId)
    {
        var key = (providerId ?? string.Empty).Trim();
        IReadOnlyList<HeatMapTier>? tiers;
        if (!string.IsNullOrEmpty(key) && ProviderTiers.TryGetValue(key, out var t))
            tiers = t;
        else
            tiers = GenericDefaults;
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
