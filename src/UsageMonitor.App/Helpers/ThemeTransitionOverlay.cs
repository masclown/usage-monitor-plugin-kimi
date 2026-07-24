using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using UsageMonitor.Core.Services;
using Image = System.Windows.Controls.Image;
using Panel = System.Windows.Controls.Panel;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-081 U-42：主题切换过渡遮罩（双层遮罩淡出方案）。
/// <para>
/// 切换主题前，把主窗口当前内容渲染为冻结位图并盖到窗口内容最上层（第一层遮罩 = 旧主题快照）；
/// 应用新主题后（第二层 = 已换肤的真实内容），快照遮罩约 250ms 淡出并彻底从视觉树移除，
/// 避免主题瞬间切换的"硬切"闪烁。仅对主窗口生效——历史 / 设置窗口开着时允许直接切换，不做过渡。
/// </para>
/// <para>
/// 边界保证：
/// - 遮罩全程 IsHitTestVisible=false，不拦截鼠标事件；
/// - 动画自然结束后立即从父面板移除并释放位图引用（防内存泄漏、防吃事件）；
/// - 捕获失败（窗口不可见 / 尺寸未就绪 / 渲染异常）返回 null，调用方自动退化为无过渡直切。
/// </para>
/// </summary>
internal static class ThemeTransitionOverlay
{
    /// <summary>遮罩淡出时长（毫秒），取值 200~300ms 区间以兼顾流畅感与响应感。</summary>
    private const int FadeOutMs = 250;

    /// <summary>遮罩标识（写入 <see cref="FrameworkElement.Tag"/>），供快照前清理残留遮罩时识别。</summary>
    private const string OverlayTag = "ThemeTransitionOverlay";

    /// <summary>共享的冻结缓动函数（QuadraticEase EaseOut）。</summary>
    private static readonly QuadraticEase EaseOut = CreateFrozenEase();

    /// <summary>创建并冻结淡出缓动函数。</summary>
    private static QuadraticEase CreateFrozenEase()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    /// <summary>
    /// 捕获窗口内容快照并盖到内容根面板最上层。失败返回 null（调用方退化为直切）。
    /// </summary>
    /// <param name="window">目标窗口（仅主窗口传入）。</param>
    /// <returns>已挂载的遮罩 Image；失败为 null。</returns>
    public static Image? AttachSnapshotOverlay(Window? window)
    {
        try
        {
            if (window == null || !window.IsVisible || window.WindowState == WindowState.Minimized) return null;
            // 内容根必须是 Panel（主窗口为 Grid）才能追加遮罩子元素
            if (window.Content is not Panel rootPanel) return null;
            var width = rootPanel.ActualWidth;
            var height = rootPanel.ActualHeight;
            if (width <= 0 || height <= 0) return null;

            // 清理尚未淡出完毕的残留遮罩：快速连续切换主题时，旧快照仍在淡出中，
            // 若不清理会被本次 bitmap.Render 捕获进新快照，产生叠影
            for (var i = rootPanel.Children.Count - 1; i >= 0; i--)
            {
                if (rootPanel.Children[i] is Image img && img.Tag is OverlayTag)
                    rootPanel.Children.RemoveAt(i);
            }

            // 按窗口实际 DPI 渲染，保证高分屏下快照不糊
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            bitmap.Render(rootPanel);
            bitmap.Freeze(); // 冻结位图：解除渲染线程依赖，淡出期间内容不再变化

            var overlay = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false, // 遮罩永不拦截鼠标事件
                Opacity = 1,
                Tag = OverlayTag // 标记身份，供下次快照前识别并清理残留遮罩
            };
            Panel.SetZIndex(overlay, int.MaxValue); // 置于所有内容之上
            // 主窗口根 Grid 为多行多列布局：不设 RowSpan/ColumnSpan 时遮罩默认落 cell(0,0)，
            // 整窗位图被 Stretch.Fill 压扁进首行条带，其余区域无遮罩硬切（U-42 失效）
            if (rootPanel is System.Windows.Controls.Grid)
            {
                System.Windows.Controls.Grid.SetRowSpan(overlay, int.MaxValue);
                System.Windows.Controls.Grid.SetColumnSpan(overlay, int.MaxValue);
            }
            rootPanel.Children.Add(overlay);
            return overlay;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("ThemeTransitionOverlay",
                $"U-42 主题切换快照捕获失败，退化为直切: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 淡出遮罩（约 250ms）并在动画结束后彻底移出视觉树、释放位图引用。
    /// </summary>
    /// <param name="overlay">待移除的遮罩；null 时静默返回。</param>
    public static void FadeOutAndRemove(Image? overlay)
    {
        if (overlay == null) return;
        var animation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(FadeOutMs))
        {
            EasingFunction = EaseOut
        };
        animation.Completed += (_, _) => RemoveFromVisualTree(overlay);
        overlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// 从父面板移除遮罩并断开位图引用（动画完成后调用；窗口已关闭等场景安全降级）。
    /// </summary>
    private static void RemoveFromVisualTree(Image overlay)
    {
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        if (overlay.Parent is Panel parent)
            parent.Children.Remove(overlay);
        overlay.Source = null; // 释放冻结位图引用，帮助 GC 回收大内存块
    }
}
