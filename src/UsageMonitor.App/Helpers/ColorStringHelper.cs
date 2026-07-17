using System.Globalization;
using System.Windows.Media;
// WPF+WinForms 混合项目下 Color / Brush 出现在两个命名空间里，alias 到 WPF 侧。
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 把 hex 字符串（如 "#f3f4f6" / "#ffa595" / "#ffa59580"）解析为 WPF <see cref="Color"/> 或 <see cref="Brush"/>。
/// <para>
/// 解析失败时回退到稳定的灰色（#94A3B8），避免热力图整片变透明。
/// 用于 <see cref="HeatMapTierScale"/> 从 config.json 读取 <c>HeatMapTierConfig.ColorHex</c> 时解析。
/// </para>
/// </summary>
public static class ColorStringHelper
{
    /// <summary>解析 hex 字符串为 Color；失败回退到 #94A3B8（次级字色灰）。</summary>
    public static Color Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.FromRgb(0x94, 0xA3, 0xB8);
        var s = hex.Trim();
        if (s.StartsWith("#", System.StringComparison.Ordinal)) s = s.Substring(1);
        try
        {
            if (s.Length == 6)
            {
                byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
                return Color.FromRgb(r, g, b);
            }
            if (s.Length == 8)
            {
                byte a = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
                byte r = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch
        {
            // 解析失败回退
        }
        return Color.FromRgb(0x94, 0xA3, 0xB8);
    }

    /// <summary>解析 hex 字符串为 SolidColorBrush（自动 Freeze，可安全跨线程）。</summary>
    public static Brush ParseBrush(string hex)
    {
        var c = Parse(hex);
        var b = new SolidColorBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}
