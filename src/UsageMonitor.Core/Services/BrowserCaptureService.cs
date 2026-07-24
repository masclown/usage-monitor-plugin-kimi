using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>req-088 Phase3：通用浏览器抓取请求（声明式取数的输入）。</summary>
public sealed class BrowserCaptureRequest
{
    /// <summary>Cookie 串（name=value; ...）。</summary>
    public string Cookie { get; init; } = string.Empty;
    /// <summary>回放用 User-Agent（空则用默认 Chrome UA）。</summary>
    public string? UserAgent { get; init; }
    /// <summary>Cookie 归属域（如 ".minimaxi.com"）。</summary>
    public string CookieDomain { get; init; } = string.Empty;
    /// <summary>用量页导航 URL。</summary>
    public string NavigateUrl { get; init; } = string.Empty;
    /// <summary>需捕获的响应 URL 子串集合（命中即保存 JSON 文本）。</summary>
    public IReadOnlyList<string> CaptureUrlMatches { get; init; } = System.Array.Empty<string>();
    /// <summary>DOM 兜底字段声明（bodyRegex / selectorText）。</summary>
    public IReadOnlyList<FetchDomField> DomFields { get; init; } = System.Array.Empty<FetchDomField>();
    /// <summary>被判定为登录失效的 URL 关键字（命中表示 Cookie 失效）。</summary>
    public IReadOnlyList<string> LoginInvalidKeywords { get; init; } = new[] { "unified-login", "login_redirect", "/login", "signin" };
}

/// <summary>req-088 Phase3：通用抓取结果。</summary>
public sealed class BrowserCaptureResult
{
    /// <summary>已捕获接口响应（key=响应 URL，value=JSON 文本）。</summary>
    public IReadOnlyDictionary<string, string> Responses { get; init; } = new Dictionary<string, string>();
    /// <summary>DOM 兜底结果（key=声明 Target，value=抓取文本）。</summary>
    public IReadOnlyDictionary<string, string> Dom { get; init; } = new Dictionary<string, string>();
    /// <summary>是否被重定向到登录页（Cookie 失效）。</summary>
    public bool LoginInvalid { get; init; }
    /// <summary>页面标题。</summary>
    public string? PageTitle { get; init; }
}

/// <summary>
/// req-088 Phase3：通用浏览器抓取服务（Core，声明式取数的执行基座）。
/// <para>把过去各插件（尤以 MiniMax）手写的"Playwright 启动 + Cookie 注入 + 导航 + 网络响应捕获 + DOM 求值"通用化：
/// 调用方只传"要抓哪些接口 URL 子串 + 哪些 DOM 字段"，本服务返回原始响应与 DOM 文本，交
/// <see cref="Plugins.Declarative.DeclarativeCaptureExecutor"/> 按声明映射为 extras——从而实现"新 Provider 只写声明、不写抓取代码"。</para>
/// </summary>
public static class BrowserCaptureService
{
    private const string LogSource = "BrowserCaptureService";

    /// <summary>限制并发浏览器实例，避免多刷新同时开多个 Edge。</summary>
    private static readonly SemaphoreSlim BrowserSemaphore = new(1, 1);

    /// <summary>req-088 Phase3：抓取结果缓存（3 分钟内不重复启动浏览器，恢复旧 MiniMaxDomExtractor 的缓存行为，避免手动+定时刷新重复开 Edge）。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (BrowserCaptureResult Result, DateTime Expiry)> ResultCache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(3);

    /// <summary>按导航 URL + Cookie 指纹构造缓存键（不存原始 Cookie）。</summary>
    private static string CacheKey(BrowserCaptureRequest req)
        => $"{req.NavigateUrl}|{req.Cookie.Length}|{req.Cookie.GetHashCode()}";

