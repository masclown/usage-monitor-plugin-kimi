using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using ToolTip = System.Windows.Controls.ToolTip;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

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
/// tooltip 提供者 v2（REQ-082 SDK v2）：返回 <see cref="TooltipContent"/>（插件自由拼装）。
/// <para>
/// 控件可同时实现 <see cref="IHoverTooltipProvider"/> 和 <see cref="IHoverTooltipProviderV2"/>；
/// 宿主优先调用本接口，未实现时 fallback 到 V1。
/// </para>
/// </summary>
public interface IHoverTooltipProviderV2
{
    /// <summary>根据控件内部坐标取得当前命中的 tooltip 内容。</summary>
    bool TryGetTooltip(Point position, out TooltipContent content);
}

/// <summary>
/// 图表 tooltip 的通用呈现器，统一主题、位置、延迟、淡出时机和无障碍文本。
/// </summary>
public static class HoverTooltipPresenter
{
    /// <summary>
    /// req-063 B11：使用 ConditionalWeakTable 替代 Dictionary，避免静态字典长期持有控件引用导致内存泄漏。
    /// 当 owner 被 GC 回收时，对应的 ToolTip 也会自动被回收。
    /// </summary>
    private static readonly ConditionalWeakTable<FrameworkElement, ToolTipHolder> ActiveTooltips = new();

