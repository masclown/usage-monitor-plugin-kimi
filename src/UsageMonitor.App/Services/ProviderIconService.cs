using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        try
        {
            // 直接从服务商自有域名取 favicon（指示性合理使用，且不经第三方代理，最稳妥）。
            var url = $"https://{host}/favicon.ico";
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (!LooksLikeImage(bytes, resp.Content.Headers.ContentType?.MediaType)) return null;

            Directory.CreateDirectory(CacheDir);
            var target = Path.Combine(CacheDir, providerId!.ToLowerInvariant() + ".ico");
            await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
            return target;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"抓取 {providerId} favicon 失败：{ex.Message}");
            return null;
        }
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
    /// 粗校验响应是否为图片：content-type 以 image 开头，或字节头匹配 ICO/PNG/JPEG 魔数。
    /// </summary>
    /// <param name="bytes">响应字节。</param>
    /// <param name="mediaType">响应 content-type 媒体类型（可空）。</param>
    private static bool LooksLikeImage(byte[]? bytes, string? mediaType)
    {
        if (bytes == null || bytes.Length < 4) return false;
        if (!string.IsNullOrEmpty(mediaType) && mediaType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            return true;
        // ICO: 00 00 01 00 ; PNG: 89 50 4E 47 ; JPEG: FF D8 FF
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        return false;
    }
}