    /// <summary>
    /// 启动浏览器、注入 Cookie、导航用量页，捕获声明的接口响应与 DOM 字段。
    /// </summary>
    /// <param name="req">抓取请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>抓取结果；启动失败/Cookie 空返回 null。</returns>
    public static async Task<BrowserCaptureResult?> CaptureAsync(BrowserCaptureRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Cookie) || string.IsNullOrWhiteSpace(req.NavigateUrl))
        {
            FileLogger.Warn(LogSource, "Cookie/NavigateUrl empty, abort");
            return null;
        }
        var cacheKey = CacheKey(req);
        if (ResultCache.TryGetValue(cacheKey, out var cachedHit) && DateTime.UtcNow < cachedHit.Expiry)
        {
            FileLogger.Info(LogSource, "Returning cached capture result (within 3-min window)");
            return cachedHit.Result;
        }
        if (!await BrowserSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            FileLogger.Warn(LogSource, "Browser semaphore timeout, skip");
            return null;
        }
        var tempProfile = Path.Combine(Path.GetTempPath(), $"UsageMonitor_Capture_{Guid.NewGuid():N}");
        IPlaywright? playwright = null;
        IBrowserContext? context = null;
        try
        {
            // 双重检查：等待信号量期间另一线程可能已填充缓存
            if (ResultCache.TryGetValue(cacheKey, out var cached2) && DateTime.UtcNow < cached2.Expiry)
                return cached2.Result;
            Directory.CreateDirectory(tempProfile);
            playwright = await Playwright.CreateAsync();
            context = await playwright.Chromium.LaunchPersistentContextAsync(tempProfile,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = "msedge",
                    Headless = true,
                    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
                    UserAgent = string.IsNullOrWhiteSpace(req.UserAgent)
                        ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                        : req.UserAgent,
                    Locale = "zh-CN",
                    TimezoneId = "Asia/Shanghai",
                    Args = new[] { "--disable-blink-features=AutomationControlled", "--disable-sync", "--no-first-run", "--no-default-browser-check" }
                });

            var cookies = ParseCookies(req.Cookie, req.CookieDomain);
            if (cookies.Count == 0) { FileLogger.Warn(LogSource, "No cookies parsed"); return null; }
            await context.AddCookiesAsync(cookies);

            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
            var captured = new ConcurrentDictionary<string, string>();
            page.Response += async (_, response) =>
            {
                try
                {
                    var url = response.Url;
                    if (response.Status != 200) return;
                    var hit = false;
                    foreach (var m in req.CaptureUrlMatches)
                        if (!string.IsNullOrEmpty(m) && url.Contains(m, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                    if (!hit) return;
                    captured[url] = await response.TextAsync();
                }
                catch (Exception ex) { FileLogger.Warn(LogSource, $"capture failed {response.Url}: {ex.Message}"); }
            };

            try
            {
                await page.GotoAsync(req.NavigateUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 30_000 });
            }
            catch (Exception ex) { FileLogger.Warn(LogSource, $"Goto exception: {ex.Message}"); }

            try { await page.WaitForSelectorAsync("canvas", new PageWaitForSelectorOptions { Timeout = 10_000 }); }
            catch { /* 图表未渲染也继续，接口可能已捕获 */ }
            ct.ThrowIfCancellationRequested();

            var currentUrl = page.Url;
            foreach (var kw in req.LoginInvalidKeywords)
            {
                if (!string.IsNullOrEmpty(kw) && currentUrl.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    FileLogger.Warn(LogSource, $"Login invalid: redirected to {currentUrl}");
                    return new BrowserCaptureResult { LoginInvalid = true, PageTitle = await SafeTitle(page) };
                }
            }

            var dom = await EvaluateDomFieldsAsync(page, req.DomFields);

            var result = new BrowserCaptureResult
            {
                Responses = new Dictionary<string, string>(captured),
                Dom = dom,
                LoginInvalid = false,
                PageTitle = await SafeTitle(page)
            };
            // 仅缓存成功（非登录失效）结果；3 分钟内复用。
            ResultCache[cacheKey] = (result, DateTime.UtcNow.Add(CacheTtl));
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogSource, $"CaptureAsync failed: {ex.GetType().Name}: {ex.Message}", ex);
            return null;
        }
        finally
        {
            try { if (context != null) await context.CloseAsync(); } catch { }
            try { playwright?.Dispose(); } catch { }
            try { if (Directory.Exists(tempProfile)) Directory.Delete(tempProfile, true); } catch { }
            BrowserSemaphore.Release();
        }
    }

    /// <summary>在浏览器端求值 DOM 兜底字段（bodyRegex 对 body 文本正则；selectorText 取选择器文本）。</summary>
    private static async Task<Dictionary<string, string>> EvaluateDomFieldsAsync(IPage page, IReadOnlyList<FetchDomField> fields)
    {
        var result = new Dictionary<string, string>();
        foreach (var f in fields)
        {
            try
            {
                string? val;
                if (string.Equals(f.Tool, "jsFunction", StringComparison.OrdinalIgnoreCase))
                {
                    // 声明提供一个返回 string|null 的 JS 函数表达式（供需自定义 DOM 查找的字段，如订阅档位）。
                    val = await page.EvaluateAsync<string?>(f.Source);
                }
                else if (string.Equals(f.Tool, "selectorText", StringComparison.OrdinalIgnoreCase))
                {
                    val = await page.EvaluateAsync<string?>(
                        "(sel) => { const el = document.querySelector(sel); return el ? el.textContent.trim() : null; }", f.Source);
                }
                else // bodyRegex
                {
                    val = await page.EvaluateAsync<string?>(
                        "(re) => { const m = (document.body.innerText || '').match(new RegExp(re)); return m ? (m[1] || m[0]) : null; }", f.Source);
                }
                if (!string.IsNullOrEmpty(val)) result[f.Target] = val!;
            }
            catch (Exception ex) { FileLogger.Warn(LogSource, $"DOM field {f.Target} failed: {ex.Message}"); }
        }
        return result;
    }

    private static async Task<string?> SafeTitle(IPage page)
    {
        try { return await page.TitleAsync(); } catch { return null; }
    }

    /// <summary>解析 Cookie 串为 Playwright Cookie（统一挂到给定 Domain，覆盖多子域）。</summary>
    private static List<Cookie> ParseCookies(string cookieString, string domain)
    {
        var list = new List<Cookie>();
        if (string.IsNullOrWhiteSpace(cookieString) || string.IsNullOrWhiteSpace(domain)) return list;
        foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var name = part.Substring(0, eq).Trim();
            var value = part.Substring(eq + 1).Trim();
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new Cookie { Name = name, Value = value, Domain = domain, Path = "/" });
        }
        return list;
    }
}
