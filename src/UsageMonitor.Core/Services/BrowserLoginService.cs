using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
/// <para>
/// req-065 B4：去静态化改造，每个 Provider 持有独立实例，避免并发登录时 LastError 互相覆盖。
/// </para>
/// </summary>
public class BrowserLoginService
{
    /// <summary>
    /// req-060：静态 HttpClient 复用，避免每次 CheckCookieValidAsync 都 new HttpClient 导致 socket 耗尽。
    /// </summary>
    private static readonly System.Net.Http.HttpClient _sharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>req-060：JSON 序列化配置复用（写入用）。</summary>
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>req-060：JSON 反序列化配置复用（读取用）。</summary>
    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Optional ConfigService for in-memory rehydration after disk write.</summary>
    private readonly ConfigService? _configService;

    /// <summary>
    /// 创建 BrowserLoginService 实例。
    /// </summary>
    /// <param name="configService">
    /// 可选的 ConfigService，用于登录成功后自动重载内存配置。
    /// 若传入 null，则跳过主配置持久化（Cookie 仍保存到 cookies/*.json）。
    /// </param>
    public BrowserLoginService(ConfigService? configService = null)
    {
        _configService = configService;
        if (configService != null)
        {
            FileLogger.Info("BrowserLoginService", "ConfigService registered for auto-reload after login.");
        }
    }

