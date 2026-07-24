using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UsageMonitor.App.Helpers
{
    /// <summary>
    /// 附加属性：让外层 ScrollViewer 接管内层可滚动控件“滚到边界后”的鼠标滚轮事件。
    /// <para>
    /// 典型场景：设置窗口外层 ScrollViewer 内含 ListBox / 嵌套 ScrollViewer 等可滚动子控件，
    /// 内层控件将 MouseWheel 恒置 Handled，导致外层永远无法滚动。
    /// 启用本属性后，外层在 PreviewMouseWheel 中判断内层是否仍可滚：
    /// 可滚 → 放行（内层正常消费）；不可滚 → 外层自行滚动并吞掉事件。
    /// </para>
    /// <para>已知限制：
    /// <list type="bullet">
    ///   <item><description>仅处理垂直方向滚动；横向滚动（Shift+滚轮 / 水平滚轮）不做边界判断与接管。</description></item>
    ///   <item><description>多层嵌套宿主时，边界接管只上抛一层（最近的内层 ScrollViewer 到边界即由当前宿主消费，不会继续向上层宿主传递）。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class ScrollViewerMouseWheelAssist
    {
        /// <summary>标识 EnableBubbling 附加属性。</summary>
        public static readonly DependencyProperty EnableBubblingProperty =
            DependencyProperty.RegisterAttached(
                "EnableBubbling",
                typeof(bool),
                typeof(ScrollViewerMouseWheelAssist),
                new PropertyMetadata(false, OnEnableBubblingChanged));

        /// <summary>获取指定 ScrollViewer 是否启用滚轮冒泡转发。</summary>
        public static bool GetEnableBubbling(DependencyObject obj) => (bool)obj.GetValue(EnableBubblingProperty);

        /// <summary>设置指定 ScrollViewer 是否启用滚轮冒泡转发。</summary>
        public static void SetEnableBubbling(DependencyObject obj, bool value) => obj.SetValue(EnableBubblingProperty, value);

        /// <summary>
        /// 属性变更回调：true 时订阅 PreviewMouseWheel，false 时退订（支持运行时 Detach）。
        /// </summary>
        private static void OnEnableBubblingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer host) return;

            if ((bool)e.NewValue)
                host.PreviewMouseWheel += OnHostPreviewMouseWheel;
            else
                host.PreviewMouseWheel -= OnHostPreviewMouseWheel;
        }

        /// <summary>
        /// 宿主 ScrollViewer 的 PreviewMouseWheel 处理：
        /// 从事件源向上查找最近的内层 ScrollViewer；
        /// 若内层在滚动方向上仍有空间则放行，否则由宿主消费滚轮。
        /// </summary>
        private static void OnHostPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var host = (ScrollViewer)sender;

            // 从事件原始源沿视觉树向上找最近的内层 ScrollViewer（跳过宿主本身）
            var inner = FindInnerScrollViewer(e.OriginalSource as DependencyObject, host);

            if (inner != null && CanScrollInDirection(inner, e.Delta))
            {
                // 内层仍可滚动 → 放行，让内层正常消费
                return;
            }

            // 内层不存在或已滚到边界 → 宿主接管滚动
            // Phase0 评审修复：对齐原生滚轮步长（Delta/120 × 系统滚轮行数 × 16px/行），
            // 避免原始 Delta（120px/格）比原生快约 2.5 倍且无视系统设置
            var step = e.Delta / 120.0 * SystemParameters.WheelScrollLines * 16.0;
            host.ScrollToVerticalOffset(host.VerticalOffset - step);
            e.Handled = true;
        }

        /// <summary>
        /// 从 <paramref name="source"/> 沿视觉树向上查找第一个位于宿主内部的 ScrollViewer。
        /// 可覆盖 ListBox / ListView / DataGrid 等 ItemsControl 内嵌的 ScrollViewer。
        /// </summary>
        private static ScrollViewer? FindInnerScrollViewer(DependencyObject? source, ScrollViewer host)
        {
            var current = source;
            while (current != null && current != host)
            {
                if (current is ScrollViewer sv)
                    return sv;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// 判断 ScrollViewer 在指定滚轮方向上是否仍有可滚动空间。
        /// Delta &lt; 0 表示向下滚（需要 VerticalOffset &lt; ScrollableHeight）；
        /// Delta &gt; 0 表示向上滚（需要 VerticalOffset &gt; 0）。
        /// </summary>
        private static bool CanScrollInDirection(ScrollViewer sv, int delta)
        {
            if (delta < 0)
                return sv.VerticalOffset < sv.ScrollableHeight;
            if (delta > 0)
                return sv.VerticalOffset > 0;
            return false;
        }
    }
}
