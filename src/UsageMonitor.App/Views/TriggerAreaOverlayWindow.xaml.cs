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
/// REQ-004：托盘悬浮窗触发区域调试遮罩。
/// <para>
/// 整屏半透明蒙版 + 当前 <see cref="RectInt"/>（X/Y/W/H）边框 / 填充 +
/// 8 个 Thumb（4 边 + 4 角）调整边界 + 矩形内可整体拖动；Esc 或点击蒙版空白退出；
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

    /// <summary>REQ-004 §1：最小尺寸（与需求文档一致）。</summary>
    private const int MinRectWidth = 80;
    /// <summary>REQ-004 §1：最小尺寸。</summary>
    private const int MinRectHeight = 60;

    /// <summary>REQ-004 §5：拖动 / 缩放完成后 500ms 防抖写入配置。</summary>
    private DispatcherTimer? _saveRectTimer;

    /// <summary>整矩形拖动的起点状态（屏幕光标位置 + 起始 Rect）。</summary>
    private System.Drawing.Point _moveStartCursorScreen;
    private RectInt _moveStartRect;
    private bool _isMoving;

    /// <summary>创建覆盖窗口。订阅 ConfigService 事件以让外部改动反向同步到 UI。</summary>
    public TriggerAreaOverlayWindow(ConfigService configService)
    {
        _configService = configService;
        InitializeComponent();

        _configService.ConfigChanged += OnConfigChanged;
        Closed += (_, _) =>
        {
            _saveRectTimer?.Stop();
            _configService.ConfigChanged -= OnConfigChanged;
        };

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>窗口加载：按当前配置应用矩形位置 / 大小 / 标签。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyRectFromConfig();
        FileLogger.Info("TriggerAreaOverlayWindow",
            $"已显示 WorkArea=({SystemParameters.WorkArea.Left},{SystemParameters.WorkArea.Top})-({SystemParameters.WorkArea.Right},{SystemParameters.WorkArea.Bottom}) TriggerRect={_configService.Settings.TrayTooltipTriggerRect}");
    }

    // =====================================================
    // 数据源 → UI：ConfigService 改动时同步给 TriggerBorder
    // =====================================================

    /// <summary>外部（TextBox 改动、其它入口）改 TriggerRect 时同步矩形尺寸 / 位置。</summary>
    private void OnConfigChanged(object? sender, EventArgs e) => Dispatcher.Invoke(ApplyRectFromConfig);

    /// <summary>把当前 <see cref="AppSettings.TrayTooltipTriggerRect"/> 落到 TriggerBorder 上 + 坐标标签。</summary>
    private void ApplyRectFromConfig()
    {
        var r = _configService.Settings.TrayTooltipTriggerRect;
        // 应用最小尺寸（防止脏数据让 Rect 缩成 0）
        var w = Math.Max(MinRectWidth, r.Width);
        var h = Math.Max(MinRectHeight, r.Height);

        // 通过夹回避免越屏（多屏断开后原 Rect 可能在新屏幕外）
        r = r.ClampToScreen((int)SystemParameters.WorkArea.Left,
                            (int)SystemParameters.WorkArea.Top,
                            (int)SystemParameters.WorkArea.Right,
                            (int)SystemParameters.WorkArea.Bottom);

        Canvas.SetLeft(TriggerBorder, r.X);
        Canvas.SetTop(TriggerBorder, r.Y);
        TriggerBorder.Width = r.Width;
        TriggerBorder.Height = r.Height;
        CoordsLabel.Text = $"X={r.X} Y={r.Y} W={r.Width} H={r.Height}（最小 {MinRectWidth}×{MinRectHeight}）";
    }

    // =====================================================
    // UI → 数据源：Thumb 拖拽（4 边 + 4 角）
    // =====================================================

    /// <summary>REQ-004 §4：8 个 Thumb 共用入口；按 Tag 决定修改 X/Y/W/H 哪些字段。</summary>
    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not string tag) return;

        var r = _configService.Settings.TrayTooltipTriggerRect;
        var dx = e.HorizontalChange;
        var dy = e.VerticalChange;
        // 整型夹回前统一用 double 做累加，规避 DragDeltaEventArgs.{Horizontal,Vertical}Change 的 double
        // 与整型 RectInt 在分支里混用时反复来回强转。
        var newX = (double)r.X;
        var newY = (double)r.Y;
        var newW = (double)r.Width;
        var newH = (double)r.Height;

        // 边 / 角调整 W（dx 方向）
        switch (tag)
        {
            case "Left":
            case "TopLeft":
            case "BottomLeft":
                // 拖动左边界或左侧角 → dx>0 使左边界右移 → Width 减小
                newW = System.Math.Max(MinRectWidth, newW - dx);
                if (newW <= MinRectWidth + 0.5 && dx > 0)
                {
                    // 到达下限后，再用 X 平移代替缩放（拖动手感连续）
                    newX = (double)r.X + dx;
                    newW = r.Width;
                }
                break;
            case "Right":
            case "TopRight":
            case "BottomRight":
                newW = System.Math.Max(MinRectWidth, newW + dx);
                break;
        }

        // 边 / 角调整 H（dy 方向）
        switch (tag)
        {
            case "Top":
            case "TopLeft":
            case "TopRight":
                newH = System.Math.Max(MinRectHeight, newH - dy);
                if (newH <= MinRectHeight + 0.5 && dy > 0)
                {
                    newY = (double)r.Y + dy;
                    newH = r.Height;
                }
                break;
            case "Bottom":
            case "BottomLeft":
            case "BottomRight":
                newH = System.Math.Max(MinRectHeight, newH + dy);
                break;
        }

        var updated = new RectInt((int)System.Math.Round(newX), (int)System.Math.Round(newY),
                                  (int)System.Math.Round(newW), (int)System.Math.Round(newH));
        updated = ClampToWorkArea(updated);
        ApplyRectToConfig(updated);
        ApplyRectFromConfig(); // 立即反馈视觉
    }

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

    /// <summary>REQ-004 §4：拖动中用光标 delta 累加到 Rect.X/Y；夹回工作区。</summary>
    private void OnBorderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isMoving) return;
        var cur = System.Windows.Forms.Cursor.Position;
        var dx = cur.X - _moveStartCursorScreen.X;
        var dy = cur.Y - _moveStartCursorScreen.Y;
        var moved = new RectInt(_moveStartRect.X + dx, _moveStartRect.Y + dy,
                                _moveStartRect.Width, _moveStartRect.Height);
        moved = ClampToWorkArea(moved);
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

    // =====================================================
    // 写入：500ms 防抖 + 立即落盘
    // =====================================================

    /// <summary>把内存矩形更新到 ConfigService（不动触发 ConfigChanged，立即同步给 Border 即可）。</summary>
    private void ApplyRectToConfig(RectInt r)
    {
        // 直接写 Settings 不 Save，由防抖或关闭兜底同步，避免写盘抖动。
        _configService.Settings.TrayTooltipTriggerRect = r;
    }

    /// <summary>REQ-004 §5：拖动 / 缩放结束（MouseUp）→ 500ms 后再 Save 一次配置（与 TrayTooltipWindow 防抖方案一致）。</summary>
    private void ScheduleSave()
    {
        _saveRectTimer?.Stop();
        _saveRectTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveRectTimer.Tick += (_, _) =>
        {
            _saveRectTimer?.Stop();
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
        _saveRectTimer.Start();
    }

    /// <summary>用户主动退出时立即落盘（不等防抖）。</summary>
    private void FlushPendingSave()
    {
        if (_saveRectTimer == null) return;
        _saveRectTimer.Stop();
        _saveRectTimer = null;
        try
        {
            _configService.Save();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("TriggerAreaOverlayWindow", "退出时强制保存失败（不影响关闭）", ex);
        }
    }

    /// <summary>把 Rect 夹回主屏工作区（多屏断开 / DPI 变更兜底）。</summary>
    private RectInt ClampToWorkArea(RectInt r)
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            return r.ClampToScreen((int)wa.Left, (int)wa.Top, (int)wa.Right, (int)wa.Bottom);
        }
        catch
        {
            return r.ClampToScreen();
        }
    }
}
