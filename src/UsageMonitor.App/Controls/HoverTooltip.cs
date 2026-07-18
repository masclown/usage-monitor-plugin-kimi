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
    /// req-046 修复：tooltip 内部元素引用，用于快速更新 Text 属性而不重建视觉树。
    /// </summary>
    private sealed class TooltipElements
    {
        public TextBlock TitleBlock { get; set; } = null!;
        public TextBlock ValueBlock { get; set; } = null!;
        public TextBlock? DetailBlock { get; set; }
    }

    /// <summary>
    /// 在指定图表附近打开 tooltip；重复调用只更新当前图表的提示内容。
    /// req-046 修复：tooltip 已打开时只更新 Text 属性，不重建视觉树，彻底消除闪烁。
    /// </summary>
    public static void Show(FrameworkElement owner, HoverTooltipData data)
    {
        // req-046 修复：如果 tooltip 已打开，只更新 Text 属性，零布局开销
        if (ActiveTooltips.TryGetValue(owner, out var existing) && existing.Tag is TooltipElements elems)
        {
            elems.TitleBlock.Text = data.Title;
            elems.ValueBlock.Text = data.Value;
            if (elems.DetailBlock != null)
            {
                elems.DetailBlock.Text = data.Detail ?? string.Empty;
                elems.DetailBlock.Visibility = string.IsNullOrWhiteSpace(data.Detail)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            owner.SetValue(AutomationProperties.HelpTextProperty, data.ToAccessibleText());
            return;
        }

        // 首次打开：创建新 tooltip
        var titleBrush = FindBrush(owner, "TextSecondaryBrush", Color.FromRgb(0xC4, 0xCF, 0xDD));
        var valueBrush = FindBrush(owner, "TextPrimaryBrush", Color.FromRgb(0xF8, 0xFA, 0xFC));
        var detailBrush = FindBrush(owner, "TextTertiaryBrush", Color.FromRgb(0x94, 0xA3, 0xB8));
        // req-046：使用 SurfaceAltBrush 作为背景（主题适配的深色，非纯黑）
        var background = FindBrush(owner, "SurfaceAltBrush", Color.FromArgb(0xE8, 0x1F, 0x24, 0x30));

        var panel = new StackPanel { MinWidth = 92 };
        var titleBlock = new TextBlock
        {
            Text = data.Title,
            FontSize = 10,
            Foreground = titleBrush
        };
        panel.Children.Add(titleBlock);
        var valueBlock = new TextBlock
        {
            Text = data.Value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush,
            Margin = new Thickness(0, 2, 0, 0)
        };
        panel.Children.Add(valueBlock);
        TextBlock? detailBlock = null;
        if (!string.IsNullOrWhiteSpace(data.Detail))
        {
            detailBlock = new TextBlock
            {
                Text = data.Detail,
                FontSize = 10,
                Foreground = detailBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            panel.Children.Add(detailBlock);
        }

        var card = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 5, 9, 5),
            Child = panel
            // req-046 修复：移除 DropShadowEffect，快速移动鼠标时 BlurRadius=8 的阴影
            // 会产生大面积灰色模糊区域（即用户看到的“灰色圆点”闪烁）。
            // HasDropShadow=false 已禁用系统阴影，无需自定义 Effect。
        };

        var tooltip = new ToolTip
        {
            Content = card,
            // req-046 修复：保存元素引用到 Tag，供后续快速更新
            Tag = new TooltipElements
            {
                TitleBlock = titleBlock,
                ValueBlock = valueBlock,
                DetailBlock = detailBlock
            },
            // req-046：重写 ToolTip 模板，彻底移除默认黑色背景
            Template = new System.Windows.Controls.ControlTemplate(typeof(ToolTip))
            {
                VisualTree = CreateTemplateVisualTree()
            },
            PlacementTarget = owner,
            Placement = PlacementMode.Mouse,
            HorizontalOffset = 8,
            VerticalOffset = -12,
            // req-034 修复：StaysOpen=true 防止鼠标移动时 tooltip 立即关闭
            // 关闭由控件的 OnMouseLeave 调用 HoverTooltipPresenter.Hide 处理
            StaysOpen = true,
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

    /// <summary>
    /// req-046：创建 ToolTip 模板视觉树（Border + ContentPresenter），透明背景。
    /// </summary>
    private static System.Windows.FrameworkElementFactory CreateTemplateVisualTree()
    {
        var contentPresenter = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
        var border = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border))
        {
            Name = "Bd"
        };
        border.AppendChild(contentPresenter);
        return border;
    }
}
