using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Services;

/// <summary>
/// Provider 图标解析与运行时获取服务（分发准备）。
/// <para>
/// 背景：第三方服务商品牌 Logo 不再随仓库 / 安装包分发（规避商标再分发风险）。
/// 本服务在首次运行时按服务商官网域名抓取 favicon，缓存到
/// <c>%AppData%/UsageMonitor/icons/{providerId}.ico</c>，后续启动直接命中缓存。
/// </para>
/// <para>
/// 图标解析优先级：用户缓存目录 → 随包内置 Assets/Providers（仅保留本项目自有 Logo / 用户自备图标）→ null。
/// 域名来源：插件实例声明的浏览器登录配置（<see cref="UsageMonitor.Core.Models.BrowserLoginConfig.LoginUrl"/>）——
/// 取自 Provider 实例，可靠且不依赖 defaults.json 目录布局；无浏览器登录配置的纯 API 插件不自动抓取。
/// </para>
/// </summary>
public static class ProviderIconService
{
    private const string LogSource = "ProviderIconService";

    // 复用单例 HttpClient，避免频繁创建导致 socket 耗尽；10s 超时保证启动预取不拖慢 UI。
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // 支持的图标扩展名（解析时按此顺序探测本地文件，与旧 ResolveIconPath 行为一致）。
    private static readonly string[] IconExtensions = { ".png", ".ico", ".jpg", ".svg" };

