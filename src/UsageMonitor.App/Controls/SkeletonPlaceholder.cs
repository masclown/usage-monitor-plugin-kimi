using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace UsageMonitor.App.Controls;

/// <summary>
/// req-079 U-33：骨架屏占位控件——灰色圆角矩形 + 微光扫过动画。
/// <para>
/// 用于 TaskbarWindow 等高频重绘区域的"数据尚未就绪"占位，避免空白闪烁。
/// 性能设计（任务栏窗口为高频重绘区域）：
/// - 微光渐变画刷在静态构造中创建并 <see cref="Freezable.Freeze"/> 冻结，全实例共享、逐帧零变更；
/// - 动画仅驱动微光条的 <see cref="TranslateTransform"/>（轻量级渲染变换），由 Storyboard 承载；
/// - 控件不可见时（数据就绪后骨架被移除）自动暂停动画，避免空转消耗；
/// - 控件始终 IsHitTestVisible=false，不拦截任务栏拖拽与滚轮事件。
/// </para>
/// </summary>
public class SkeletonPlaceholder : Border
{
    /// <summary>微光单次扫过时长（毫秒）。</summary>
    private const int ShimmerDurationMs = 1200;

    /// <summary>微光条宽度（DIP）。</summary>
    private const double ShimmerBarWidth = 48;

    /// <summary>触发微光动画重建的宽度变化阈值（DIP）：变化量超过此值时重建扫过动画，避免微光终点固化在旧宽度。</summary>
    private const double RebuildWidthThreshold = 4;

    /// <summary>共享的冻结微光渐变画刷（低透明白色高光，深浅主题下均表现为柔和提亮）。</summary>
    private static readonly LinearGradientBrush ShimmerBrush = CreateFrozenShimmerBrush();

    /// <summary>微光条矩形实例（懒创建，尺寸就绪后生成）。</summary>
    private Rectangle? _shimmerBar;

    /// <summary>微光扫过循环 Storyboard（随控件可见性启停）。</summary>
    private Storyboard? _storyboard;

    /// <summary>
    /// 初始化骨架占位：主题资源 TrackBrush 背景、圆角、内容裁剪、不参与命中测试。
    /// </summary>
    public SkeletonPlaceholder()
    {
        // 以资源引用方式取主题轨道画刷（等价于 XAML 的 {DynamicResource TrackBrush}，随主题切换刷新）
        SetResourceReference(BackgroundProperty, "TrackBrush");
        CornerRadius = new CornerRadius(3);
        ClipToBounds = true;
        IsHitTestVisible = false; // 骨架屏不拦截鼠标事件（不影响任务栏拖拽 / 滚轮切组）
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 创建并冻结微光渐变画刷（透明 → 约 12% 白 → 透明，水平方向）。
    /// </summary>
    private static LinearGradientBrush CreateFrozenShimmerBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 尺寸就绪后构建微光条与循环 Storyboard（可见时立即启动）；
    /// 宽度变化超过阈值（<see cref="RebuildWidthThreshold"/>）时销毁旧动画并按新宽度重建，
    /// 避免扫过终点固化在首次创建时的容器宽度。
    /// </summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.NewSize.Width <= 0 || sizeInfo.NewSize.Height <= 0) return;

        // 宽度显著变化：停止旧 Storyboard 并销毁微光条，由 EnsureShimmer 按新宽度重建
        if (_shimmerBar != null &&
            Math.Abs(sizeInfo.NewSize.Width - sizeInfo.PreviousSize.Width) > RebuildWidthThreshold)
        {
            ResetShimmer();
        }

        EnsureShimmer();
    }

    /// <summary>
    /// 停止并释放当前微光 Storyboard 与微光条（宽度显著变化时调用，随后由 <see cref="EnsureShimmer"/> 重建）。
    /// </summary>
    private void ResetShimmer()
    {
        _storyboard?.Stop();
        _storyboard = null;
        _shimmerBar = null;
        Child = null;
    }

    /// <summary>
    /// 懒创建微光条（冻结画刷 + 左对齐矩形）与扫过 Storyboard。幂等：重复调用不重复创建。
    /// </summary>
    private void EnsureShimmer()
    {
        if (_shimmerBar != null || ActualWidth <= 0) return;
        _shimmerBar = new Rectangle
        {
            Width = ShimmerBarWidth,
            Fill = ShimmerBrush,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            RenderTransform = new TranslateTransform(-ShimmerBarWidth, 0)
        };
        Child = _shimmerBar;

        // 扫过动画：从容器左侧外平移至右侧外，循环播放
        var sweep = new DoubleAnimation
        {
            From = -ShimmerBarWidth,
            To = ActualWidth + ShimmerBarWidth,
            Duration = TimeSpan.FromMilliseconds(ShimmerDurationMs)
        };
        Storyboard.SetTarget(sweep, _shimmerBar);
        Storyboard.SetTargetProperty(sweep,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        _storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        _storyboard.Children.Add(sweep);
        if (IsVisible) _storyboard.Begin();
    }

    /// <summary>
    /// 可见性变化：可见 → 启动扫过；不可见（数据就绪骨架移除 / 窗口最小化）→ 暂停避免空转。
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_storyboard == null) return;
        if (IsVisible) _storyboard.Begin();
        else _storyboard.Pause();
    }

    /// <summary>
    /// 从视觉树移除时停止动画（防止时钟脱离视觉树后仍运行）。
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _storyboard?.Stop();
    }
}