    /// <summary>req-063 B11：ToolTip 包装类，用于 ConditionalWeakTable 的值类型。</summary>
    private sealed class ToolTipHolder
    {
        public ToolTip? Tooltip { get; set; }
    }

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
        if (ActiveTooltips.TryGetValue(owner, out var existingHolder) && existingHolder.Tooltip is { } existingTip && existingTip.Tag is TooltipElements elems)
        {
            elems.TitleBlock.Text = data.Title;
            // 问题1修复：Title 为空时折叠标题行，避免 tooltip 首行空白
            elems.TitleBlock.Visibility = string.IsNullOrWhiteSpace(data.Title)
                ? Visibility.Collapsed
                : Visibility.Visible;
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
        // 问题1修复：Title 为空时不占位（折叠），避免 tooltip 首行空白
        var titleBlock = new TextBlock
        {
            Text = data.Title,
            FontSize = 10,
            Foreground = titleBrush,
            Visibility = string.IsNullOrWhiteSpace(data.Title) ? Visibility.Collapsed : Visibility.Visible
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

        // req-063 B11：使用 ConditionalWeakTable 的 AddOrUpdate 模式
        var holder = ActiveTooltips.GetOrCreateValue(owner);
        holder.Tooltip = tooltip;
        owner.SetValue(AutomationProperties.HelpTextProperty, data.ToAccessibleText());
        tooltip.IsOpen = true;
    }

    /// <summary>关闭指定图表的 tooltip 并释放当前 ToolTip 实例。</summary>
    public static void Hide(FrameworkElement owner)
    {
        if (!ActiveTooltips.TryGetValue(owner, out var holder) || holder.Tooltip == null) return;
        holder.Tooltip.IsOpen = false;
        holder.Tooltip.Content = null;
        holder.Tooltip = null;
        ActiveTooltips.Remove(owner);
    }

    /// <summary>
    /// REQ-082 SDK v2：根据 <see cref="TooltipContent"/> 渲染 tooltip（插件自由拼装版本）。
    /// <para>
    /// 与 <see cref="Show(FrameworkElement, HoverTooltipData)"/> 共存：已打开时只更新 Panel 内容，
    /// 未打开时创建新 tooltip。Host 优先调 V2，未实现时 fallback 到 V1。
    /// </para>
    /// </summary>
    public static void Show(FrameworkElement owner, TooltipContent content)
    {
        // 已打开：更新内容（注意：v2 是动态 Panel，不只是 Text 属性，
        // 这里为了简化直接重建视觉树，未来可优化为按 Block 类型 diff 更新）
        if (ActiveTooltips.TryGetValue(owner, out var existingHolder) && existingHolder.Tooltip is { } existingTip)
        {
            existingTip.Content = BuildTooltipCard(owner, content);
            owner.SetValue(AutomationProperties.HelpTextProperty, BuildAccessibleText(content));
            return;
        }

        // 首次打开
        var tooltip = new ToolTip
        {
            Content = BuildTooltipCard(owner, content),
            Template = new System.Windows.Controls.ControlTemplate(typeof(ToolTip))
            {
                VisualTree = CreateTemplateVisualTree()
            },
            PlacementTarget = owner,
            Placement = PlacementMode.Mouse,
            HorizontalOffset = 8,
            VerticalOffset = -12,
            StaysOpen = true,
            HasDropShadow = false
        };
        ToolTipService.SetShowDuration(tooltip, 30000);

        var holder = ActiveTooltips.GetOrCreateValue(owner);
        holder.Tooltip = tooltip;
        owner.SetValue(AutomationProperties.HelpTextProperty, BuildAccessibleText(content));
        tooltip.IsOpen = true;
    }

    /// <summary>根据 TooltipContent 构建 Border 视觉树。</summary>
    private static Border BuildTooltipCard(FrameworkElement owner, TooltipContent content)
    {
        var titleBrush = FindBrush(owner, "TextSecondaryBrush", Color.FromRgb(0xC4, 0xCF, 0xDD));
        var valueBrush = FindBrush(owner, "TextPrimaryBrush", Color.FromRgb(0xF8, 0xFA, 0xFC));
        var detailBrush = FindBrush(owner, "TextTertiaryBrush", Color.FromRgb(0x94, 0xA3, 0xB8));
        var background = FindBrush(owner, "SurfaceAltBrush", Color.FromArgb(0xE8, 0x1F, 0x24, 0x30));

        var panel = new StackPanel { MinWidth = 92 };
        if (content.Blocks != null)
        {
            foreach (var block in content.Blocks)
            {
                switch (block)
                {
                    case TooltipTextBlock text:
                        panel.Children.Add(BuildTextBlock(text, titleBrush, valueBrush, detailBrush));
                        break;
                    case TooltipColorRow colorRow:
                        panel.Children.Add(BuildColorRow(colorRow, titleBrush, detailBrush));
                        break;
                    case TooltipSummaryRow summary:
                        panel.Children.Add(BuildSummaryRow(summary, valueBrush));
                        break;
                }
            }
        }

        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 5, 9, 5),
            Child = panel
        };
    }

    /// <summary>构建文本行（按 Style 应用字号/字重/颜色）。</summary>
    private static TextBlock BuildTextBlock(TooltipTextBlock text, Brush secondaryBrush, Brush primaryBrush, Brush tertiaryBrush)
    {
        Brush foreground;
        double fontSize;
        FontWeight weight;
        switch (text.Style)
        {
            case TooltipTextStyle.Bold:
                foreground = primaryBrush;
                fontSize = 14;
                weight = FontWeights.SemiBold;
                break;
            case TooltipTextStyle.Secondary:
                foreground = tertiaryBrush;
                fontSize = 10;
                weight = FontWeights.Normal;
                break;
            default:
                foreground = secondaryBrush;
                fontSize = 12;
                weight = FontWeights.Normal;
                break;
        }
        return new TextBlock
        {
            Text = text.Text,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground,
            Margin = new Thickness(0, 2, 0, 0)
        };
    }

    /// <summary>构建色块明细行：■ 标签  值。</summary>
    private static Grid BuildColorRow(TooltipColorRow row, Brush labelBrush, Brush valueBrush)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatch = new Rectangle
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = MetricGridControl.TryParseBrush(row.Color ?? "#888888") ?? Brushes.Gray
        };
        Grid.SetColumn(swatch, 0);
        grid.Children.Add(swatch);

        var label = new TextBlock
        {
            Text = row.Label,
            FontSize = 12,
            Foreground = labelBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var value = new TextBlock
        {
            Text = row.Value,
            FontSize = 12,
            Foreground = valueBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        return grid;
    }

    /// <summary>构建合计行：标签（左）+ 加粗值（右）。</summary>
    private static Grid BuildSummaryRow(TooltipSummaryRow row, Brush valueBrush)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = row.Label,
            FontSize = 12,
            Foreground = valueBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var value = new TextBlock
        {
            Text = row.Value,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        return grid;
    }

    /// <summary>从 TooltipContent 生成屏幕阅读器文本。</summary>
    private static string BuildAccessibleText(TooltipContent content)
    {
        if (content.Blocks == null || content.Blocks.Count == 0) return string.Empty;
        var parts = new List<string>(content.Blocks.Count);
        foreach (var block in content.Blocks)
        {
            switch (block)
            {
                case TooltipTextBlock t:
                    parts.Add(t.Text);
                    break;
                case TooltipColorRow c:
                    parts.Add($"{c.Label}：{c.Value}");
                    break;
                case TooltipSummaryRow s:
                    parts.Add($"{s.Label}：{s.Value}");
                    break;
            }
        }
        return string.Join("。", parts);
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