    /// <summary>用户图标缓存目录：%AppData%/UsageMonitor/icons。</summary>
    public static string CacheDir
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "UsageMonitor", "icons");
        }
    }

    /// <summary>
    /// 解析指定 Provider 的本地图标路径（同步、无网络）。
    /// </summary>
    /// <param name="providerId">服务商唯一标识。</param>
    /// <returns>可用图标文件绝对路径；均不存在返回 null。优先级：用户缓存目录 → 随包资源。</returns>
    public static string? ResolveIconPath(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        var id = providerId.ToLowerInvariant();

        // 1) 用户缓存目录（运行时抓取的 favicon 落这里）
        var cached = ProbeDirectory(CacheDir, id);
        if (cached != null) return cached;

        // 2) 随包内置资源（仅保留本项目自有 Logo / 用户手动放置的图标）
        var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Providers");
        return ProbeDirectory(bundled, id);
    }

    /// <summary>在指定目录按扩展名优先级探测 {id}.{ext} 文件。</summary>
    /// <param name="dir">待探测目录。</param>
    /// <param name="id">已小写化的 providerId。</param>
    private static string? ProbeDirectory(string dir, string id)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        foreach (var ext in IconExtensions)
        {
            var candidate = Path.Combine(dir, id + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// 确保指定 Provider 的图标可用：本地已有则直接返回；否则按服务商域名抓取 favicon 缓存后返回。
    /// <para>尽力而为——任何失败（无域名 / 网络异常 / 非图片响应）均记日志并返回 null，绝不抛出。</para>
    /// <para>修复16：抓取路径改为多 URL fallback + HTML <c>&lt;link rel="icon"&gt;</c> 解析。原因：部分服务商如 DeepSeek
    /// 的 <c>favicon.ico</c> 实际返回 HTML 页面（服务端 200 + text/html），造成 <see cref="LooksLikeImage"/> 拒绝。
    /// 优先级：① /apple-touch-icon*.png ② /favicon-32x32.png ③ /favicon.png ④ 解析首页 HTML 取出 icon URL ⑤ 原始 /favicon.ico。</para>
    /// </summary>
    /// <param name="provider">插件实例（提供 ProviderId 与浏览器登录配置域名）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>图标文件绝对路径；无法获取返回 null。</returns>
    public static async Task<string?> EnsureIconAsync(IUsageProvider? provider, CancellationToken ct = default)
    {
        var providerId = provider?.ProviderId;
        if (string.IsNullOrWhiteSpace(providerId)) return null;

        var local = ResolveIconPath(providerId);
        if (local != null) return local;

        var host = ResolveIconHost(provider!);
        if (host == null) return null;

        // 修复16：按需先尝试多 URL fallback；若仅取到 SVG（WPF 不渲染），继续 OG 兑底。
        var bytes = await TryCommonIconUrlsAsync(host, ct).ConfigureAwait(false);
        bytes ??= await TryHtmlLinkedIconAsync(host, ct).ConfigureAwait(false);
        bytes ??= await TryOpenGraphImageAsync(host, ct).ConfigureAwait(false);

        if (bytes == null)
        {
            FileLogger.Warn(LogSource, $"{providerId} 图标抓取全部失败（favicon.ico / link / og:image 都不是可用图片）");
            return null;
        }

        // 修复16：按字节实际格式选择扩展名（WPF Image Source 依赖文件扩展名决定解码器）。
        var ext = PickImageExtension(bytes, null);
        var bytesAreRenderable = ext != ".svg"; // WPF 不能渲染 SVG，即使抓到也不能用于卡片/logo。
        if (!bytesAreRenderable)
        {
            // 兑底：继续拉 og:image / twitter:image（社会分享大图一般非 SVG）。
            var ogBytes = await TryOpenGraphImageAsync(host, ct).ConfigureAwait(false);
            if (ogBytes == null)
            {
                FileLogger.Info(LogSource, $"{providerId} favicon 为 SVG 格式且 OG 图取不到（WPF 不支持，需手动放 PNG 至 Assets/Providers）");
                return null;
            }
            bytes = ogBytes;
            ext = PickImageExtension(bytes, null);
            bytesAreRenderable = ext != ".svg";
            if (!bytesAreRenderable)
            {
                FileLogger.Info(LogSource, $"{providerId} OG 图也是 SVG（WPF 不支持，需手动放 PNG 至 Assets/Providers）");
                return null;
            }
        }

        Directory.CreateDirectory(CacheDir);
        var target = Path.Combine(CacheDir, providerId!.ToLowerInvariant() + ext);
        await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
        return target;
    }

    /// <summary>
    /// 修复16：按优先级依次尝试图标 URL，返回首个成功获取的字节数组；全部失败返回 null。
    /// <para>顺序：<c>/apple-touch-icon-precomposed.png</c> → <c>/apple-touch-icon.png</c> →
    /// <c>/favicon-32x32.png</c> → <c>/favicon.png</c> → <c>/favicon.ico</c>。</para>
    /// </summary>
    private static async Task<byte[]?> TryCommonIconUrlsAsync(string host, CancellationToken ct)
    {
        var urls = new[]
        {
            $"https://{host}/apple-touch-icon-precomposed.png",
            $"https://{host}/apple-touch-icon.png",
            $"https://{host}/favicon-32x32.png",
            $"https://{host}/favicon.png",
            $"https://{host}/favicon.ico"
        };
        foreach (var url in urls)
        {
            byte[]? bytes = await DownloadIfImageAsync(url, ct).ConfigureAwait(false);
            if (bytes != null) return bytes;
        }
        return null;
    }

    /// <summary>
    /// 修复16：拉取首页 HTML，从 <c>&lt;link rel="icon" href="..."&gt;</c> / <c>apple-touch-icon</c> 解析图标 URL 后下载。
    /// <para>作为 favicon.ico 返回 HTML 、服务器仅在首页暴露 icon 链接 的服务商的兑底路径（如 DeepSeek）。</para>
    /// </summary>
    private static async Task<byte[]?> TryHtmlLinkedIconAsync(string host, CancellationToken ct)
    {
        try
        {
            var indexUrl = $"https://{host}/";
            using var req = new HttpRequestMessage(HttpMethod.Get, indexUrl);
            // 不自动跳转：保持 index 页返回体本身（部分 Server 会 301→ usage）。
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var ctHeader = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!ctHeader.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            // 仅读前 32 KB（icon link 一般出现在 head 前几行；全页有数百 KB）
            var buf = new char[32_000];
            var read = await reader.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
            var html = new string(buf, 0, read);

            // 匹配顺序：apple-touch-icon → icon
            var linkRe = new Regex(
                @"<link[^>]+rel\s*=\s*""(?<r>[\w\- ]+)""[^>]+href\s*=\s*""(?<h>[^""]+)""",
                RegexOptions.IgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in linkRe.Matches(html))
            {
                var rel = m.Groups["r"].Value.Trim();
                var href = m.Groups["h"].Value.Trim();
                if (!(rel.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
                      rel.Contains("apple-touch-icon", StringComparison.OrdinalIgnoreCase))) continue;
                if (string.IsNullOrEmpty(href)) continue;
                var absolute = Uri.TryCreate(href, UriKind.Absolute, out var u) ? u.ToString() : $"https://{host}{href.TrimStart('/')}";
                if (!seen.Add(absolute)) continue;
                var bytes = await DownloadIfImageAsync(absolute, ct).ConfigureAwait(false);
                if (bytes != null) return bytes;
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"HTML link icon 解析失败 ({host})：{ex.Message}");
            return null;
        }
    }

    /// <summary>下载 URL 并校验是否为图片；失败或非图片返回 null。</summary>
    private static async Task<byte[]?> DownloadIfImageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return LooksLikeImage(bytes, resp.Content.Headers.ContentType?.MediaType) ? bytes : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"抓取 {url} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 修复16：拉取首页 HTML，从中提取 <c>og:image</c> / <c>twitter:image</c>（社会分享大图，通常是品牌高分辨率 PNG/JPG）。
    /// <para>作为 favicon.ico 与 apple-touch-icon 均为 SVG/HTML 的服务商的最后一次兑底（如 DeepSeek）。
    /// 注意抓取后会用 PNG/JPG 才落地（WPF 不渲染 SVG）。</para>
    /// </summary>
    private static async Task<byte[]?> TryOpenGraphImageAsync(string host, CancellationToken ct)
    {
        try
        {
            var indexUrl = $"https://{host}/";
            using var req = new HttpRequestMessage(HttpMethod.Get, indexUrl);
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var ctHeader = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!ctHeader.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;
            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var buf = new char[64_000];
            var read = await reader.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
            var html = new string(buf, 0, read);

            // 优先顺序：og:image → twitter:image
            var metaRe = new Regex(
                @"<meta[^>]+(?:property|name)\s*=\s*""(?<k>[^""]+)""[^>]+content\s*=\s*""(?<v>[^""]+)""",
                RegexOptions.IgnoreCase);
            foreach (Match m in metaRe.Matches(html))
            {
                var key = m.Groups["k"].Value.Trim();
                var val = m.Groups["v"].Value.Trim();
                if (!string.Equals(key, "og:image", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(key, "twitter:image", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(val)) continue;
                var absolute = Uri.TryCreate(val, UriKind.Absolute, out var u) ? u.ToString() : $"https://{host}{val.TrimStart('/')}";
                var bytes = await DownloadIfImageAsync(absolute, ct).ConfigureAwait(false);
                // 在 OG 图场景中接受 SVG（但调用方会再筛选 WPF 可渲染的扩展名）
                if (bytes != null && LooksLikeImage(bytes, null))
                    return bytes;
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"OG image 解析失败 ({host})：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 修复16：删除失效的图标缓存，允许重试抓取（UI 提供「重试」入口时调用）。
    /// </summary>
    /// <param name="providerId">服务商唯一标识。</param>
    /// <returns>是否删除了文件。</returns>
    public static bool DeleteCachedIcon(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return false;
        var path = Path.Combine(CacheDir, providerId!.ToLowerInvariant() + ".ico");
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (Exception ex) { FileLogger.Warn(LogSource, $"删除 {providerId} 缓存图标失败：{ex.Message}"); return false; }
    }

    /// <summary>
    /// 并发预取多个 Provider 的图标（启动时调用）。仅对本地缺失的 Provider 发起抓取。
    /// </summary>
    /// <param name="providers">待预取的插件实例集合。</param>
    /// <param name="ct">取消令牌。</param>
    public static Task PrefetchAllAsync(IEnumerable<IUsageProvider>? providers, CancellationToken ct = default)
    {
        if (providers == null) return Task.CompletedTask;
        var tasks = providers
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.ProviderId) && ResolveIconPath(p.ProviderId) == null)
            .Select(p => EnsureIconAsync(p, ct))
            .ToArray();
        return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
    }

    /// <summary>
    /// 解析 Provider 的 favicon 主机名：取自插件实例声明的浏览器登录配置（LoginUrl / ValidateUrl）。
    /// <para>数据来自 Provider 实例（可靠，不依赖 defaults.json 目录布局）；无浏览器登录配置的纯 API 插件返回 null。</para>
    /// </summary>
    /// <param name="provider">插件实例。</param>
    /// <returns>主机名（如 platform.deepseek.com）；无法解析返回 null。</returns>
    private static string? ResolveIconHost(IUsageProvider provider)
    {
        try
        {
#pragma warning disable CS0618 // LoginConfig 已过时（req-096），此处仅借其 URL 推导 favicon 域名，与 DisplayModule 用法一致
            var login = provider.LoginConfig;
#pragma warning restore CS0618
            var source = !string.IsNullOrWhiteSpace(login?.LoginUrl) ? login!.LoginUrl : login?.ValidateUrl;
            if (string.IsNullOrWhiteSpace(source)) return null;
            return Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : null;
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"解析 {provider.ProviderId} 图标域名失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 粗校验响应是否为图片：content-type 以 image 开头（含 svg+xml），或字节头匹配 ICO/PNG/JPEG/GIF/SVG 魔数。
    /// <para>修复16：增加 SVG 魔数检测（<c>&lt;?xml</c> / <c>&lt;svg</c>），但 WPF <c>BitmapImage</c> 暂不渲染 SVG，
    /// 故 SVG 抓取仅供诊断需人工转存；JPG/PNG/GIF/ICO 可直接被主程序 XAML 加载。</para>
    /// </summary>
    /// <param name="bytes">响应字节。</param>
    /// <param name="mediaType">响应 content-type 媒体类型（可空）。</param>
    private static bool LooksLikeImage(byte[]? bytes, string? mediaType)
    {
        if (bytes == null || bytes.Length < 4) return false;
        if (!string.IsNullOrEmpty(mediaType) && mediaType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            return true;
        // ICO: 00 00 01 00 ; PNG: 89 50 4E 47 ; JPEG: FF D8 FF ; GIF: 47 49 46 38
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true;
        // SVG: <xml? 或 <svg ；以 ASCII '<' 开头，后续 "svg" 或 "?xml"
        if (bytes[0] == 0x3C &&
            (bytes[1] == 0x73 /* s */ || bytes[1] == 0x3F /* ? */ || bytes[1] == 0x21 /* ! */) &&
            (bytes[2] == 0x76 /* v */ || bytes[2] == 0x78 /* x */))
        {
            // 进一步以 UTF-8 ASCII 偏移检查 <svg / <?xml（避开 <script 等假鳂）
            var max = Math.Min(bytes.Length, 32);
            var head = System.Text.Encoding.ASCII.GetString(bytes, 0, max).TrimStart();
            return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>修复16：按字节实际格式判断图片文件应使用的扩展名（WPF 按扩展名加载 Image Source）。</summary>
    private static string PickImageExtension(byte[] bytes, string? mediaType)
    {
        if (!string.IsNullOrEmpty(mediaType))
        {
            var mt = mediaType.ToLowerInvariant();
            if (mt.Contains("jpeg") || mt.Contains("jpg")) return ".jpg";
            if (mt.Contains("png")) return ".png";
            if (mt.Contains("x-icon") || mt.Contains("vnd.microsoft.icon")) return ".ico";
            if (mt.Contains("gif")) return ".gif";
            if (mt.Contains("svg")) return ".svg";
        }
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8) return ".jpg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50) return ".png";
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01) return ".ico";
            if (bytes[0] == 0x47 && bytes[1] == 0x49) return ".gif";
            if (bytes[0] == 0x3C && bytes[1] == 0x73) return ".svg"; // <s ...
        }
        return ".png"; // 兑底（BitmapImage 能加载 PNG 作为默认兑底）
    }
}
