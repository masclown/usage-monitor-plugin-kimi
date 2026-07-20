using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Size = System.Windows.Size;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 维度切换器（REQ-083 SDK v2）。通用 SegmentedButton 风格的 Tab 按钮组。
/// <para>
/// 不绑定任何特定图表或数据源：
/// <list type="bullet">
/// <item>数据：<see cref="Dimensions"/> 字符串列表（如 ["模型", "API Key"]）</item>
/// <item>选中：<see cref="SelectedIndex"/> 整数索引</item>
/// <item>事件：<see cref="SelectionChanged"/></item>
/// <item>样式：选中态黑底白字，未选中透明底灰字</item>
/// </list>
/// </para>
/// <para>
/// 数据来源策略：插件在刷新时一次性拉取所有维度数据并持久化，切换只是"视图切换"，
/// 零网络开销。
/// </para>
/// </summary>
public class ChartDimensionSwitcher : Control
{
    static ChartDimensionSwitcher()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ChartDimensionSwitcher),
            new FrameworkPropertyMetadata(typeof(ChartDimensionSwitcher)));
    }

    /// <summary>维度标签列表（REQ-083）。</summary>
    public static readonly DependencyProperty DimensionsProperty = DependencyProperty.Register(
        nameof(Dimensions), typeof(IReadOnlyList<string>), typeof(ChartDimensionSwitcher),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDimensionsChanged));

    /// <summary>当前选中索引。</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(ChartDimensionSwitcher),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIndexChanged));

    /// <summary>选中变更事件。</summary>
    public static readonly RoutedEvent SelectionChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(ChartDimensionSwitcher));

    /// <summary>维度标签列表（如 ["模型", "API Key"]）。</summary>
    public IReadOnlyList<string>? Dimensions
    {
        get => (IReadOnlyList<string>?)GetValue(DimensionsProperty);
        set => SetValue(DimensionsProperty, value);
    }

    /// <summary>当前选中索引（-1 表示未选中）。</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>选中索引变化事件（REQ-083）。</summary>
    public event RoutedPropertyChangedEventHandler<int> SelectionChanged
    {
        add { AddHandler(SelectionChangedEvent, value); }
        remove { RemoveHandler(SelectionChangedEvent, value); }
    }

    private static void OnDimensionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChartDimensionSwitcher c) c.RebuildButtons();
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChartDimensionSwitcher c) c.RefreshButtonsVisual();
    }

    private readonly StackPanel _rootPanel;

    /// <summary>构造维度切换器：内部用 StackPanel 水平排列 ToggleButton。</summary>
    public ChartDimensionSwitcher()
    {
        _rootPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AddVisualChild(_rootPanel);
        AddLogicalChild(_rootPanel);
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
        return _rootPanel;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        _rootPanel.Measure(constraint);
        return _rootPanel.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        _rootPanel.Arrange(new Rect(arrangeBounds));
        return arrangeBounds;
    }

    /// <summary>根据 <see cref="Dimensions"/> 重建按钮列表。</summary>
    private void RebuildButtons()
    {
        _rootPanel.Children.Clear();
        if (Dimensions == null) return;
        for (int i = 0; i < Dimensions.Count; i++)
        {
            int capturedIndex = i;
            var label = Dimensions[i];
            var btn = new Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(12, 4, 12, 4),
                MinWidth = 56,
                FontSize = 12,
                Cursor = Cursors.Hand
            };
            btn.SetResourceReference(Button.StyleProperty, "DimensionSwitcherButtonStyle");
            btn.Click += (_, _) => SelectedIndex = capturedIndex;
            _rootPanel.Children.Add(btn);
        }
        RefreshButtonsVisual();
    }

    /// <summary>刷新所有按钮的视觉态（基于当前 SelectedIndex）。</summary>
    private void RefreshButtonsVisual()
    {
        for (int i = 0; i < _rootPanel.Children.Count; i++)
        {
            if (_rootPanel.Children[i] is Button btn)
            {
                bool isSelected = i == SelectedIndex;
                btn.SetResourceReference(Button.BackgroundProperty,
                    isSelected ? "AccentBrush" : "SurfaceAltBrush");
                btn.SetResourceReference(Button.ForegroundProperty,
                    isSelected ? "TextOnAccentBrush" : "TextSecondaryBrush");
            }
        }
    }
}