    /// <summary>
    /// 将新捕获的 Cookie 持久化到主 config.json 的对应 ProviderConfigs 条目。
    /// <para>
    /// 安全要求（req-001）：必须经由 <see cref="ConfigService"/> 的加密保存路径写入，
    /// 使 Cookie 等敏感字段以 DPAPI 密文落盘，杜绝明文泄露。
    /// 若未注册 ConfigService（例如未传入构造函数参数的独立进程），
    /// 则跳过主配置写入（Cookie 仍由 <see cref="SaveCookieData"/> 存于 cookies/*.json），绝不明文写 config.json。
    /// </para>
    /// </summary>
    private void PersistToMainConfig(BrowserLoginConfig config, BrowserCookieData data)
    {
        try
        {
            if (_configService == null)
            {
                FileLogger.Warn("BrowserLoginService",
                    "ConfigService 未注册，跳过主配置持久化（Cookie 已保存到 cookies 目录，不写明文 config.json）");
                return;
            }

            FileLogger.Info("BrowserLoginService",
                $"Persisting cookies via ConfigService (encrypted) for providerId={config.ProviderId}");

            // 经 ConfigService 加密路径写入：GetProviderConfig -> SetValue -> UpdateProviderConfig
            // （UpdateProviderConfig 内部 EncryptSensitiveFields + Save，Cookie 命中敏感词表被 DPAPI 加密）。
            var cfg = _configService.GetProviderConfig(config.ProviderId);
            cfg.SetValue("Cookie", data.Cookie);
            cfg.SetValue("_userAgent", data.UserAgent);
            // 保底 Region，避免用户从未设置时缺字段（不覆盖已有值）
            if (string.IsNullOrEmpty(cfg.GetValue("Region")))
                cfg.SetValue("Region", "CN");

            _configService.UpdateProviderConfig(config.ProviderId, cfg);

            // req-fix-Kimi-GetLoginState-AutoDetectMode: dual-mode plugin (KimiDualModeProvider)
            // main ProviderId (e.g. kimi) differs from Web-mode internal ProviderId (e.g. kimi_web).
            // Above already wrote Cookie to kimi_web's config, KimiDualModeProvider.GetUsageAsync
            // can read it via _webProvider. But KimiDualModeProvider.GetUsageAsync's config
            // is the main ProviderId (kimi) config -- can't read Cookie field directly.
            // Here also mirror Cookie to main provider's config, and set QueryMode=web,
            // so KimiDualModeProvider.GetUsageAsync auto-picks Web mode, avoiding "API Key missing" error.
            // Only try when ProviderId contains underscore (kimi_web / deepseek_web etc dual-mode internal mode).
            int underscoreIdx = config.ProviderId.IndexOf('_');
            if (underscoreIdx > 0)
            {
                try
                {
                    var mainProviderId = config.ProviderId.Substring(0, underscoreIdx);
                    var mainCfg = _configService.GetProviderConfig(mainProviderId);
                    mainCfg.SetValue("Cookie", data.Cookie);
                    mainCfg.SetValue("_userAgent", data.UserAgent);
                    if (string.IsNullOrEmpty(mainCfg.GetValue("Region")))
                        mainCfg.SetValue("Region", "CN");
                    if (string.IsNullOrEmpty(mainCfg.GetValue("QueryMode")))
                        mainCfg.SetValue("QueryMode", "web");
                    _configService.UpdateProviderConfig(mainProviderId, mainCfg);
                    FileLogger.Info("BrowserLoginService",
                        $"Mirrored Cookie to dual-mode main provider={mainProviderId} (mode=web)");
                }
                catch (Exception mirrorEx)
                {
                    FileLogger.Warn("BrowserLoginService",
                        $"Mirror Cookie to main provider failed: {mirrorEx.Message}");
                }
            }

            if (!string.IsNullOrEmpty(_configService.LastSaveError))
                FileLogger.Warn("BrowserLoginService",
                    $"UpdateProviderConfig 报告保存错误：{_configService.LastSaveError}");
            else
                FileLogger.Info("BrowserLoginService",
                    $"Persisted cookies (encrypted). Cookie length={data.Cookie.Length}");
        }
        catch (Exception ex)
        {
            FileLogger.Error("BrowserLoginService", $"PersistToMainConfig failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从已登录页面提取声明的 localStorage 令牌（如 DeepSeek 的 userToken）。
    /// <para>对每个声明的 localStorage 键读取原始值；若为 JSON 且含 <c>value</c> 成员
    /// （DeepSeek appKit 存储格式）则取其 <c>value</c>，否则用原始字符串。</para>
    /// </summary>
    /// <param name="page">仍打开的已登录页面。</param>
    /// <param name="config">登录配置（LocalStorageTokens：配置字段名 → localStorage 键名）。</param>
    /// <returns>配置字段名 → 令牌值 字典；未声明或提取失败时为空。</returns>
    private static async Task<Dictionary<string, string>> ExtractLocalStorageTokensAsync(
        IPage page, BrowserLoginConfig config)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (config.LocalStorageTokens == null || config.LocalStorageTokens.Count == 0) return result;

        try
        {
            // 把“配置字段 → localStorage 键”映射传入浏览器端，读取后返回 JSON。
            var mapping = new Dictionary<string, string>(config.LocalStorageTokens);
            var json = await page.EvaluateAsync<string>(@"(mapping) => {
                const out = {};
                for (const field in mapping) {
                    const lsKey = mapping[field];
                    let raw = localStorage.getItem(lsKey);
                    if (!raw) continue;
                    try {
                        const p = JSON.parse(raw);
                        if (p && typeof p === 'object' && p.value) { out[field] = String(p.value); continue; }
                    } catch (e) { /* not JSON, use raw */ }
                    out[field] = raw;
                }
                return JSON.stringify(out);
            }", mapping);

            if (!string.IsNullOrWhiteSpace(json))
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed != null)
                {
                    foreach (var kv in parsed)
                        if (!string.IsNullOrWhiteSpace(kv.Value)) result[kv.Key] = kv.Value;
                }
            }
            FileLogger.Info("BrowserLoginService",
                $"Extracted {result.Count} localStorage token(s): {string.Join(",", result.Keys)}");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("BrowserLoginService", $"ExtractLocalStorageTokens failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 将 localStorage 令牌经 ConfigService 加密路径写入配置表（与 Cookie 同样走敏感字段 DPAPI 加密）。
    /// </summary>
    /// <param name="config">登录配置（提供 ProviderId）。</param>
    /// <param name="tokens">配置字段名 → 令牌值。</param>
    private void PersistLocalStorageTokens(BrowserLoginConfig config, Dictionary<string, string> tokens)
    {
        if (_configService == null)
        {
            FileLogger.Warn("BrowserLoginService",
                "ConfigService 未注册，跳过 localStorage 令牌持久化");
            return;
        }
        var cfg = _configService.GetProviderConfig(config.ProviderId);
        foreach (var kv in tokens)
            cfg.SetValue(kv.Key, kv.Value);
        _configService.UpdateProviderConfig(config.ProviderId, cfg);
        FileLogger.Info("BrowserLoginService",
            $"Persisted {tokens.Count} localStorage token(s) (encrypted) for {config.ProviderId}");
    }

    /// <summary>Cookie 持久化根目录</summary>
    private static readonly string CookieDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "cookies");

    /// <summary>
    /// 最近一次失败的错误信息（用于 UI 显示真实失败原因）。
    /// req-065 B4：改为实例属性，避免并发登录时互相覆盖。
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 启动临时 Edge 浏览器，等待用户完成登录，提取 Cookie 后关闭浏览器。
    /// </summary>
    /// <param name="config">浏览器登录配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提取的 Cookie 数据，失败时返回 null</returns>
    public async Task<BrowserCookieData?> LoginAndExtractCookieAsync(
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
                IgnoreHTTPSErrors = false,
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

                    // User authenticated. Force-navigate the visible tab to the protected usage
                    // page so (a) the user sees real usage data and (b) we confirm the session is valid.
                    // req-fix-Kimi跳转MiniMax：使用 config.ValidateUrl 替换硬编码的 MiniMax URL。
                    // 否则从 Kimi/Qoder/DeepSeek 等插件启动的浏览器都会跳转到 MiniMax 的页面，
                    // 在 MiniMax 服务端看来未登录 → 被重定向到 MiniMax 登录页 → 用户误以为跳转错误。
                    var postLoginTarget = !string.IsNullOrEmpty(config.ValidateUrl) ? config.ValidateUrl! : config.LoginUrl;
                    try
                    {
                        FileLogger.Info("BrowserLoginService",
                            $"Navigating visible tab to {postLoginTarget} (post-login target)");
                        await page.GotoAsync(postLoginTarget,
                            new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 30_000 });
                        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                            new PageWaitForLoadStateOptions { Timeout = 20_000 });
                    }
                    catch (Exception navEx)
                    {
                        FileLogger.Warn("BrowserLoginService", $"Navigate to {postLoginTarget} failed: {navEx.Message}");
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

            // localStorage 令牌提取（如 DeepSeek：鉴权令牌存于 localStorage.userToken 而非 Cookie）。
            // 提前到 cookies.Count 判定之前：纯 localStorage 鉴权的服务商可能抓不到任何 Cookie，
            // 但只要提取到声明的令牌即视为登录成功，不因 Cookie 为空而失败。
            var localStorageTokens = await ExtractLocalStorageTokensAsync(page, config);

            if ((cookies == null || cookies.Count == 0) && localStorageTokens.Count == 0)
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
                    + $"可能原因：(1) 登录后 {config.ProviderId} 服务端未设置 Cookie（如 SSO 失败）；"
                    + "(2) Edge profile 未启用 Cookie 存储；(3) Playwright 与 Edge 进程不兼容";
                return null;
            }

            var cookieStr = cookies == null ? string.Empty : string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
            var primaryDomain = (cookies != null && cookies.Count > 0) ? cookies[0].Domain : string.Empty;

            // ---- Strict login verification: required cookie names + required domain ----
            // If RequiredCookieNames is configured (e.g. "acw_tc" for MiniMax),
            // verify at least one is present. Otherwise the captured cookies are likely just
            // landing-page tracking cookies (sensorsdata, GA, _oauth_state) and the real
            // session token never got saved.
            var missing = new List<string>();
            if (cookies != null && config.RequiredCookieNames != null && config.RequiredCookieNames.Count > 0)
            {
                var presentNames = new HashSet<string>(cookies.Select(c => c.Name),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var req in config.RequiredCookieNames)
                {
                    if (!presentNames.Contains(req)) missing.Add(req);
                }
            }
            if (cookies != null && !string.IsNullOrEmpty(config.RequiredCookieDomain))
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
                    $"Captured names: {string.Join(",", cookies!.Select(c => c.Name))}");
            }
            FileLogger.Info("BrowserLoginService",
                $"Captured {cookies?.Count ?? 0} cookies, {localStorageTokens.Count} localStorage token(s)");

            var cookieData = new BrowserCookieData
            {
                Cookie = cookieStr,
                UserAgent = userAgent ?? "UsageMonitor",
                SavedAt = DateTime.UtcNow,
                Count = cookies?.Count ?? 0,
                Domain = primaryDomain,
                ProviderId = config.ProviderId,
                LoginUrl = config.LoginUrl,
                RawCookies = (cookies ?? Array.Empty<BrowserContextCookiesResult>()).Select(c => new BrowserCookieEntry
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

            // 携带 localStorage 令牌供 UI 回填（不落盘，仅内存传递）。
            cookieData.LocalStorageTokens = localStorageTokens;

            SaveCookieData(cookieData);

            // Also persist into main config.json so RefreshService picks up the new cookie
            // without requiring app restart. (RefreshService reads ProviderConfigs on next tick.)
            try
            {
                PersistToMainConfig(config, cookieData);
            }
            catch (Exception persistEx)
            {
                FileLogger.Warn("BrowserLoginService",
                    $"PersistToMainConfig threw (non-fatal): {persistEx.Message}");
            }

            // localStorage 令牌持久化到配置表（加密路径），供声明包 http 端点 {config:字段} 占位符引用。
            if (localStorageTokens.Count > 0)
            {
                try
                {
                    PersistLocalStorageTokens(config, localStorageTokens);
                }
                catch (Exception tokenEx)
                {
                    FileLogger.Warn("BrowserLoginService",
                        $"PersistLocalStorageTokens threw (non-fatal): {tokenEx.Message}");
                }
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

    /// <summary>Load saved cookie by ProviderId. Supports legacy Base64 format auto-migration to HMAC-signed format.</summary>
    /// <param name="providerId">Provider ID。</param>
    /// <param name="accountId">req-110 P2-2：账号 ID（可空）。非空时优先读账号级文件
    /// <c>cookies/{Provider}.{Account}.json</c>，缺失时回退 Provider 级旧文件（存量单账号兼容）。</param>
    public static BrowserCookieData? LoadCookieData(string providerId, string? accountId = null)
    {
        // req-110 P2-2：账号级文件优先，回退 Provider 级旧文件
        if (!string.IsNullOrWhiteSpace(accountId) &&
            !string.Equals(accountId, "default", StringComparison.Ordinal) &&
            File.Exists(GetCookieFilePath(providerId, accountId)))
        {
            return LoadCookieDataFromPath(GetCookieFilePath(providerId, accountId), providerId);
        }
        var path = GetCookieFilePath(providerId);
        if (!File.Exists(path)) return null;
        return LoadCookieDataFromPath(path, providerId);
    }

    /// <summary>从指定路径装载 cookie 文件（req-110 P2-2：账号级/Provider 级文件共用同一解密与旧格式迁移逻辑）。</summary>
    /// <param name="path">cookie 文件绝对路径。</param>
    /// <param name="providerId">Provider ID（审计日志用）。</param>
    private static BrowserCookieData? LoadCookieDataFromPath(string path, string providerId)
    {
        try
        {
            // req-090-001：先尝试新格式（HMAC 签名二进制）
            var rawBytes = File.ReadAllBytes(path);
            var dpapiCipher = CookieProtection.Unprotect(rawBytes);
            if (dpapiCipher != null)
            {
                // 新格式验签成功
                var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(dpapiCipher, null, DataProtectionScope.CurrentUser));
                CookieAuditLog.Write(providerId, CookieAuditLog.AuditAction.Load, true, CookieAuditLog.AuditSource.Auto);
                return JsonSerializer.Deserialize<BrowserCookieData>(json, s_readOptions);
            }

            // 回退旧格式（纯 Base64 DPAPI 文本）→ 自动迁移到新格式
            var encryptedJson = File.ReadAllText(path, Encoding.UTF8);
            if (CookieProtection.IsLegacyFormat(encryptedJson))
            {
                var json = Decrypt(encryptedJson);
                var data = JsonSerializer.Deserialize<BrowserCookieData>(json, s_readOptions);
                if (data != null)
                {
                    FileLogger.Info("BrowserLoginService", $"Cookie 旧格式自动迁移: {providerId}");
                    SaveCookieData(data); // 迁移到新格式
                    CookieAuditLog.Write(providerId, CookieAuditLog.AuditAction.Load, true, CookieAuditLog.AuditSource.Auto, "legacy-migrated");
                }
                return data;
            }

            // 新格式但验签失败（可能被篡改）
            FileLogger.Warn("BrowserLoginService", $"Cookie 文件验签失败，可能已被篡改: {providerId}");
            CookieAuditLog.Write(providerId, CookieAuditLog.AuditAction.Load, false, CookieAuditLog.AuditSource.Auto, "hmac-verification-failed");
            File.Delete(path); // 删除被篡改的文件
            return null;
        }
        catch (Exception ex)
        {
            CookieAuditLog.Write(providerId, CookieAuditLog.AuditAction.Load, false, CookieAuditLog.AuditSource.Auto, ex.Message);
            return null;
        }
    }

    /// <summary>Save cookie to JSON with DPAPI encryption + HMAC-SHA256 signature.</summary>
    /// <param name="data">cookie 数据。</param>
    /// <param name="accountId">req-110 P2-2：账号 ID（可空）。非空且非 "default" 时写账号级文件，否则写 Provider 级旧路径（向后兼容）。</param>
    public static void SaveCookieData(BrowserCookieData data, string? accountId = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        Directory.CreateDirectory(CookieDir);
        var path = (!string.IsNullOrWhiteSpace(accountId) && !string.Equals(accountId, "default", StringComparison.Ordinal))
            ? GetCookieFilePath(data.ProviderId, accountId)
            : GetCookieFilePath(data.ProviderId);

        var json = JsonSerializer.Serialize(data, s_writeOptions);
        var dpapiCipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        // req-090-001：HMAC 签名 + 新格式写入
        var signed = CookieProtection.Protect(dpapiCipher);
        File.WriteAllBytes(path, signed);
        CookieAuditLog.Write(data.ProviderId, CookieAuditLog.AuditAction.Save, true, CookieAuditLog.AuditSource.Auto);
    }

    /// <summary>使用DPAPI加密字符串</summary>
    private static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>使用DPAPI解密字符串</summary>
    private static string Decrypt(string cipherText)
    {
        var bytes = Convert.FromBase64String(cipherText);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
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
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, config.ValidateUrl);
            // req-065 B6：Cookie header injection防护，清理控制字符
            var safeCookie = CookieHeaderSanitizer.Sanitize(data.Cookie);
            if (!request.Headers.TryAddWithoutValidation("Cookie", safeCookie))
            {
                FileLogger.Warn("BrowserLoginService", "Cookie header rejected after sanitization in ValidateCookieAsync");
            }
            if (!string.IsNullOrEmpty(data.UserAgent))
            {
                request.Headers.UserAgent.ParseAdd(data.UserAgent);
            }

            using var response = await _sharedHttp.SendAsync(request, cancellationToken);
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
    public async Task<BrowserCookieData?> GetOrRefreshCookieAsync(
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

    /// <summary>req-110 P2-2：账号级 cookie 文件路径（cookies/{Provider}.{Account}.json）。</summary>
    /// <param name="providerId">Provider ID。</param>
    /// <param name="accountId">账号 ID。</param>
    private static string GetCookieFilePath(string providerId, string accountId)
    {
        return Path.Combine(CookieDir, $"{providerId}.{accountId}.json");
    }
}
