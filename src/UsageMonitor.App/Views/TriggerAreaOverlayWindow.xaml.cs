using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Views;

/// <summary>
/// REQ-004/006：托盘悬浮窗触发区域调试遮罩。
/// <para>
/// 覆盖完整虚拟屏（含任务栏）+ 当前 <see cref="RectInt"/>（X/Y/W/H）边框 / 填充 +
/// 8 个 Thumb（4 边 + 4 角）按固定对侧基点调整边界 + 矩形内可整体拖动；Esc 或点击蒙版空白退出；
/// 拖动 / 缩放 MouseUp 后 500ms 防抖写入 <see cref="AppSettings.TrayTooltipTriggerRect"/>，
/// SettingsWindow 的 4 个 TextBox 通过 <see cref="ConfigService.ConfigChanged"/> 双向同步。
/// </para>
/// <para>
/// 与 v1 版本（仅 Width/Height 调整）的区别：本控件全面切到 <see cref="RectInt"/> X/Y/W/H 模型，
/// 配合 <see cref="App.IsCursorInTrayArea"/> 改用 <see cref="RectInt.Contains"/> 后整体一致；
/// 保留多屏断开时 <see cref="RectInt.ClampToScreen(int,int,int,int)"/> 兜底。
/// </para>
/// </summary>
public partial class TriggerAreaOverlayWindow : Window
{
    private readonly ConfigService _configService;

    /// <summary>REQ-006：触发区域最小宽度为 10 像素。</summary>
    private const int MinRectWidth = 10;
    /// <summary>REQ-006：触发区域最小高度为 10 像素。</summary>
    private const int MinRectHeight = 10;

    /// <summary>REQ-004 §5：拖动 / 缩放完成后 500ms 防抖写入配置（req-063 B8：一次性订阅，避免高频拖拽时连续 new 大量 timer）。</summary>
    private DispatcherTimer _saveRectTimer;

    /// <summary>整矩形拖动的起点状态（屏幕光标位置 + 起始 Rect）。</summary>
    private System.Drawing.Point _moveStartCursorScreen;
    private RectInt _moveStartRect;
    private bool _isMoving;

    /// <summary>创建覆盖窗口。订阅 ConfigService 事件以让外部改动反向同步到 UI。</summary>
    public TriggerAreaOverlayWindow(ConfigService configService)
    {
        _configService = configService;
        InitializeComponent();

        // req-063 B8：防抖保存 timer 一次性订阅，避免高频拖拽时连续 new 大量 timer
        _saveRectTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveRectTimer.Tick += (_, _) =>
        {
            _saveRectTimer.Stop();
            try
            {
                _configService.Save();
                FileLogger.Info("TriggerAreaOverlayWindow",
                    $"TriggerRect 已落盘: {_configService.Settings.TrayTooltipTriggerRect}");
            }
            catch (Exception ex)
            {
                FileLogger.Error("TriggerAreaOverlayWindow", "防抖保存 TriggerRect 失败", ex);
            }
        };

        _configService.ConfigChanged += OnConfigChanged;
        // REQ-006：Closed 也需兑底落盘，否则在 500ms 防抖窗口内被 Alt+F4 或外部 Close 绕过会丢本次拖动。
        // FlushPendingSave 内部已 Stop + 释放 timer 引用，避免 DispatcherTimer 闭包循环。
        Closed += (_, _) =>
        {
            FlushPendingSave();
            _saveRectTimer = null!;
            _configService.ConfigChanged -= OnConfigChanged;
        };

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>窗口加载：按当前配置应用矩形，并记录完整虚拟屏边界。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyRectFromConfig();
        var bounds = GetVirtualScreenBounds();
        FileLogger.Info("TriggerAreaOverlayWindow",
            $"已显示 VirtualScreen=({bounds.Left},{bounds.Top})-({bounds.Right},{bounds.Bottom}) TriggerRect={_configService.Settings.TrayTooltipTriggerRect}");
    }

    // =====================================================
    // 数据源 → UI：ConfigService 改动时同步给 TriggerBorder
    // =====================================================

