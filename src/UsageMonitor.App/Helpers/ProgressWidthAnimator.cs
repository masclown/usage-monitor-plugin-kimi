using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-081 U-41：进度条宽度平滑动画行为（附加属性）。
/// <para>
/// 用法：在 XAML 中把目标宽度绑定到 <see cref="TargetWidthProperty"/>（取代直接绑定 Border.Width），
/// 本行为在目标值变化时从当前渲染宽度启动 300ms <see cref="DoubleAnimation"/>（CubicEase EaseOut），
/// 平滑过渡到目标值，覆盖主窗口卡片全部进度条实例（含双进度条卡片与 SDK v2 MetricBar 模板）。
/// </para>
/// <para>
/// 方案选择理由：WPF 中 Border.Width 由 Binding 驱动时无法叠加隐式动画（绑定占据属性值来源，
/// 动画与绑定互相抢占）；改为附加属性接收目标值后，Width 完全由动画 / 本地值控制，无冲突。
/// 相较 ScaleTransform.ScaleX 方案：附加属性方案保持圆角几何不变形（ScaleX 会拉伸圆角），
/// 且无需处理 RenderTransformOrigin 与裁剪问题；进度条数量少（≤6/卡片），Width 动画的布局开销可忽略。
/// </para>
/// <para>
/// 边界行为：首次赋值（窗口加载 / 模板首次应用）直接写入不播放动画，避免开机从 0 扫满；
/// 0% / 100% 边界正常收敛；窗口尺寸变化时 MultiBinding 重算目标宽度，同样平滑过渡。
/// </para>
/// </summary>
public static class ProgressWidthAnimator
{
    /// <summary>动画时长（毫秒），参考 Material Design 进度变化推荐（约 300ms）。</summary>
    private const int DurationMs = 300;

    /// <summary>共享的冻结缓动函数（CubicEase EaseOut：起步快、收尾柔和）。</summary>
    private static readonly CubicEase EaseOut = CreateFrozenEase();

    /// <summary>创建并冻结 CubicEase EaseOut 缓动函数（全进度条共享，Freezable 冻结后免运行时校验）。</summary>
    private static CubicEase CreateFrozenEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    /// <summary>目标宽度附加属性（XAML 绑定百分比 × 容器宽度的换算结果）。</summary>
    public static readonly DependencyProperty TargetWidthProperty =
        DependencyProperty.RegisterAttached(
            "TargetWidth",
            typeof(double),
            typeof(ProgressWidthAnimator),
            new PropertyMetadata(0.0, OnTargetWidthChanged));

    /// <summary>获取目标宽度。</summary>
    public static double GetTargetWidth(DependencyObject obj) => (double)obj.GetValue(TargetWidthProperty);

    /// <summary>设置目标宽度（通常由 XAML 绑定调用）。</summary>
    public static void SetTargetWidth(DependencyObject obj, double value) => obj.SetValue(TargetWidthProperty, value);

    /// <summary>私有附加属性：标记是否已完成首次赋值（首次直接写入、不播放动画）。</summary>
    private static readonly DependencyProperty InitializedProperty =
        DependencyProperty.RegisterAttached(
            "Initialized",
            typeof(bool),
            typeof(ProgressWidthAnimator),
            new PropertyMetadata(false));

    /// <summary>读取首次赋值标记。</summary>
    private static bool GetInitialized(DependencyObject obj) => (bool)obj.GetValue(InitializedProperty);

    /// <summary>写入首次赋值标记。</summary>
    private static void SetInitialized(DependencyObject obj, bool value) => obj.SetValue(InitializedProperty, value);

    /// <summary>
    /// 目标宽度变化回调：首次赋值直接写入；后续变化从当前渲染宽度启动 300ms 平滑过渡。
    /// </summary>
    private static void OnTargetWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        var target = (double)e.NewValue;
        if (double.IsNaN(target) || double.IsInfinity(target) || target < 0) target = 0;

        // 首次赋值（窗口加载 / 模板首次应用）：直接写入，避免进度条开机从 0 扫满
        if (!GetInitialized(border))
        {
            SetInitialized(border, true);
            border.BeginAnimation(FrameworkElement.WidthProperty, null);
            border.Width = target;
            return;
        }

        // 变化量极小（亚像素）：直接写入，省去动画开销
        var from = border.ActualWidth;
        if (Math.Abs(from - target) < 0.5)
        {
            border.BeginAnimation(FrameworkElement.WidthProperty, null);
            border.Width = target;
            return;
        }

        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(DurationMs))
        {
            EasingFunction = EaseOut
        };
        // 自然结束后：清除动画时钟并把目标值落地为本地值，避免 HoldEnd 时钟残留
        animation.Completed += (_, _) =>
        {
            border.BeginAnimation(FrameworkElement.WidthProperty, null);
            border.Width = target;
        };
        // SnapshotAndReplace：快速连续变化（如窗口拖拽缩放）时以当前动画值快照为新起点，保证视觉连续
        border.BeginAnimation(FrameworkElement.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
