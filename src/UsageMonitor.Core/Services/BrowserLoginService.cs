using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 通用浏览器登录服务 - 复刻自销项数据助手 browser-cookie-manager Skill。
/// <para>
/// 从 2026-07-06 的裸 CDP 实现迁移到 Microsoft.Playwright。
/// 解决了反复出现的 5+ Bug（页面太小、navigate 丢失、URL 关键字误判、Cookie 提取失败等）。
/// 复用系统已安装的 Microsoft Edge（channel=msedge），不下载 Chromium。
/// </para>
/// </summary>
public static class BrowserLoginService
{
    /// <summary>Optional ConfigService for in-memory rehydration after disk write.</summary>
    private static ConfigService? _configService;

    /// <summary>
    /// Register the live ConfigService so that when <see cref="LoginAndExtractCookieAsync"/>
    /// writes to disk directly (bypassing <c>UpdateProviderConfig</c>), the in-memory
    /// provider config can be reloaded — so the next refresh tick sees the new cookie
    /// without requiring an app restart.
    /// </summary>
    public static void RegisterConfigService(ConfigService service)
    {
        _configService = service;
        FileLogger.Info("BrowserLoginService", "ConfigService registered for auto-reload after login.");
    }

    /// <summary>Main config.json path: %APPDATA%/UsageMonitor/config.json</summary>
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "config.json");

    /// <summary>Persist newly captured cookies into the main config.json's ProviderConfigs entry.</summary>
    private static void PersistToMainConfig(BrowserLoginConfig config, BrowserCookieData data)
    {
        try
        {
            FileLogger.Info("BrowserLoginService",
                $"Persisting new cookies to {ConfigPath} (providerId={config.ProviderId})");
            string raw;
            // Read existing config (may or may not exist)
            if (File.Exists(ConfigPath))
            {
                raw = File.ReadAllText(ConfigPath, System.Text.Encoding.UTF8);
            }
            else
            {
                raw = "{\"ProviderConfigs\":{}}";
            }
            // Deserialize as raw JsonNode to preserve unrelated fields
            var doc = System.Text.Json.Nodes.JsonNode.Parse(raw, new System.Text.Json.Nodes.JsonNodeOptions { PropertyNameCaseInsensitive = true }) as System.Text.Json.Nodes.JsonObject;
            if (doc == null)
            {
                FileLogger.Warn("BrowserLoginService", "Main config.json could not be parsed; skipping persistence");
                return;
            }
            var providerConfigs = doc["ProviderConfigs"] as System.Text.Json.Nodes.JsonObject;
            if (providerConfigs == null)
            {
                providerConfigs = new System.Text.Json.Nodes.JsonObject();
                doc["ProviderConfigs"] = providerConfigs;
            }
            var miniCfg = providerConfigs[config.ProviderId] as System.Text.Json.Nodes.JsonObject;
            if (miniCfg == null)
            {
                miniCfg = new System.Text.Json.Nodes.JsonObject
                {
                    ["ProviderId"] = config.ProviderId,
                    ["IsEnabled"] = true,
                    ["Values"] = new System.Text.Json.Nodes.JsonObject()
                };
                providerConfigs[config.ProviderId] = miniCfg;
            }
            var values = miniCfg["Values"] as System.Text.Json.Nodes.JsonObject;
            if (values == null)
            {
                values = new System.Text.Json.Nodes.JsonObject();
                miniCfg["Values"] = values;
            }
            values["Cookie"] = data.Cookie;
            values["_userAgent"] = data.UserAgent;
            // Ensure ApiKey/BaseUrl don't get wiped if user never set them
            values["Region"] = values["Region"]?.GetValue<string>() ?? "CN";

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, doc.ToJsonString(options), System.Text.Encoding.UTF8);
            FileLogger.Info("BrowserLoginService", $"Persisted cookies. Cookie length={data.Cookie.Length}");

            // CRITICAL: rehydrate the in-memory ConfigService so the next RefreshService
            // tick sees the new cookie (otherwise the old in-memory dict is reused and
            // the user must restart the app).
            try
            {
                _configService?.ReloadProviderConfigsFromDisk();
            }
            catch (Exception reloadEx)
            {
                FileLogger.Warn("BrowserLoginService",
                    $"Reload after persist failed (non-fatal): {reloadEx.Message}");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("BrowserLoginService", $"PersistToMainConfig failed: {ex.Message}", ex);
        }
    }

    /// <summary>Cookie 持久化根目录</summary>
    private static readonly string CookieDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "cookies");

    /// <summary>
    /// 最近一次失败的错误信息（用于 UI 显示真实失败原因）。
    /// </summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// 启动临时 Edge 浏览器，等待用户完成登录，提取 Cookie 后关闭浏览器。
    /// </summary>
    public static async Task<BrowserCookieData?> LoginAndExtractCookieAsync(
        BrowserLoginConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.LoginUrl))
            throw new ArgumentException("LoginUrl cannot be empty", nameof(config));

        // 重置 LastError，避免上一次失败信息影响本次显示
        LastError = null;

        IPlaywright? playwright = null;
        IBrowserContext? context = null;
        var tempProfile = Path.Combine(Path.GetTempPath(),
            $"UsageMonitor_Edge_{config.ProviderId}_{Guid.NewGuid():N}");

        try
        {
            // 创建临时 profile（移到 try 块内，避免权限异常未捕获）
            Directory.CreateDirectory(tempProfile);
            playwright = await Playwright.CreateAsync();

            // Channel="msedge" 让 Playwright 自动用系统已安装的 Microsoft Edge 启动
            // （不再需要手动找 Edge 路径或指定 ExecutablePath）
            var launchOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                // 关键：告诉 Playwright 用系统已安装的 Microsoft Edge 启动（避免下载 Chromium）
                Channel = "msedge",
                Headless = false,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                             "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                IgnoreHTTPSErrors = true,
                Locale = "zh-CN",
                TimezoneId = "Asia/Shanghai",
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8",
                },
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-sync",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-features=msEdgeSync,InterestFeedContentSuggestions," +
                                       "Translate,OptimizationHints",
                },
            };

            context = await playwright.Chromium.LaunchPersistentContextAsync(
                tempProfile, launchOptions);

            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

            // Navigate directly to the protected usage page. Because the temp profile is NOT
            // logged in, MiniMax redirects to account.minimaxi.com/unified-login and shows the
            // login UI. The redirect carries login_redirect=/console/usage, so after the user
            // logs in they are automatically brought BACK to /console/usage.
            var initialTarget = !string.IsNullOrEmpty(config.ValidateUrl) ? config.ValidateUrl! : config.LoginUrl;
            try
            {
                FileLogger.Info("BrowserLoginService", $"Navigating to {initialTarget} (expect redirect to login page)");
                await page.GotoAsync(initialTarget,
                    new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions { Timeout = 30_000 });
            }
            catch (Exception gotoEx)
            {
                // Don't abort: keep the browser open so the user can still log in manually.
                FileLogger.Warn("BrowserLoginService", $"Initial navigation exception (continuing to wait): {gotoEx.Message}");
            }
            FileLogger.Info("BrowserLoginService", $"Login page presented. Current URL: {page.Url}");

            // Poll until the user finishes login. The browser stays OPEN the whole time so the
            // user can scan the QR code / enter the phone verification code. We NEVER close Edge
            // here (matching the reference browser-cookie-manager pattern). Detection is purely
            // URL-based: when a visible tab is no longer on a login page, the user has logged in.
            var deadline = DateTime.UtcNow + config.LoginTimeout;
            var loginSuccess = false;
            string detectedUrl = "(login page)";
            var pollSw = System.Diagnostics.Stopwatch.StartNew();
            long lastLogBucket = -1;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Collect all live tab URLs (user may open a new tab during login).
                var urls = context.Pages
                    .Where(p => !string.IsNullOrEmpty(p.Url))
                    .Select(p => p.Url!)
                    .ToList();

                // A URL counts as "logged in" when it's on the platform host and not a login page.
                var loggedInUrl = urls.FirstOrDefault(u => !IsLoginUrl(u, config));

                if (loggedInUrl != null)
                {
                    detectedUrl = loggedInUrl;
                    FileLogger.Info("BrowserLoginService", $"Left login page. URL: {loggedInUrl}");

                    // User authenticated. Force-navigate the visible tab to /console/usage so
                    // (a) the user sees real usage data and (b) we confirm the session is valid.
                    try
                    {
                        FileLogger.Info("BrowserLoginService",
                            "Navigating visible tab to https://platform.minimaxi.com/console/usage");
                        await page.GotoAsync("https://platform.minimaxi.com/console/usage",
                            new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 30_000 });
                        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                            new PageWaitForLoadStateOptions { Timeout = 20_000 });
                    }
                    catch (Exception navEx)
                    {
                        FileLogger.Warn("BrowserLoginService", $"Navigate to /console/usage failed: {navEx.Message}");
                    }
                    await Task.Delay(2500, cancellationToken);

                    // Confirm we actually landed on /console/usage (not bounced back to login).
                    var finalUrl = page.Url;
                    FileLogger.Info("BrowserLoginService", $"After navigate to console/usage, URL = {finalUrl}");
                    if (IsLoginUrl(finalUrl, config))
                    {
                        // Bounced back to login → session not established yet. Keep waiting.
                        FileLogger.Info("BrowserLoginService",
                            "Bounced back to login page — session not ready yet, keep waiting.");
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }

                    detectedUrl = finalUrl;
                    loginSuccess = true;
                    break;
                }

                // Still on login page — log progress about every 10s; keep Edge open.
                var bucket = (long)pollSw.Elapsed.TotalSeconds / 10;
                if (bucket != lastLogBucket)
                {
                    lastLogBucket = bucket;
                    FileLogger.Debug("BrowserLoginService",
                        $"Waiting for login... {pollSw.Elapsed.TotalSeconds:0}s elapsed. URL: {(urls.Count > 0 ? urls[0] : "(none)")}");
                }
                await Task.Delay(1500, cancellationToken);
            }

            if (!loginSuccess)
            {
                LastError = $"[Stage LoginTimeout] {config.LoginTimeout.TotalMinutes:0} 分钟内未检测到登录完成。请在弹出的 Edge 窗口中扫码或输入手机验证码完成登录。\n最后 URL: {detectedUrl}";
                FileLogger.Error("BrowserLoginService",
                    $"LoginTimeout after {pollSw.ElapsedMilliseconds}ms. Last URL: {detectedUrl}");
                return null;
            }
            FileLogger.Info("BrowserLoginService",
                $"Login success confirmed. URL: {detectedUrl}. Total elapsed: {pollSw.ElapsedMilliseconds}ms");

            // Extract cookies via Playwright. Build cookie URLs from:
            //   (a) LoginUrl and ValidateUrl (raw URLs the user passes in)
            //   (b) Every scheme://host derived from CookieDomainFilters — critical for cross-subdomain
            //       auth flows where the session cookie is on a different subdomain than LoginUrl
            //       (e.g. MiniMax login token is on account.minimaxi.com, LoginUrl is platform.minimaxi.com).
            var cookieUrls = new List<string>();
            if (Uri.TryCreate(config.LoginUrl, UriKind.Absolute, out var loginUri))
            {
                cookieUrls.Add(config.LoginUrl);
                cookieUrls.Add($"{loginUri.Scheme}://{loginUri.Host}");
                if (!string.IsNullOrEmpty(config.ValidateUrl))
                    cookieUrls.Add(config.ValidateUrl);
            }
            if (config.CookieDomainFilters != null)
            {
                foreach (var rawFilter in config.CookieDomainFilters)
                {
                    if (string.IsNullOrWhiteSpace(rawFilter)) continue;
                    var host = rawFilter.TrimStart('.');
                    cookieUrls.Add($"https://{host}");
                    // Try common subdomains that often hold the auth cookie (account / www / oauth / login)
                    foreach (var sub in new[] { "account", "www", "oauth", "login", "passport", "auth" })
                    {
                        cookieUrls.Add($"https://{sub}.{host}");
                    }
                }
            }
            // Deduplicate while preserving order
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            cookieUrls = cookieUrls.Where(u => seen.Add(u)).ToList();
            if (cookieUrls.Count == 0) cookieUrls.Add(config.LoginUrl);

            FileLogger.Info("BrowserLoginService",
                $"Cookie URLs to query: {cookieUrls.Count}: {string.Join(", ", cookieUrls.Take(8))}");

            var cookies = await context.CookiesAsync(cookieUrls.ToArray());
            if (cookies == null || cookies.Count == 0)
            {
                // 诊断：记录当前页面状态帮助排查
                string diagInfo = "unknown";
                try
                {
                    var cur = context.Pages[0];
                    diagInfo = await cur.EvaluateAsync<string>("() => JSON.stringify({ url: window.location.href, title: document.title, cookies: document.cookie.substring(0, 500) })");
                }
                catch { }
                try
                {
                    var debugDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UsageMonitor", "debug");
                    Directory.CreateDirectory(debugDir);
                    var diagPath = Path.Combine(debugDir, $"login-diag-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
                    var diagContent = "[BrowserLoginService] cookies.Count=0 diagnostic" + Environment.NewLine
                        + "  page=" + diagInfo + Environment.NewLine
                        + "  TempProfile=" + tempProfile + Environment.NewLine
                        + "  LoginUrl=" + config.LoginUrl + Environment.NewLine
                        + "  LoginUrlKeywords=" + string.Join(",", config.LoginUrlKeywords ?? new List<string>()) + Environment.NewLine
                        + "  LoginSuccessHost=" + config.LoginSuccessHost;
                    File.WriteAllText(diagPath, diagContent);
                }
                catch { }

                LastError = "[Stage CookieExtract] cookies.Count=0。"
                    + "可能原因：(1) 登录后 MiniMax 未设置 Cookie（如 SSO 失败）；"
                    + "(2) Edge profile 未启用 Cookie 存储；(3) Playwright 与 Edge 进程不兼容";
                return null;
            }

            var cookieStr = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
            // cookies.Count 已大于 0，FirstOrDefault 不会返回 null
            var primaryDomain = cookies[0].Domain;

            // ---- Strict login verification: required cookie names + required domain ----
            // If RequiredCookieNames is configured (e.g. "acw_tc" for MiniMax),
            // verify at least one is present. Otherwise the captured cookies are likely just
            // landing-page tracking cookies (sensorsdata, GA, _oauth_state) and the real
            // session token never got saved.
            var missing = new List<string>();
            if (config.RequiredCookieNames != null && config.RequiredCookieNames.Count > 0)
            {
                var presentNames = new HashSet<string>(cookies.Select(c => c.Name),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var req in config.RequiredCookieNames)
                {
                    if (!presentNames.Contains(req)) missing.Add(req);
                }
            }
            if (!string.IsNullOrEmpty(config.RequiredCookieDomain))
            {
                var reqDom = config.RequiredCookieDomain.TrimStart('.');
                bool hasDomain = cookies.Any(c => !string.IsNullOrEmpty(c.Domain) &&
                    (c.Domain.TrimStart('.').Equals(reqDom, StringComparison.OrdinalIgnoreCase) ||
                     c.Domain.TrimStart('.').EndsWith("." + reqDom, StringComparison.OrdinalIgnoreCase)));
                if (!hasDomain)
                    FileLogger.Warn("BrowserLoginService",
                        $"RequiredCookieDomain '{config.RequiredCookieDomain}' not found in captured cookies. Names: {string.Join(",", cookies.Select(c => c.Name))}");
            }
            if (missing.Count > 0)
            {
                // NOTE: We already confirmed the session is valid by reaching /console/usage
                // (not bounced back to login). Some auth cookies (e.g. _token) may be HttpOnly and
                // scoped to a subdomain that CookiesAsync(urls) does not return for our query set.
                // Therefore a missing "required" name is only a WARNING now, not a hard failure —
                // otherwise a genuine login would be wrongly rejected. The captured cookies are what
                // the browser actually uses, so we save them regardless.
                FileLogger.Warn("BrowserLoginService",
                    $"Required cookies missing (non-fatal, session already confirmed): {string.Join(",", missing)}. " +
                    $"Captured names: {string.Join(",", cookies.Select(c => c.Name))}");
            }
            FileLogger.Info("BrowserLoginService",
                $"Captured {cookies.Count} cookies. Names: {string.Join(",", cookies.Select(c => c.Name))}");

            var cookieData = new BrowserCookieData
            {
                Cookie = cookieStr,
                UserAgent = userAgent ?? "UsageMonitor",
                SavedAt = DateTime.UtcNow,
                Count = cookies.Count,
                Domain = primaryDomain,
                ProviderId = config.ProviderId,
                LoginUrl = config.LoginUrl,
                RawCookies = cookies.Select(c => new BrowserCookieEntry
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expires = c.Expires,
                    HttpOnly = c.HttpOnly,
                    Secure = c.Secure,
                    SameSite = c.SameSite.ToString(),
                }).ToList(),
            };

            SaveCookieData(cookieData);

            // Also persist into main config.json so RefreshService picks up the new cookie
            // without requiring app restart. (RefreshService reads ProviderConfigs on next tick.)
            try { PersistToMainConfig(config, cookieData); }
            catch (Exception persistEx)
            {
                FileLogger.Warn("BrowserLoginService",
                    $"PersistToMainConfig threw (non-fatal): {persistEx.Message}");
            }

            return cookieData;
        }
        catch (OperationCanceledException)
        {
            // 用户主动取消 - 不要显示技术错误
            LastError = "[Stage UserCanceled] 用户取消了登录";
            return null;
        }
        catch (Exception ex)
        {
            // 记录真实错误信息，让 UI 能显示具体原因
            LastError = $"[Stage {ex.GetType().Name}] {ex.Message}";
            System.Diagnostics.Debug.WriteLine(
                "[BrowserLoginService] LoginAndExtractCookieAsync failed: " + ex);
            return null;
        }
        finally
        {
            if (context != null)
            {
                try { await context.CloseAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[BrowserLoginService] context.CloseAsync 失败：" + ex); }
            }
            playwright?.Dispose();
            try { Directory.Delete(tempProfile, recursive: true); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[BrowserLoginService] 清理临时 profile 失败：" + ex); }
        }
    }

    /// <summary>
    /// Check if current URL is still on a login page. Returns true = still on login page.
    /// <para>
    /// Uses STRICT EXCLUSION: a page is "logged in" if and only if:
    /// </para>
    /// <list type="bullet">
    ///   <item>URL is not on a known cross-subdomain auth gateway (account.*, oauth.*, etc.)</item>
    ///   <item>URL path does NOT contain any <c>LoginUrlKeywords</c> (login, unified-login, auth, oauth, signin, signup, etc.)</item>
    ///   <item>URL host MATCHES the expected logged-in host</item>
    /// </list>
    /// <para>
    /// Critically, this method does NOT require the URL path to be in <c>LoggedInPathKeywords</c>.
    /// Many sites (including MiniMax) redirect to a public landing page (e.g. /docs/guides/models-intro)
    /// after successful login, BEFORE we have a chance to navigate to /console/usage.
    /// Treating that landing page as "logged-in" is correct -- the next step is to actively
    /// navigate to <c>/console/usage</c> and verify the session by checking the response.
    /// </para>
    /// </summary>
    private static bool IsLoginUrl(string url, BrowserLoginConfig config)
    {
        if (string.IsNullOrEmpty(url)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return true;
        var urlPath = uri.AbsolutePath.ToLowerInvariant();
        var urlHost = uri.Host.ToLowerInvariant();

        // 1) Path contains a login keyword → still on login page
        if (config.LoginUrlKeywords != null)
        {
            foreach (var keyword in config.LoginUrlKeywords)
            {
                if (urlPath.Contains(keyword.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // 2) Determine expected host (priority: LoggedInHost > LoginSuccessHost > LoginUrl host)
        var expectedHost = config.LoggedInHost;
        if (string.IsNullOrEmpty(expectedHost)) expectedHost = config.LoginSuccessHost;
        if (string.IsNullOrEmpty(expectedHost)) expectedHost = ExtractHost(config.LoginUrl);

        if (!string.IsNullOrEmpty(expectedHost))
        {
            // Wrong host = still on cross-subdomain auth gateway (e.g. account.minimaxi.com)
            if (!string.Equals(urlHost, expectedHost, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 3) Otherwise, NOT a login page (regardless of path)
        // This means /docs/guides/models-intro, /, /anything-else are all "logged in".
        // The caller is responsible for force-navigating to /console/usage to verify
        // that the captured cookies actually carry a valid session.
        return false;
    }

    /// <summary>Extract host from URL. Empty string on failure.</summary>
    private static string ExtractHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
        return string.Empty;
    }

    /// <summary>Load saved cookie by ProviderId.</summary>
    public static BrowserCookieData? LoadCookieData(string providerId)
    {
        var path = GetCookieFilePath(providerId);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<BrowserCookieData>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Save cookie to JSON.</summary>
    public static void SaveCookieData(BrowserCookieData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        Directory.CreateDirectory(CookieDir);
        var path = GetCookieFilePath(data.ProviderId);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, options), Encoding.UTF8);
    }

    /// <summary>Delete saved cookie by ProviderId.</summary>
    public static bool DeleteCookieData(string providerId)
    {
        var path = GetCookieFilePath(providerId);
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch { return false; }
    }

    /// <summary>Check if saved cookie is still valid via HTTP test.</summary>
    public static async Task<bool> CheckCookieValidAsync(
        BrowserLoginConfig config,
        CancellationToken cancellationToken = default)
    {
        var data = LoadCookieData(config.ProviderId);
        if (data == null || string.IsNullOrEmpty(data.Cookie)) return false;

        if (string.IsNullOrEmpty(config.ValidateUrl)) return true;

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, config.ValidateUrl);
            request.Headers.Add("Cookie", data.Cookie);
            if (!string.IsNullOrEmpty(data.UserAgent))
            {
                request.Headers.UserAgent.ParseAdd(data.UserAgent);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            return (int)response.StatusCode == 200;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get cookie if valid, otherwise refresh by launching browser.
    /// If valid cookie file is missing/corrupted, also refresh.
    /// </summary>
    public static async Task<BrowserCookieData?> GetOrRefreshCookieAsync(
        BrowserLoginConfig config,
        CancellationToken cancellationToken = default)
    {
        if (await CheckCookieValidAsync(config, cancellationToken))
        {
            var existing = LoadCookieData(config.ProviderId);
            if (existing != null) return existing;
            // Valid by HTTP but file missing/corrupted - re-login
        }
        return await LoginAndExtractCookieAsync(config, cancellationToken);
    }

    /// <summary>Get cookie string in HTTP header format.</summary>
    public static string? GetCookieString(string providerId)
    {
        return LoadCookieData(providerId)?.Cookie;
    }

    /// <summary>Get cookie file path for ProviderId.</summary>
    private static string GetCookieFilePath(string providerId)
    {
        return Path.Combine(CookieDir, $"{providerId}.json");
    }
}