using System;
using System.Collections.Generic;
using System.Globalization;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Display;

/// <summary>
/// 显示资源包公共头（req-115）：四类包（主题 / 图表样式 / mini 图表样式 / 悬浮窗模板）共用的元信息。
/// <para>包目录与 plugins/ 平级：themes/ charts/ minicharts/ traytooltips/，全部 JSON 声明、宿主渲染、零代码执行。</para>
/// </summary>
public abstract class DisplayPackBase
{
    /// <summary>声明 schema 版本（当前为 1）。</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>包唯一标识（必填；缺失时整包跳过）。</summary>
    public string? Id { get; set; }

    /// <summary>包显示名（设置界面下拉展示；缺省回退 Id）。</summary>
    public string? DisplayName { get; set; }

    /// <summary>包所在目录（加载时回填，供 assets 相对路径解析；不参与序列化输入）。</summary>
    public string PackDirectory { get; set; } = "";

    /// <summary>设置界面展示名（DisplayName 缺省时回退 Id）。</summary>
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? (Id ?? "") : DisplayName!;
}

/// <summary>
/// 主题包（themes/&lt;pack&gt;/theme.json）：设计令牌色值映射。
/// <para>token 键对齐宿主 Themes/Dark.xaml 与 Light.xaml 的资源键（如 "BackgroundBrush"），
/// 值为 #RRGGBB / #AARRGGBB 色值；缺失的 token 由宿主按 <see cref="IsDark"/> 对应内置主题打底。</para>
/// </summary>
public sealed class ThemePack : DisplayPackBase
{
    /// <summary>是否深色系（决定缺失 token 的内置打底主题与托盘图标配色参考）。</summary>
    public bool IsDark { get; set; } = true;

    /// <summary>设计令牌映射：资源键 → 色值（#RRGGBB / #AARRGGBB）。</summary>
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 单个图表类型的样式声明（图表 / mini 图表样式包共用）。
/// <para>色阶（thresholds+colors 等长数组）+ 白名单样式参数；<see cref="Assets"/> 为未来
/// "图表主题 / 视觉特效 / 图片素材替换" SDK 预留的图片引用槽位（本期不渲染）。</para>
/// </summary>
public sealed class ChartStyleEntry
{
    /// <summary>色阶阈值（升序，与 <see cref="Colors"/> 等长）。</summary>
    public List<double> Thresholds { get; set; } = new();

    /// <summary>色阶颜色（#RRGGBB，与 <see cref="Thresholds"/> 等长）。</summary>
    public List<string> Colors { get; set; } = new();

    /// <summary>白名单样式参数（如 lineThickness / ringThickness / cellSize，宿主按需消费，未知键忽略）。</summary>
    public Dictionary<string, double> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>预留：图片素材引用（槽位名 → 包内 assets/ 相对路径；本期仅声明不渲染）。</summary>
    public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 图表样式包（charts/&lt;pack&gt;/chartstyle.json）：按图表类型声明样式。
/// <para>键为 ChartKind 名（Bar / Line / HeatMap / Ring / Number）或特殊键 "usage"
/// （全局用量色阶：选中该包后覆盖进度条 / 环形图的全局取色）。</para>
/// </summary>
public sealed class ChartStylePack : DisplayPackBase
{
    /// <summary>图表类型 → 样式声明。</summary>
    public Dictionary<string, ChartStyleEntry> ChartStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// mini 图表样式包（minicharts/&lt;pack&gt;/ministyle.json）：按迷你图类型声明样式。
/// <para>键为 MiniChart 类型名（MiniRingChart / MiniText 等）；色阶应用到任务栏迷你图私有色阶。</para>
/// </summary>
public sealed class MiniChartStylePack : DisplayPackBase
{
    /// <summary>迷你图类型 → 样式声明。</summary>
    public Dictionary<string, ChartStyleEntry> ChartStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 悬浮窗模板行（traytooltips 包）：一行 = 一个 SDK 字段行或一段静态文本。
/// </summary>
public sealed class TrayTooltipRow
{
    /// <summary>SDK 标准字段名（与 <see cref="TextTemplate"/> 二选一；经字段白名单校验）。</summary>
    public string? FieldName { get; set; }

    /// <summary>静态文本行（与 <see cref="FieldName"/> 二选一；原样渲染）。</summary>
    public string? TextTemplate { get; set; }

    /// <summary>本行之后是否插入分隔线。</summary>
    public bool SeparatorAfter { get; set; }
}

/// <summary>
/// 托盘悬浮窗模板包（traytooltips/&lt;pack&gt;/traytooltip.json）：字段行布局模板。
/// <para>选中后每个 Provider 摘要卡的明细区按 <see cref="Rows"/> 声明渲染，布局仍由宿主模板统一。</para>
/// </summary>
public sealed class TrayTooltipPack : DisplayPackBase
{
    /// <summary>行声明列表（按序渲染）。</summary>
    public List<TrayTooltipRow> Rows { get; set; } = new();
}

/// <summary>
/// 显示资源包转换助手（req-115）：把包内声明映射为宿主既有色阶模型。
/// </summary>
public static class DisplayPackConverters
{
    /// <summary>
    /// 把样式条目的色阶（thresholds + colors）转换为 <see cref="UsageTierConfig"/> 列表。
    /// <para>阈值/颜色不等长、为空或任一色值解析失败时返回 null（调用方回退既有色阶）。</para>
    /// </summary>
    /// <param name="entry">样式条目。</param>
    public static List<UsageTierConfig>? ToUsageTiers(this ChartStyleEntry? entry)
    {
        if (entry == null || entry.Thresholds.Count == 0 || entry.Thresholds.Count != entry.Colors.Count) return null;
        var list = new List<UsageTierConfig>();
        for (var i = 0; i < entry.Thresholds.Count; i++)
        {
            if (!TryParseArgb(entry.Colors[i], out var argb)) return null;
            list.Add(new UsageTierConfig { MinPercent = entry.Thresholds[i], ColorArgb = argb, IsEnabled = true });
        }
        return list;
    }

    /// <summary>解析 #RRGGBB / #AARRGGBB 色值为 ARGB 32 位整数（RRGGBB 默认不透明）。</summary>
    /// <param name="hex">色值文本。</param>
    /// <param name="argb">解析结果。</param>
    public static bool TryParseArgb(string? hex, out uint argb)
    {
        argb = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.TrimStart('#');
        if (s.Length == 6)
        {
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
            argb = 0xFF000000u | rgb;
            return true;
        }
        if (s.Length == 8)
        {
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb);
        }
        return false;
    }
}
