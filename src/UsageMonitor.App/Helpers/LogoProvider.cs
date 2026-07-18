using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-032：项目 Logo 加载器（单 logo 统一模式）。
/// <para>
/// 回退双主题 logo 设计，改用单一透明背景 PNG（<c>usage-monitor-logo.png</c>），
/// 全局统一用于主窗口 Icon / 托盘图标 / 任务栏窗口 / 悬浮窗。
/// </para>
/// <para>
/// 资源位于 <c>Assets/Providers/usage-monitor-logo.png</c>，已标记为 <c>Content + CopyToOutputDirectory</c>。
/// 加载时用 <see cref="BitmapCacheOption.OnLoad"/> 立即释放文件锁，避免占用部署包。
/// </para>
/// </summary>
public static class LogoProvider
{
    /// <summary>统一 logo 文件名（透明背景，单文件）。</summary>
    private const string LogoFileName = "usage-monitor-logo.png";

    /// <summary>
    /// 返回项目 Logo <see cref="ImageSource"/>（不再按主题区分，统一返回同一图片）。
    /// <para>
    /// 注意：返回的 <see cref="BitmapImage"/> 已 <see cref="Freezable.Freeze"/>，可在任意线程使用。
    /// </para>
    /// </summary>
    /// <returns>logo ImageSource</returns>
    public static ImageSource LoadLogo()
    {
        var path = ResolveLogoPath(LogoFileName);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// 取得 Logo 文件绝对路径。供托盘 .ico / 其他场景使用。
    /// </summary>
    public static string GetLogoPath()
    {
        return ResolveLogoPath(LogoFileName);
    }

    /// <summary>
    /// 把 logo 文件名解析为 exe 同目录下 Assets/Providers/&lt;name&gt; 的绝对路径。
    /// </summary>
    private static string ResolveLogoPath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Assets", "Providers", fileName);
        if (!File.Exists(path))
        {
            FileLogger.Warn("LogoProvider",
                $"Logo file not found: {path}; falling back to legacy icon.ico");
            // 兜底：找任意存在的 icon.ico
            var fallback = Path.Combine(baseDir, "Assets", "Providers", "icon.ico");
            return File.Exists(fallback) ? fallback : path;
        }
        return path;
    }
}
