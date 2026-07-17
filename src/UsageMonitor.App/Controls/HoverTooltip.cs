using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using ToolTip = System.Windows.Controls.ToolTip;
using DropShadowEffect = System.Windows.Media.Effects.DropShadowEffect;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 图表 hover 提示数据：将命中位置、数值和可选的补充信息统一成可读文本。
/// </summary>
public sealed record HoverTooltipData(string Title, string Value, string? Detail = null)
{
    /// <summary>生成供屏幕阅读器使用的完整提示文本。</summary>
    public string ToAccessibleText()
        => string.IsNullOrWhiteSpace(Detail)
            ? $"{Title}：{Value}"
            : $"{Title}：{Value}。{Detail}";
}

/// <summary>
/// 将图表坐标映射为 tooltip 数据的统一契约。
/// </summary>
public interface IHoverTooltipProvider
{
    /// <summary>根据控件内部坐标取得当前命中的数据点。</summary>
    bool TryGetTooltip(Point position, out HoverTooltipData data);
}

/// <summary>
/// 图表 tooltip 的通用呈现器，统一主题、位置、延迟、淡出时机和无障碍文本。
/// </summary>
public static class HoverTooltipPresenter
{
    private static readonly Dictionary<FrameworkElement, ToolTip> ActiveTooltips = new();

    /// <summary>
    /// 在指定图表附近打开 tooltip；重复调用只更新当前图表的提示内容。
    /// </summary>
    public static void Show(FrameworkElement owner, HoverTooltipData data)
    {
        Hide(owner);

        var titleBrush = FindBrush(owner, "TextSecondaryBrush", Color.FromRgb(0xC4, 0xCF, 0xDD));
        var valueBrush = FindBrush(owner, "TextPrimaryBrush", Color.FromRgb(0xF8, 0xFA, 0xFC));
        var detailBrush = FindBrush(owner, "TextTertiaryBrush", Color.FromRgb(0x94, 0xA3, 0xB8));
        var background = FindBrush(owner, "SurfaceAltBrush", Color.FromArgb(0xE8, 0x1F, 0x24, 0x30));

        var panel = new StackPanel { MinWidth = 92 };
        panel.Children.Add(new TextBlock
        {
            Text = data.Title,
            FontSize = 10,
            Foreground = titleBrush
        });
        panel.Children.Add(new TextBlock
        {
            Text = data.Value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush,
            Margin = new Thickness(0, 2, 0, 0)
        });
        if (!string.IsNullOrWhiteSpace(data.Detail))
        {
            panel.Children.Add(new TextBlock
            {
                Text = data.Detail,
                FontSize = 10,
                Foreground = detailBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        var card = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 5, 9, 5),
            Child = panel,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.35,
                Color = Colors.Black
            }
        };

        var tooltip = new ToolTip
        {
            Content = card,
            PlacementTarget = owner,
            Placement = PlacementMode.Mouse,
            HorizontalOffset = 8,
            VerticalOffset = -12,
            StaysOpen = false,
            HasDropShadow = false
        };
        ToolTipService.SetShowDuration(tooltip, 30000);

        ActiveTooltips[owner] = tooltip;
        owner.SetValue(AutomationProperties.HelpTextProperty, data.ToAccessibleText());
        tooltip.IsOpen = true;
    }

    /// <summary>关闭指定图表的 tooltip 并释放当前 ToolTip 实例。</summary>
    public static void Hide(FrameworkElement owner)
    {
        if (!ActiveTooltips.Remove(owner, out var tooltip)) return;
        tooltip.IsOpen = false;
        tooltip.Content = null;
    }

    /// <summary>按主题资源键取得画笔，资源缺失时使用稳定的颜色回退。</summary>
    private static Brush FindBrush(FrameworkElement owner, string key, Color fallback)
    {
        if (owner.TryFindResource(key) is Brush brush) return brush;
        var result = new SolidColorBrush(fallback);
        result.Freeze();
        return result;
    }
}
