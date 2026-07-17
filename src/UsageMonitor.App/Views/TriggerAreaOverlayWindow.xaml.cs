using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Views;

/// <summary>
/// 触发区域调试矩形覆盖窗口：在真实屏幕上 1:1 还原 TrayTriggerWidth/TrayTriggerHeight，
/// 含 8 个拖拽手柄（4 边 + 4 角）实时调整边界，矩形与 SettingsWindow 中的 TextBox 双向同步。
///
/// 设计要点：
/// - 顶级覆盖窗口（WindowStyle=None + AllowsTransparency=True + Background=Transparent），
///   仅 Border 与 8 个 Thumb 接收鼠标事件，其余区域鼠标穿透到下层窗口。
/// - Border 用 HorizontalAlignment=Right + VerticalAlignment=Bottom 对齐屏幕右下角；
///   调整 Width 时左边界自动右移、调整 Height 时上边界自动下移，矩形不需记录 Left/Top。
/// - 拖拽时直接写 _configService.Settings.TrayTriggerWidth/Height + Save()，
///   由 MainViewModel 在 ConfigChanged 时对 TrayTriggerWidth/Height 属性 OnPropertyChanged，
///   TextBox 通过 TwoWay 绑定自动同步。
/// - 外部（TextBox 改动）走 ConfigChanged 路径反向同步矩形大小。
/// </summary>
public partial class TriggerAreaOverlayWindow : Window
{
    private readonly ConfigService _configService;

    /// <summary>边界约束：与 App.IsCursorInTrayArea 的现有下限保持一致（避免设为 0 后永远无法触发）。</summary>
    private const int MinWidth = 20;
    private const int MinHeight = 10;

    public TriggerAreaOverlayWindow(ConfigService configService)
    {
        _configService = configService;
        InitializeComponent();

        // 订阅配置变更：外部（TextBox 改动、其它入口）改 TrayTriggerWidth/Height 时同步矩形大小。
        _configService.ConfigChanged += OnConfigChanged;
        Closed += (_, _) => _configService.ConfigChanged -= OnConfigChanged;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// 窗口加载时：按当前 ConfigService.Settings 中的宽/高设置 Border，
    /// 位置由 HorizontalAlignment=Right + VerticalAlignment=Bottom 自动落到屏幕右下角。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySizeFromConfig();
        FileLogger.Info("TriggerAreaOverlayWindow",
            $"已显示 @ WorkArea=({SystemParameters.WorkArea.Left},{SystemParameters.WorkArea.Top})-({SystemParameters.WorkArea.Right},{SystemParameters.WorkArea.Bottom}) TriggerSize=({_configService.Settings.TrayTriggerWidth}x{_configService.Settings.TrayTriggerHeight})");
    }

    /// <summary>
    /// 8 个 Thumb 共用：根据 Tag 决定如何累加 dx/dy 到 Width/Height。
    /// 矩形对齐方式为 Right+Bottom，所以"左边线"对应 Width、"上边线"对应 Height。
    /// </summary>
    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not string tag) return;

        var newW = _configService.Settings.TrayTriggerWidth;
        var newH = _configService.Settings.TrayTriggerHeight;
        var dx = e.HorizontalChange;
        var dy = e.VerticalChange;

        switch (tag)
        {
            case "Left":
            case "TopLeft":
            case "BottomLeft":
                // 拖动左边界或左下角：dx>0 表示左边界右移 → Width 减小
                newW = newW - dx;
                break;
            case "Right":
            case "TopRight":
            case "BottomRight":
                // 拖动右边界或右下角：dx>0 表示右边界右移 → Width 增大
                newW = newW + dx;
                break;
        }

        switch (tag)
        {
            case "Top":
            case "TopLeft":
            case "TopRight":
                // 拖动上边界或上角：dy>0 表示上边界下移 → Height 减小
                newH = newH - dy;
                break;
            case "Bottom":
            case "BottomLeft":
            case "BottomRight":
                // 拖动下边界或下角：dy>0 表示下边界下移 → Height 增大
                newH = newH + dy;
                break;
        }

        // 边界约束
        newW = Math.Max(MinWidth, newW);
        newH = Math.Max(MinHeight, newH);

        // 直写 ConfigService 并 Save：MainViewModel 会通过 ConfigChanged 路径通知 TextBox
        _configService.Settings.TrayTriggerWidth = (int)Math.Round(newW);
        _configService.Settings.TrayTriggerHeight = (int)Math.Round(newH);
        _configService.Save();

        // 立即更新 Border（不等 ConfigChanged 反馈，因为拖拽中需要顺滑）
        TriggerBorder.Width = _configService.Settings.TrayTriggerWidth;
        TriggerBorder.Height = _configService.Settings.TrayTriggerHeight;
    }

    /// <summary>
    /// 外部（TextBox 改动、其它入口）触发 ConfigChanged 时，按当前 Settings 同步 Border 尺寸。
    /// 不会写回 Settings，避免循环。
    /// </summary>
    private void OnConfigChanged(object? sender, EventArgs e)
    {
        ApplySizeFromConfig();
    }

    private void ApplySizeFromConfig()
    {
        var w = Math.Max(MinWidth, _configService.Settings.TrayTriggerWidth);
        var h = Math.Max(MinHeight, _configService.Settings.TrayTriggerHeight);
        if (TriggerBorder.Width != w) TriggerBorder.Width = w;
        if (TriggerBorder.Height != h) TriggerBorder.Height = h;
    }
}
