using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-016：项目 Logo 加载器。
/// <para>
/// 提供双主题适配的 Logo 资源：
/// <list type="bullet">
/// <item><description>深色主题（Dark）→ 浅色 logo（<c>usage-monitor-logo-light.png</c>，白色 / 高亮色，适合深色背景）</description></item>
/// <item><description>浅色主题（Light）→ 深色 logo（<c>usage-monitor-logo-dark.png</c>，深灰 / 暗色，适合浅色背景）</description></item>
/// </list>
/// </para>
/// <para>
/// 资源位于 <c>Assets/Providers/usage-monitor-logo-{light,dark}.png</c>，已标记为 <c>Content + CopyToOutputDirectory</c>。
/// 加载时用 <see cref="BitmapCacheOption.OnLoad"/> 立即释放文件锁，避免占用部署包。
/// </para>
/// </summary>
public static class LogoProvider
{
    /// <summary>浅色 logo 文件名（用于深色主题，深色背景下高亮显示）。</summary>
    private const string LightLogoFileName = "usage-monitor-logo-light.png";

    /// <summary>深色 logo 文件名（用于浅色主题，浅色背景下深色显示）。</summary>
    private const string DarkLogoFileName = "usage-monitor-logo-dark.png";

    /// <summary>
    /// 按当前主题返回对应的 Logo <see cref="ImageSource"/>（用于 WPF Window.Icon 绑定）。
    /// <para>
    /// 注意：返回的 <see cref="BitmapImage"/> 已 <see cref="Freezable.Freeze"/>，可在任意线程使用。
    /// </para>
    /// </summary>
    /// <param name="theme">目标主题</param>
    /// <returns>对应主题的 logo ImageSource</returns>
    public static ImageSource LoadLogo(ThemeMode theme)
    {
        var fileName = theme == ThemeMode.Light ? DarkLogoFileName : LightLogoFileName;
        var path = ResolveLogoPath(fileName);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// req-016：取得当前主题下的 Logo 文件绝对路径。供托盘 .ico / 其他场景使用。
    /// </summary>
    public static string GetLogoPath(ThemeMode theme)
    {
        var fileName = theme == ThemeMode.Light ? DarkLogoFileName : LightLogoFileName;
        return ResolveLogoPath(fileName);
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