    /// <summary>外部（TextBox 改动、其它入口）改 TriggerRect 时同步矩形尺寸 / 位置。</summary>
    // 死锁防护：ConfigChanged 可能由后台刷新线程在持有 ConfigService 锁时触发，用 BeginInvoke 异步投递避免交叉死锁。
    private void OnConfigChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(ApplyRectFromConfig));

    /// <summary>把当前 <see cref="AppSettings.TrayTooltipTriggerRect"/> 换算为虚拟屏 Canvas 局部坐标并刷新标签；不修改设置中的原始值，避免“打开蒙版即迁移”。</summary>
    private void ApplyRectFromConfig()
    {
        var configured = _configService.Settings.TrayTooltipTriggerRect;
        var r = ClampToVirtualScreen(configured);

        var bounds = GetVirtualScreenBounds();
        Canvas.SetLeft(TriggerBorder, r.X - bounds.Left);
        Canvas.SetTop(TriggerBorder, r.Y - bounds.Top);
        TriggerBorder.Width = r.Width;
        TriggerBorder.Height = r.Height;
        CoordsLabel.Text = $"X={r.X} Y={r.Y} W={r.Width} H={r.Height}（最小 {MinRectWidth}×{MinRectHeight}）";
    }

    // =====================================================
    // UI → 数据源：Thumb 拖拽（4 边 + 4 角）
    // =====================================================

    /// <summary>
    /// REQ-006：8 个 Thumb 共用缩放入口。拖动边按 VirtualScreen 与 10px 下限钳制，
    /// 对侧边始终作为固定基点；达到下限后继续向内拖动时矩形保持不动。
    /// </summary>
    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not string tag) return;

        var configured = _configService.Settings.TrayTooltipTriggerRect;
        var r = ClampToVirtualScreen(configured);
        // 只用钳制后的矩形作为缩放参考；不直接写回 Settings，以保留用户原本的坐标。
        var bounds = GetVirtualScreenBounds();
        var left = r.X;
        var top = r.Y;
        var right = r.Right;
        var bottom = r.Bottom;

        switch (tag)
        {
            case "Left":
            case "TopLeft":
            case "BottomLeft":
                left = (int)Math.Round(Math.Clamp(r.X + e.HorizontalChange,
                    bounds.Left, right - MinRectWidth));
                break;
            case "Right":
            case "TopRight":
            case "BottomRight":
                right = (int)Math.Round(Math.Clamp(r.Right + e.HorizontalChange,
                    left + MinRectWidth, bounds.Right));
                break;
        }

        switch (tag)
        {
            case "Top":
            case "TopLeft":
            case "TopRight":
                top = (int)Math.Round(Math.Clamp(r.Y + e.VerticalChange,
                    bounds.Top, bottom - MinRectHeight));
                break;
            case "Bottom":
            case "BottomLeft":
            case "BottomRight":
                bottom = (int)Math.Round(Math.Clamp(r.Bottom + e.VerticalChange,
                    top + MinRectHeight, bounds.Bottom));
                break;
        }

        var updated = new RectInt(left, top, right - left, bottom - top);
        if (updated == r) return;

        ApplyRectToConfig(updated);
        ApplyRectFromConfig();
    }

    /// <summary>REQ-006：Thumb 释放后安排 500ms 防抖保存，保持缩放与整体拖动的持久化语义一致。</summary>
    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e) => ScheduleSave();

    // =====================================================
    // 整矩形移动：按下 Border 内部任意位置拖动
    // =====================================================

    /// <summary>REQ-004 §4：在矩形内部按下时启动"整矩形拖动"（不是点击 8 个 Thumb）。</summary>
    private void OnBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 忽略来自 8 个 Thumb 的命中（Thumb 会自行处理 DragDelta/鼠标捕获）
        if (e.OriginalSource is DependencyObject src && FindThumbAncestor(src) != null) return;
        _isMoving = true;
        _moveStartCursorScreen = System.Windows.Forms.Cursor.Position;
        _moveStartRect = _configService.Settings.TrayTooltipTriggerRect;
        ((IInputElement)sender).CaptureMouse();
        e.Handled = true;
    }

    /// <summary>REQ-006：拖动中用光标 delta 累加到 Rect.X/Y，并把整体矩形限制在完整虚拟屏内。</summary>
    private void OnBorderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isMoving) return;
        var cur = System.Windows.Forms.Cursor.Position;
        var dx = cur.X - _moveStartCursorScreen.X;
        var dy = cur.Y - _moveStartCursorScreen.Y;
        var moved = new RectInt(_moveStartRect.X + dx, _moveStartRect.Y + dy,
                                _moveStartRect.Width, _moveStartRect.Height);
        moved = ClampToVirtualScreen(moved);
        ApplyRectToConfig(moved);
        ApplyRectFromConfig();
    }

    /// <summary>REQ-004 §4：释放鼠标 → 停止拖动 + 安排 500ms 防抖写盘。</summary>
    private void OnBorderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMoving) return;
        _isMoving = false;
        ((IInputElement)sender).ReleaseMouseCapture();
        ScheduleSave();
    }

    /// <summary>祖先链上找是否包含某个 Thumb（用于忽略 Thumb 点击）。</summary>
    private static Thumb? FindThumbAncestor(DependencyObject d)
    {
        var cur = d;
        while (cur != null)
        {
            if (cur is Thumb t) return t;
            cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    // =====================================================
    // 退出（Esc / 蒙版空白点击 / 双击矩形）
    // =====================================================

    /// <summary>蒙版空白单击：直接关闭（用户最直观的退出方式）。</summary>
    private void OnMaskMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 点击蒙版空白（非 Border）→ 关闭遮罩并立即保存（不等待 500ms 防抖，用户主动退出意味着确认）
        FlushPendingSave();
        Close();
    }

    /// <summary>Esc 键退出（同上立即保存后关闭）。</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FlushPendingSave();
            Close();
            e.Handled = true;
        }
    }

    /// <summary>req-080 U-38：窗口级 KeyDown 处理（补充 PreviewKeyDown）。</summary>
    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FlushPendingSave();
            Close();
            e.Handled = true;
        }
    }

    /// <summary>req-080 U-38：取消按钮点击——不保存当前拖动结果，直接关闭。</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // 取消：停止防抖计时器，不保存 pending 的拖动结果，直接关闭
        _saveRectTimer.Stop();
        Close();
    }

    // =====================================================
    // 写入：500ms 防抖 + 立即落盘
    // =====================================================

    /// <summary>把内存矩形更新到 ConfigService（不动触发 ConfigChanged，立即同步给 Border 即可）。</summary>
    private void ApplyRectToConfig(RectInt r)
    {
        // 直接写 Settings 不 Save，由防抖或关闭兜底同步，避免写盘抖动。
        _configService.Settings.TrayTooltipTriggerRect = r;
    }

    /// <summary>REQ-004 §5：拖动 / 缩放结束（MouseUp）→ 500ms 后再 Save 一次配置（与 TrayTooltipWindow 防抖方案一致）。req-063 B8：timer 已一次性订阅，此处仅 Stop/Start 复用。</summary>
    private void ScheduleSave()
    {
        _saveRectTimer.Stop();
        _saveRectTimer.Start();
    }

    /// <summary>用户主动退出时停止防抖计时并立即落盘；即使尚未建立计时器也保存当前内存矩形。</summary>
    private void FlushPendingSave()
    {
        _saveRectTimer.Stop();
        try
        {
            _configService.Save();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("TriggerAreaOverlayWindow", "退出时强制保存失败（不影响关闭）", ex);
        }
    }

    /// <summary>获取完整虚拟屏整数边界；WPF 指标不可用时回退到 WinForms 虚拟屏，再回退到 1920×1080。</summary>
    private static (int Left, int Top, int Right, int Bottom) GetVirtualScreenBounds()
    {
        try
        {
            var left = (int)Math.Round(SystemParameters.VirtualScreenLeft);
            var top = (int)Math.Round(SystemParameters.VirtualScreenTop);
            var right = (int)Math.Round(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth);
            var bottom = (int)Math.Round(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);
            if (right - left >= MinRectWidth && bottom - top >= MinRectHeight)
            {
                return (left, top, right, bottom);
            }
        }
        catch
        {
            // 继续使用 WinForms 完整虚拟屏兜底。
        }

        var fallback = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (fallback.Width >= MinRectWidth && fallback.Height >= MinRectHeight)
        {
            return (fallback.Left, fallback.Top, fallback.Right, fallback.Bottom);
        }

        return (0, 0, 1920, 1080);
    }

    /// <summary>把整个触发区域平移并夹回完整虚拟屏；仅用于加载归一化与整体拖动，不用于 Thumb 缩放。</summary>
    private static RectInt ClampToVirtualScreen(RectInt r)
    {
        var bounds = GetVirtualScreenBounds();
        return r.ClampToScreen(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }
}
