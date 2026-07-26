using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 修复6：屏幕取色器——全屏透明覆盖层，鼠标移动时实时显示光标处像素颜色（HEX 预览），
/// 点击确认取色，Escape / 右键取消。
/// <para>
/// 实现原理：打开前截取全屏位图，覆盖层展示截图 + 放大镜色块 + HEX 文本；
/// 点击时从截图中读取像素颜色返回给调用方。
/// </para>
/// </summary>
public static class ScreenColorPicker
{
    /// <summary>
    /// 弹出屏幕取色覆盖层，返回用户选中的颜色；取消时返回 null。
    /// </summary>
    public static Color? PickColor()
    {
        // 截取全屏位图
        var (bitmap, screenW, screenH) = CaptureScreen();
        if (bitmap == null) return null;

        Color? result = null;

        var overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Cursor = Cursors.Cross,
            Left = 0,
            Top = 0,
            Width = screenW,
            Height = screenH,
            WindowState = WindowState.Normal,
            ResizeMode = ResizeMode.NoResize
        };

        // 预览面板：放大镜色块 + HEX 文本（跟随鼠标）
        var previewColor = new System.Windows.Shapes.Rectangle
        {
            Width = 48,
            Height = 48,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            RadiusX = 4,
            RadiusY = 4
        };
        var hexText = new System.Windows.Controls.TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var hint = new System.Windows.Controls.TextBlock
        {
            Text = "点击取色 · Esc/右键取消",
            FontSize = 11,
            Foreground = Brushes.White,
            Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var panelBorder = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 40)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = panel
        };
        panel.Children.Add(previewColor);
        panel.Children.Add(hexText);
        panel.Children.Add(hint);

        var canvas = new System.Windows.Controls.Canvas();
        var screenshotImage = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = screenW,
            Height = screenH,
            Stretch = Stretch.None
        };
        canvas.Children.Add(screenshotImage);
        canvas.Children.Add(panelBorder);
        overlay.Content = canvas;

        // 鼠标移动：更新预览色块与 HEX 文本，面板跟随光标偏移
        overlay.MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(overlay);
            var c = GetPixelColor(bitmap, pos.X, pos.Y, screenW, screenH);
            previewColor.Fill = new SolidColorBrush(c);
            hexText.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            // 面板跟随光标（右下方偏移，避免遮挡取色点；靠近边缘时翻转）
            double px = pos.X + 20;
            double py = pos.Y + 20;
            if (px + 120 > screenW) px = pos.X - 130;
            if (py + 130 > screenH) py = pos.Y - 140;
            panelBorder.SetValue(System.Windows.Controls.Canvas.LeftProperty, px);
            panelBorder.SetValue(System.Windows.Controls.Canvas.TopProperty, py);
        };

        // 点击确认取色
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(overlay);
            result = GetPixelColor(bitmap, pos.X, pos.Y, screenW, screenH);
            overlay.Close();
        };

        // 右键 / Escape 取消
        overlay.MouseRightButtonDown += (_, _) => overlay.Close();
        overlay.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) overlay.Close();
        };

        overlay.ShowDialog();
        return result;
    }

    /// <summary>截取全屏位图（跨多显示器取主屏；DPI 按 1:1 映射到窗口坐标）。</summary>
    private static (WriteableBitmap? bitmap, double screenW, double screenH) CaptureScreen()
    {
        try
        {
            double left = SystemParameters.VirtualScreenLeft;
            double top = SystemParameters.VirtualScreenTop;
            double width = SystemParameters.VirtualScreenWidth;
            double height = SystemParameters.VirtualScreenHeight;

            using var bmp = new System.Drawing.Bitmap(
                (int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)left, (int)top, 0, 0, bmp.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var bitmapData = bmp.LockBits(
                new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var wb = new WriteableBitmap(
                bmp.Width, bmp.Height, 96, 96, PixelFormats.Pbgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, bmp.Width, bmp.Height),
                bitmapData.Scan0, bitmapData.Stride * bmp.Height, bitmapData.Stride);
            bmp.UnlockBits(bitmapData);
            wb.Freeze();
            return (wb, width, height);
        }
        catch
        {
            return (null, 0, 0);
        }
    }

    /// <summary>从截图中读取指定窗口坐标处的像素颜色（越界时钳位）。</summary>
    private static Color GetPixelColor(WriteableBitmap bitmap, double x, double y, double screenW, double screenH)
    {
        int px = (int)Math.Clamp(x, 0, screenW - 1);
        int py = (int)Math.Clamp(y, 0, screenH - 1);
        // 窗口坐标 → 像素坐标（截图按 96dpi 1:1 映射）
        px = (int)Math.Clamp(px, 0, bitmap.PixelWidth - 1);
        py = (int)Math.Clamp(py, 0, bitmap.PixelHeight - 1);
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(px, py, 1, 1));
        var pixels = new byte[4];
        cropped.CopyPixels(pixels, 4, 0);
        // Pbgra32：预乘 Alpha，此处 Alpha 视为不透明还原
        double a = pixels[3] / 255.0;
        byte r = a > 0 ? (byte)Math.Clamp(pixels[2] / a, 0, 255) : (byte)0;
        byte g = a > 0 ? (byte)Math.Clamp(pixels[1] / a, 0, 255) : (byte)0;
        byte b = a > 0 ? (byte)Math.Clamp(pixels[0] / a, 0, 255) : (byte)0;
        return Color.FromRgb(r, g, b);
    }
}
