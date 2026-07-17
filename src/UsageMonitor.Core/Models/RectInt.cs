namespace UsageMonitor.Core.Models;

/// <summary>
/// 屏幕坐标系下的整数矩形（REQ-004）。
/// <para>
/// 用于托盘悬浮窗触发区域（命中矩形）、设置 UI 中"在屏幕上调整"覆盖层显示与拖拽。
/// 与 <c>System.Windows.Int32Rect</c> 区分（后者主要用于图像像素坐标，命名易冲突）：
/// 本结构用 <c>X/Y/Width/Height</c> 四个 <c>int</c> 字段描述"屏幕坐标系下的矩形"，
/// 序列化时与 WPF <c>Rect</c> 兼容、JSON 可读性强。
/// </para>
/// <para>
/// 设计要点：
/// <list type="bullet">
///   <item><description>值类型 + 不可变 record struct：避免 UI 误改内部字段引发不一致。</description></item>
///   <item><description><see cref="ClampToScreen(int, int, int, int)"/>：传入工作区坐标，调用方负责传真实主屏工作区（避免 Core 项目被强绑 WPF/WinForms 引用）。</description></item>
///   <item><description><see cref="Contains"/>：与 <see cref="System.Drawing.Point"/> 互操作，判断光标是否落在矩形内。</description></item>
/// </list>
/// </para>
/// </summary>
public readonly record struct RectInt(int X, int Y, int Width, int Height)
{
    /// <summary>矩形的右边界（不含，X + Width）。</summary>
    public int Right => X + Width;

    /// <summary>矩形的下边界（不含，Y + Height）。</summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// 默认托盘悬浮窗触发区域：屏幕右下角 240×120 像素（基于 1920×1080 主屏兜底）。
    /// <para>
    /// Core 项目无可视化依赖，无法在这里直接读 WPF/WinForms 工作区；实际启动时由
    /// WPF 层调用 <see cref="ClampToScreen(int,int,int,int)"/> 把默认坐标夹回真实主屏工作区。
    /// </para>
    /// </summary>
    public static RectInt DefaultBottomRight()
    {
        // 主屏 1920×1080 兜底；启动后 ClampToScreen 会修正到真实主屏工作区
        const int assumedRight = 1920;
        const int assumedBottom = 1040;
        const int w = 240;
        const int h = 120;
        return new RectInt(assumedRight - w, assumedBottom, w, h);
    }

    /// <summary>命中测试：给定的屏幕坐标点是否落在矩形内（含左边界、不含右边界，与 WPF Rect 语义一致）。</summary>
    /// <param name="px">屏幕 X 坐标。</param>
    /// <param name="py">屏幕 Y 坐标。</param>
    public bool Contains(int px, int py)
        => px >= X && px < Right && py >= Y && py < Bottom;

    /// <summary>命中测试（System.Drawing.Point 重载，兼容 WinForms 坐标）。</summary>
    public bool Contains(System.Drawing.Point p) => Contains(p.X, p.Y);

    /// <summary>
    /// 无工作区参数的兜底夹回：假定主屏 1920×1040。WPF 层启动后应调用带工作区参数的重载做真实夹回。
    /// </summary>
    public RectInt ClampToScreen() => ClampToScreen(0, 0, 1920, 1040);

    /// <summary>
    /// 把矩形夹到指定工作区内（多显示器断开 / 分辨率变更场景）。
    /// 矩形宽高保持不变；若完全位于工作区外则平移到工作区右下角。
    /// </summary>
    /// <param name="workLeft">工作区左边界（含），通常由 WPF <c>SystemParameters.WorkArea.Left</c> 提供。</param>
    /// <param name="workTop">工作区上边界（含）。</param>
    /// <param name="workRight">工作区右边界（不含）。</param>
    /// <param name="workBottom">工作区下边界（不含）。</param>
    public RectInt ClampToScreen(int workLeft, int workTop, int workRight, int workBottom)
    {
        const int minW = 80;  // 与需求文档 §4 一致：最小尺寸 80×60
        const int minH = 60;
        var w = Math.Max(minW, Width);
        var h = Math.Max(minH, Height);

        // 工作区空间不足以容纳矩形 → 直接缩到工作区大小
        var screenW = workRight - workLeft;
        var screenH = workBottom - workTop;
        if (w > screenW) w = screenW;
        if (h > screenH) h = screenH;

        // 完全越界 → 默认回退到工作区右下角
        if (X + w < workLeft || X > workRight || Y + h < workTop || Y > workBottom)
        {
            return new RectInt(workRight - w, workBottom, w, h);
        }

        // 部分越界 → 平移回来，不缩放宽高
        var newX = X;
        var newY = Y;
        if (newX < workLeft) newX = workLeft;
        if (newX + w > workRight) newX = workRight - w;
        if (newY < workTop) newY = workTop;
        if (newY + h > workBottom) newY = workBottom - h;
        return new RectInt(newX, newY, w, h);
    }

    /// <summary>把矩形平移一段距离。</summary>
    public RectInt Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>返回一个新矩形，左上角和宽高按指定值更新。</summary>
    public RectInt With(int? x = null, int? y = null, int? width = null, int? height = null)
        => new(x ?? X, y ?? Y, width ?? Width, height ?? Height);

    /// <summary>JSON 反序列化辅助：把可能为 null 的值兜底成默认值。</summary>
    public static RectInt FromJsonOrDefault(RectInt? value, RectInt fallback)
        => value ?? fallback;

    public override string ToString() => $"X={X},Y={Y},W={Width},H={Height}";
}