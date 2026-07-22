using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.MiniMax;

/// <summary>
/// Playwright-based DOM extractor for MiniMax usage page.
/// Strategy: launch headless msedge with injected saved cookies, navigate to /console/usage,
/// extract data from DOM (aria-labels for 5h/weekly/video/total + captured XHR JSON for trend/heatmap).
/// This is the PRIMARY data source (per project decision 2026-07-13).
/// API calls are kept as fallback only.
/// </summary>
internal static class MiniMaxDomExtractor
{
    /// <summary>Source name used in FileLogger</summary>
    private const string LogSource = "MiniMaxDomExtractor";

    // req-067 B22：Regex 编译为 static readonly，避免每次调用都重新编译
    private static readonly Regex PercentRegex = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    /// <summary>req-058：限制最大并发浏览器实例数，避免手动刷新+定时刷新同时启动两个 Edge。</summary>
    private static readonly SemaphoreSlim BrowserSemaphore = new(1, 1);

    /// <summary>req-058：DOM 抽取结果缓存（3 分钟内不重复抽取）。</summary>
    private static UsageInfo? _cachedResult;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly object CacheLock = new();

    /// <summary>
    /// Build a UsageInfo by reading the logged-in MiniMax usage page via Playwright.
    /// Returns null on failure (cookie invalid, page unreachable, browser error).
    /// req-058：加入并发限制（SemaphoreSlim）和 3 分钟结果缓存。
    /// </summary>
    /// <param name="cookie">Cookie string from %AppData%\UsageMonitor\cookies\MiniMax.json</param>
    /// <param name="userAgent">Browser User-Agent for replay</param>
    /// <param name="region">Region identifier: "CN" (default) or "Global". Affects cookie domain and navigation URL.</param>
    /// <param name="ct">Cancellation</param>
    public static async Task<UsageInfo?> ExtractAsync(
        string cookie, string? userAgent, string region = "CN", CancellationToken ct = default)
    {
        FileLogger.Info(LogSource, $"ExtractAsync started. cookieLen={cookie?.Length ?? 0}, uaLen={userAgent?.Length ?? 0}");
        if (string.IsNullOrWhiteSpace(cookie))
        {
            FileLogger.Warn(LogSource, "Cookie is empty, aborting");
            return null;
        }

        // req-058：检查缓存（3 分钟内不重复抽取）
        lock (CacheLock)
        {
            if (_cachedResult != null && DateTime.Now < _cacheExpiry)
            {
                FileLogger.Info(LogSource, "Returning cached DOM result (within 3-min window)");
                return _cachedResult;
            }
        }

        // req-058：并发限制——等待浏览器槽位（最多等 30 秒）
        if (!await BrowserSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            FileLogger.Warn(LogSource, "Browser semaphore timeout (another extraction in progress), skip");
            return null;
        }

        try
        {
            // 双重检查：等待期间可能已有另一个线程完成抽取并填充缓存
            lock (CacheLock)
            {
                if (_cachedResult != null && DateTime.Now < _cacheExpiry)
                {
                    FileLogger.Info(LogSource, "Cache filled while waiting for semaphore, returning cached");
                    return _cachedResult;
                }
            }

            var result = await ExtractCoreAsync(cookie, userAgent, region, ct);

            // 成功时填充缓存
            if (result != null && result.IsSuccess)
            {
                lock (CacheLock)
                {
                    _cachedResult = result;
                    _cacheExpiry = DateTime.Now.AddMinutes(3);
                }
            }

            return result;
        }
        finally
        {
            BrowserSemaphore.Release();
        }
    }

    /// <summary>
    /// req-058：实际的 Playwright DOM 抽取核心逻辑（从原 ExtractAsync 拆分）。
    /// </summary>
    private static async Task<UsageInfo?> ExtractCoreAsync(
        string cookie, string? userAgent, string region, CancellationToken ct)
    {

        var tempProfile = Path.Combine(Path.GetTempPath(),
            $"UsageMonitor_DomExtract_{Guid.NewGuid():N}");
        IPlaywright? playwright = null;
        IBrowserContext? context = null;
        var stepSw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(tempProfile);
            FileLogger.Debug(LogSource, $"Temp profile: {tempProfile}");
            playwright = await Playwright.CreateAsync();
            FileLogger.Debug(LogSource, $"Playwright created (took {stepSw.ElapsedMilliseconds}ms)");

            context = await playwright.Chromium.LaunchPersistentContextAsync(
                tempProfile,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = "msedge",
                    Headless = true,
                    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
                    UserAgent = string.IsNullOrWhiteSpace(userAgent)
                        ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                          "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                        : userAgent,
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
                });
            FileLogger.Debug(LogSource, $"Edge launched in {stepSw.ElapsedMilliseconds}ms total");

            // Inject saved cookies. Playwright requires {Name, Value, Domain or Url} per entry.
            // req-062 B1: Pass region to dynamically select cookie domain (.minimaxi.com vs .minimax.io)
            var cookieList = ParseCookieString(cookie, region);
            if (cookieList.Count == 0)
            {
                FileLogger.Warn(LogSource, "Empty cookie list parsed, skip");
                return null;
            }
            FileLogger.Info(LogSource, $"Parsed {cookieList.Count} cookies");
            FileLogger.Debug(LogSource, $"First cookie domain='{cookieList[0].Domain}' url='{cookieList[0].Url ?? "<null>"}'");

            // Domain is always set in ParseCookieString, so no further fallback needed.

            try
            {
                await context.AddCookiesAsync(cookieList);
                FileLogger.Debug(LogSource, $"Cookies injected into context");
            }
            catch (Exception cookieEx)
            {
                FileLogger.Error(LogSource, $"AddCookiesAsync failed: {cookieEx.Message}", cookieEx);
                throw;
            }

            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

            // Capture network responses for /backend/account/token_plan/* endpoints
            // (the trend chart, heatmap, credit all come from here)
            // 另订阅档位来自 /v1/api/openplatform/charge/combo/cycle_audio_resource_package
            // req-062 B2: Use ConcurrentDictionary for thread-safe response capture
            var capturedResponses = new ConcurrentDictionary<string, string>();
            page.Response += async (_, response) =>
            {
                try
                {
                    var url = response.Url;
                    // 同时命中用量相关接口与订阅档位接口
                    var isUsageApi = url.Contains("/backend/account/token_plan", StringComparison.OrdinalIgnoreCase);
                    var isSubscriptionApi = url.Contains("cycle_audio_resource_package", StringComparison.OrdinalIgnoreCase);
                    if (response.Status == 200 && (isUsageApi || isSubscriptionApi))
                    {
                        var body = await response.TextAsync();
                        capturedResponses[url] = body;
                        FileLogger.Info(LogSource, $"Captured API: {url} (size={body.Length}B)");
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Warn(LogSource, $"Response capture failed for {response.Url}: {ex.Message}");
                }
            };
            page.RequestFailed += (_, req) =>
            {
                FileLogger.Warn(LogSource, $"Request failed: {req.Method} {req.Url} - {req.Failure}");
            };
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                    FileLogger.Warn(LogSource, $"Browser console error: {msg.Text}");
            };

            // Navigate to the usage page; wait for chart rendering.
            // req-062 B1: Select URL based on region (CN: platform.minimaxi.com, Global: platform.minimax.io)
            var usageUrl = string.Equals(region, "Global", StringComparison.OrdinalIgnoreCase)
                ? "https://platform.minimax.io/console/usage"
                : "https://platform.minimaxi.com/console/usage";
            FileLogger.Info(LogSource, $"Navigating to {usageUrl} ...");
            var navSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // req-062 B19: Use Commit instead of NetworkIdle for faster navigation,
                // then wait for canvas element to ensure chart rendering
                await page.GotoAsync(usageUrl,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.Commit,
                        Timeout = 30_000,
                    });
            }
            catch (Exception navEx)
            {
                FileLogger.Warn(LogSource, $"GotoAsync exception: {navEx.Message}");
            }
            FileLogger.Info(LogSource, $"Navigation completed in {navSw.ElapsedMilliseconds}ms (URL: {page.Url})");
            ct.ThrowIfCancellationRequested();

            // req-062 B19: Wait for canvas element (echarts container) instead of fixed timeout
            try
            {
                await page.WaitForSelectorAsync("canvas", new PageWaitForSelectorOptions { Timeout = 10_000 });
                FileLogger.Debug(LogSource, "Canvas element detected, chart likely rendered");
            }
            catch (Exception canvasEx)
            {
                FileLogger.Warn(LogSource, $"Canvas wait timeout (chart may not be rendered): {canvasEx.Message}");
            }
            ct.ThrowIfCancellationRequested();

            // Verify we're not redirected to login page
            var currentUrl = page.Url;
            if (currentUrl.Contains("/unified-login", StringComparison.OrdinalIgnoreCase) ||
                currentUrl.Contains("login_redirect", StringComparison.OrdinalIgnoreCase))
            {
                FileLogger.Warn(LogSource, $"Cookie invalid: redirected to {currentUrl}");
                WriteDebug(capturedResponses, "cookie-invalid", currentUrl, await page.TitleAsync());
                return null;
            }
            FileLogger.Info(LogSource, $"Page title: {await page.TitleAsync()}");

            // Extract DOM metrics
            var metrics = await ExtractDomMetricsAsync(page);
            ct.ThrowIfCancellationRequested();
            FileLogger.Info(LogSource,
                $"DOM extracted: 5h='{metrics.Interval5hAriaLabel}', weekly='{metrics.WeeklyAriaLabel}', video='{metrics.VideoAriaLabel}'");

            // Combine: prefer XHR JSON for fuller data, fall back to DOM for 5h/weekly/video.
            BuildUsageInfo(metrics, capturedResponses, cookieList.Count,
                out var usageInfo, out var extras);

            FileLogger.Info(LogSource,
                $"Built UsageInfo. primaryPct={(double)usageInfo.UsedAmount:F2}, " +
                $"endpoint={capturedResponses.Count}, total={stepSw.ElapsedMilliseconds}ms");

            WriteDebug(capturedResponses, $"ok-{DateTime.Now:yyyyMMdd-HHmmss}", currentUrl, await page.TitleAsync());
            return usageInfo;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogSource, $"ExtractAsync failed: {ex.GetType().Name}: {ex.Message}", ex);
            System.Diagnostics.Debug.WriteLine($"[MiniMaxDomExtractor] Exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (context != null) await context.CloseAsync(); } catch (Exception ex) { FileLogger.Warn(LogSource, $"context.Close failed: {ex.Message}"); }
            try { playwright?.Dispose(); } catch { }
            try
            {
                if (Directory.Exists(tempProfile))
                    Directory.Delete(tempProfile, true);
            }
            catch { }
            FileLogger.Debug(LogSource, $"ExtractAsync done. Total: {stepSw.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// Read percentages/counts/times from DOM using stable selectors.
    /// Returns a Metrics record. Returned values may be null when missing.
    /// 使用多语言兼容的稳定选择器策略：优先按 DOM 结构路径，兜底按 aria-label 关键词匹配。
    /// </summary>
    private static async Task<DomMetrics> ExtractDomMetricsAsync(IPage page)
    {
        // Run all DOM lookups in the browser using `evaluate`.
        var raw = await page.EvaluateAsync<string>(@"() => {
            // 稳定选择器策略：优先按 DOM 结构路径定位，兜底按 aria-label 关键词匹配（支持多语言）
            const findByStructure = (index) => {
                // 策略1：按 DOM 结构路径 - 查找所有带 aria-label 的 div，按文档顺序取第 N 个
                const divs = Array.from(document.querySelectorAll('div[aria-label]'));
                if (divs.length > index) return divs[index];
                return null;
            };
            
            const findByAriaLabel = (patterns) => {
                // 策略2：按 aria-label 关键词匹配（支持多语言）
                const divs = Array.from(document.querySelectorAll('div[aria-label]'));
                for (const pattern of patterns) {
                    const el = divs.find(x => {
                        const label = x.getAttribute('aria-label') || '';
                        return label.toLowerCase().includes(pattern.toLowerCase());
                    });
                    if (el) return el;
                }
                return null;
            };
            
            const get = (el) => {
                if (!el) return null;
                // First <span> inside the el is the percent number (e.g. ""66%"")
                const span = el.querySelector('span');
                return span ? span.textContent.trim() : el.textContent.trim();
            };
            
            const getAll = (el) => {
                if (!el) return null;
                return el.innerText;
            };
            
            // 5h 限额：优先按结构（第1个带 aria-label 的 div），兜底按关键词匹配
            const interval5hEl = findByStructure(0) || findByAriaLabel(['5h', '5小时', 'interval', '限额']);
            
            // 周限额：优先按结构（第2个带 aria-label 的 div），兜底按关键词匹配
            const weeklyEl = findByStructure(1) || findByAriaLabel(['周', 'week', 'weekly', '限额']);
            
            // 视频赠送：按关键词匹配（多语言）
            const videoEl = findByAriaLabel(['视频', 'video', '赠送', 'gift']);
            
            return JSON.stringify({
                interval5h: get(interval5hEl),
                weekly:    get(weeklyEl),
                video:     getAll(videoEl),
                credit:    document.body.innerText.match(/\u79ef\u5206[\s\S]{0,50}/)?.[0] || null,
                pageTitle: document.title
            });
        }");

        var m = new DomMetrics();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            m.Interval5hAriaLabel = GetStr(root, "interval5h");
            m.WeeklyAriaLabel = GetStr(root, "weekly");
            m.VideoAriaLabel = GetStr(root, "video");
            m.CreditHint = GetStr(root, "credit");
            m.PageTitle = GetStr(root, "pageTitle");

            // Extract percentages
            m.IntervalUsedPercent = ExtractPercent(m.Interval5hAriaLabel);
            m.WeeklyUsedPercent = ExtractPercent(m.WeeklyAriaLabel);
            m.IntervalTotalPercent = ExtractTotalPercent(m.Interval5hAriaLabel);
            m.WeeklyTotalPercent = ExtractTotalPercent(m.WeeklyAriaLabel);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MiniMaxDomExtractor] DOM parse error: {ex.Message}");
        }
        return m;

        // Local helpers
        static string? GetStr(JsonElement root, string key)
        {
            if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }
    }

    /// <summary>Extract first percentage like ""66%"" from a string.</summary>
    private static double ExtractPercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var m = PercentRegex.Match(text);
        if (m.Success && double.TryParse(m.Groups[1].Value, out var v)) return v;
        return -1;
    }

    /// <summary>Extract second percentage (total) like ""100%"" from ""已用 66% / 总额 100%"" pattern.</summary>
    private static double ExtractTotalPercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var matches = PercentRegex.Matches(text);
        if (matches.Count >= 2 && double.TryParse(matches[1].Groups[1].Value, out var v)) return v;
        // fall back to first
        if (matches.Count >= 1 && double.TryParse(matches[0].Groups[1].Value, out var v2)) return v2;
        return -1;
    }

    /// <summary>
    /// Combine DOM metrics with captured JSON responses to build UsageInfo + extras dict.
    /// prefers JSON for trend data, DOM for primary numbers.
    /// req-062 B2: Accept ConcurrentDictionary for thread-safe response capture.
    /// </summary>
    private static void BuildUsageInfo(
        DomMetrics dom,
        ConcurrentDictionary<string, string> captured,
        int cookieCount,
        out UsageInfo usageInfo,
        out Dictionary<string, object> extras)
    {
        usageInfo = new UsageInfo
        {
            ProviderId = "MiniMax",
            ProviderName = "MiniMax",
            IsSuccess = true,
            // req-067 B21：统一使用 UTC 时间存储，避免时区问题
            LastUpdated = DateTime.UtcNow
        };

        extras = new Dictionary<string, object>
        {
            ["domExtract"] = true,
            ["cookieCount"] = cookieCount,
            ["capturedEndpointCount"] = captured.Count,
            ["pageTitle"] = dom.PageTitle ?? string.Empty,
        };

        // Primary metric (5h used %). Prefer the reliable remains_percent JSON below;
        // fall back to DOM aria-label only if JSON is unavailable.
        double interval5hUsed = dom.IntervalUsedPercent;
        double weeklyUsed = dom.WeeklyUsedPercent;

        // Parse captured JSON responses (the reliable source).
        foreach (var kv in captured)
        {
            var url = kv.Key.ToLowerInvariant();
            try
            {
                if (url.Contains("remains_percent"))
                {
                    // 5h / weekly / video quotas. This is the PRIMARY, reliable data.
                    using var doc = JsonDocument.Parse(kv.Value);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("model_remains", out var models) && models.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in models.EnumerateArray())
                        {
                            var name = m.TryGetProperty("model_name", out var mn) ? mn.GetString() : null;
                            if (name == "general")
                            {
                                interval5hUsed = ParsePercentStr(m, "current_interval_used_percent", interval5hUsed);
                                weeklyUsed = ParsePercentStr(m, "current_weekly_used_percent", weeklyUsed);
                                extras["mm_5hUsedPercent"] = interval5hUsed;
                                extras["mm_weeklyUsedPercent"] = weeklyUsed;
                                if (m.TryGetProperty("end_time", out var e5) && e5.TryGetInt64(out var e5ms) && e5ms > 0)
                                    extras["mm_5hResetAt"] = DateTimeOffset.FromUnixTimeMilliseconds(e5ms).LocalDateTime;
                                if (m.TryGetProperty("weekly_end_time", out var ew) && ew.TryGetInt64(out var ewms) && ewms > 0)
                                    extras["mm_weeklyResetAt"] = DateTimeOffset.FromUnixTimeMilliseconds(ewms).LocalDateTime;
                            }
                            else if (name == "video")
                            {
                                // req-062 B20: Fix field semantics - current_*_used_count is actually REMAINING count
                                // (per OpenClaw Issue #81156: current_*_usage_count means "remaining count", not used)
                                // We store as "remaining" and calculate used = total - remaining
                                var videoIntervalRemaining = GetLong(m, "current_interval_used_count");
                                var videoIntervalTotal = GetLong(m, "current_interval_total_count");
                                var videoWeeklyRemaining = GetLong(m, "current_weekly_used_count");
                                var videoWeeklyTotal = GetLong(m, "current_weekly_total_count");
                                
                                extras["mm_videoIntervalRemaining"] = videoIntervalRemaining;
                                extras["mm_videoIntervalTotal"] = videoIntervalTotal;
                                extras["mm_videoWeeklyRemaining"] = videoWeeklyRemaining;
                                extras["mm_videoWeeklyTotal"] = videoWeeklyTotal;
                                
                                // Legacy keys: calculate used = total - remaining (fix semantic inversion)
                                extras["mm_videoIntervalUsed"] = videoIntervalTotal - videoIntervalRemaining;
                                extras["mm_videoWeeklyUsed"] = videoWeeklyTotal - videoWeeklyRemaining;
                            }
                        }
                    }
                }
                else if (url.Contains("usage_summary"))
                {
                    using var doc = JsonDocument.Parse(kv.Value);
                    var root = doc.RootElement;
                    extras["mm_totalDays"] = root.TryGetProperty("total_days", out var td1) ? td1.GetInt32() : 0;
                    extras["mm_activeDays"] = root.TryGetProperty("active_days", out var ad1) ? ad1.GetInt32() : 0;
                    extras["mm_totalTokens"] = root.TryGetProperty("total_token_consumed", out var tt1) ? tt1.GetString() ?? "" : "";
                    if (root.TryGetProperty("usage_ranking_percent", out var urp1) && urp1.ValueKind == JsonValueKind.Number)
                        extras["mm_rankingPercent"] = urp1.GetDouble();

                    if (root.TryGetProperty("daily_token_usage", out var dtu) && dtu.ValueKind == JsonValueKind.Array)
                    {
                        var listD = new List<long>();
                        foreach (var item in dtu.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var n))
                                listD.Add(n);
                        extras["dailyTokenUsage"] = listD;
                    }

                    // 折线图 / 热力图数据源：优先用 date_model_usage（带 date 的每日 total_token，最可靠），
                    // 回退到上面的 daily_token_usage（纯数字数组、无日期）。两个数组都按日期升序。
                    // 提取为 mm_dailyTokenValues（每日 token）+ mm_dailyTokenDates（对应 yyyy-MM-dd），
                    // 供 App 层折线图（趋势）与热力图（日历）渲染；展示相关的裁剪/映射交给 App 层处理。
                    // req-010：同步累加 token 加权的缓存命中率（mm_cacheHitPercent）供热力图 tooltip 使用。
                    var dailyDates = new List<string>();
                    var dailyValues = new List<long>();
                    var dailyCacheHitPercents = new List<double>(); // req-034 修复：每独立的缓存命中率
                    double cacheHitSumNum = 0; // Σ(token × cacheHitPercent) 分子
                    double cacheHitSumDen = 0; // Σ(token) 分母
                    if (root.TryGetProperty("date_model_usage", out var dmu) && dmu.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dmu.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            var date = item.TryGetProperty("date", out var dv) && dv.ValueKind == JsonValueKind.String
                                ? dv.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(date)) continue;
                            long tt = item.TryGetProperty("total_token", out var tv)
                                      && tv.ValueKind == JsonValueKind.Number && tv.TryGetInt64(out var ttn)
                                      ? ttn : 0;
                            dailyDates.Add(date);
                            dailyValues.Add(tt);

                            // req-010：累加 token 加权的缓存命中率（该天被跳过不会影响其他天）
                            double dayCacheHit = -1; // 该天缓存命中率，-1 表示无数据
                            if (tt > 0 &&
                                item.TryGetProperty("cache_hit_percent", out var chp) &&
                                chp.ValueKind == JsonValueKind.String)
                            {
                                var s = chp.GetString() ?? "";
                                // 兼容 "33.3%" 与 "33.3" 两种格式
                                if (s.EndsWith("%", StringComparison.Ordinal))
                                    s = s.Substring(0, s.Length - 1);
                                if (System.Double.TryParse(s, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var pct))
                                {
                                    cacheHitSumNum += tt * pct;
                                    cacheHitSumDen += tt;
                                    dayCacheHit = pct;
                                }
                            }
                            dailyCacheHitPercents.Add(dayCacheHit);
                        }
                    }
                    // date_model_usage 缺失时回退到 daily_token_usage 的数值（无日期，热力图将按“最近 N 天”推断）
                    if (dailyValues.Count == 0
                        && extras.TryGetValue("dailyTokenUsage", out var dtuObj)
                        && dtuObj is List<long> dtuList)
                    {
                        dailyValues = dtuList;
                    }
                    if (dailyValues.Count > 0)
                        extras["mm_dailyTokenValues"] = dailyValues;
                    if (dailyDates.Count > 0)
                        extras["mm_dailyTokenDates"] = dailyDates;
                    // req-034 修复：存储每独立的缓存命中率供热力图 tooltip 使用
                    if (dailyCacheHitPercents.Count > 0)
                        extras["mm_dailyCacheHitPercents"] = dailyCacheHitPercents;
                    // req-010：写入按 token 加权的全局平均缓存命中率（0-100）
                    if (cacheHitSumDen > 0)
                    {
                        var avg = cacheHitSumNum / cacheHitSumDen;
                        if (avg >= 0 && avg <= 100) extras["mm_cacheHitPercent"] = avg;
                    }
                    // most_active_day.token_count is a FORMATTED STRING like "552.49M" (not a number).
                    if (root.TryGetProperty("most_active_day", out var mad) && mad.ValueKind == JsonValueKind.Object)
                    {
                        var date = mad.TryGetProperty("date", out var d) ? d.GetString() : "";
                        var token = mad.TryGetProperty("token_count", out var t)
                            ? (t.ValueKind == JsonValueKind.String ? t.GetString() : t.ToString())
                            : "";
                        extras["mm_mostActiveDay"] = $"{date} ({token})";
                    }
                }
                else if (url.Contains("/credit"))
                {
                    using var doc = JsonDocument.Parse(kv.Value);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("remaining_credits", out var rc) && rc.ValueKind == JsonValueKind.Number)
                        extras["mm_remainingCredits"] = rc.GetDouble();
                    if (root.TryGetProperty("total_credits", out var tcc) && tcc.ValueKind == JsonValueKind.Number)
                        extras["mm_totalCredits"] = tcc.GetDouble();
                }
                else if (url.Contains("cycle_audio_resource_package", StringComparison.OrdinalIgnoreCase))
                {
                    // 当前订阅档位（"Token Plan · TokenPlanMax-年度会员" 等）
                    // 响应里包含 current_subscribe 对象，字段名、语义遵循用量页 JS bundle 调研结论。
                    using var doc = JsonDocument.Parse(kv.Value);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("current_subscribe", out var cs) &&
                        cs.ValueKind == JsonValueKind.Object)
                    {
                        // 是否已订阅：以 curr_subscribe_combo_id 是否存在且非空为准
                        var hasCombo = cs.TryGetProperty("curr_subscribe_combo_id", out var cc) &&
                                       cc.ValueKind != JsonValueKind.Null &&
                                       !string.IsNullOrWhiteSpace(cc.ToString());
                        extras["mm_subscriptionActive"] = hasCombo;

                        string GetStr(string prop)
                        {
                            if (cs.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
                                return v.GetString() ?? "";
                            return "";
                        }
                        var title = GetStr("current_subscribe_title");
                        // 仅在已订阅且标题存在时写入，未订阅或不返回标题的场景交给前端回退。
                        if (hasCombo && !string.IsNullOrWhiteSpace(title))
                        {
                            extras["mm_subscriptionTitle"] = title;
                            FileLogger.Info(LogSource, $"Current subscription title: {title}");
                        }

                        var priceText = GetStr("current_subscribe_price");
                        if (!string.IsNullOrWhiteSpace(priceText))
                        {
                            extras["mm_subscriptionPrice"] = priceText;
                        }

                        // 周期类型（1=月度、3=年度）在订阅文案中体现
                        if (cs.TryGetProperty("current_subscribe_cycle_type", out var cyc) &&
                            cyc.ValueKind == JsonValueKind.Number)
                        {
                            extras["mm_subscriptionCycleType"] = cyc.GetInt32();
                        }

                        // 到期时间（毫秒，应用长期记录遵循项目毫秒约定）
                        if (cs.TryGetProperty("current_subscribe_end_time", out var et) &&
                            et.ValueKind == JsonValueKind.Number && et.TryGetInt64(out var etMs) && etMs > 0)
                        {
                            extras["mm_subscriptionEndTime"] =
                                DateTimeOffset.FromUnixTimeMilliseconds(etMs).LocalDateTime;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MiniMaxDomExtractor] Parse error for {kv.Key}: {ex.Message}");
            }
        }

        // Set primary display: 5h used percent drives the progress bar.
        double primaryPct = interval5hUsed >= 0 ? interval5hUsed : 0;
        usageInfo.UsedAmount = (decimal)primaryPct;
        usageInfo.TotalAmount = 100m;
        usageInfo.Unit = "%";
        // req-005-011：主指标为 5h 已用百分比，写入强类型 Quantity(PercentUnit)，与 UsedAmount 一致，GetUsagePercentage 不变。
        usageInfo.PopulateQuantityFromLegacy();
        extras["intervalUsedPercent"] = interval5hUsed;
        extras["weeklyUsedPercent"] = weeklyUsed;

        // 声明当前插件能交给主窗口UI的渲染能力。
        // 主窗口卡片按这个集合决定是否展示订阅胶囊、5h/周进度条、视频赠送、汇总信息。
        // 后续接入折线图 / 热力图时，仅需向本数组追加 "trendLine" / "heatMap" 等关键字并准备数据。
        var renderKinds = new List<string>
        {
            "subscriptionTitle",   // 订阅档位胶囊（仅当 mm_subscriptionTitle 存在时显示具体文案）
            "primaryBar",          // 5h 限额进度条
            "weeklyBar",           // 周限额进度条
            "videoProgress",       // 视频赠送已用/总额（5h 与周维度）
            "summary",             // 累计 token / 最活跃日
            "ranking",             // 排名百分比 / 活跃天数
            "credits"              // 积分余额
        };
        extras["mm_render_kinds"] = renderKinds;

        usageInfo.Extra = extras;
    }

    /// <summary>Parse a "NN%" string property to a double (e.g. "13%" -> 13). Returns fallback if missing/invalid.</summary>
    private static double ParsePercentStr(JsonElement obj, string prop, double fallback)
    {
        if (obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = (v.GetString() ?? "").Trim().TrimEnd('%');
            if (double.TryParse(s, out var d)) return d;
        }
        return fallback;
    }

    /// <summary>Read a long property; returns 0 when missing or non-numeric.</summary>
    private static long GetLong(JsonElement obj, string prop)
    {
        if (obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
            return n;
        return 0;
    }

    /// <summary>
    /// Parse a Cookie header string into Playwright Cookie objects.
    /// <para>
    /// Playwright requires each cookie to have either Url or Domain set;
    /// using Domain lets Playwright match against multiple subdomains
    /// (www / platform / account) without per-cookie URL tuning.
    /// </para>
    /// <para>
    /// IMPORTANT: When Domain is set, Url must remain null — Playwright throws
    /// "Cookie should have either url or domain" if both have non-empty values
    /// that do not agree. We never set Url here.
    /// </para>
    /// <para>
    /// req-062 B1: Added region parameter to dynamically select cookie domain.
    /// CN region uses .minimaxi.com, Global region uses .minimax.io.
    /// </para>
    /// </summary>
    /// <param name="cookieString">Raw cookie header string (name=value; name2=value2)</param>
    /// <param name="region">Region identifier: "CN" (default) or "Global"</param>
    private static List<Cookie> ParseCookieString(string cookieString, string region = "CN")
    {
        var list = new List<Cookie>();
        if (string.IsNullOrWhiteSpace(cookieString)) return list;

        // req-062 B1: Dynamically select domain based on region
        var domain = string.Equals(region, "Global", StringComparison.OrdinalIgnoreCase)
            ? ".minimax.io"
            : ".minimaxi.com";

        foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx <= 0) continue;
            var name = part.Substring(0, eqIdx).Trim();
            var value = part.Substring(eqIdx + 1).Trim();
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new Cookie
            {
                Name = name,
                Value = value,
                Domain = domain,
                Path = "/",
                // Url intentionally left null — Domain above satisfies Playwright's
                // "either url or domain" requirement and applies to all subdomains.
            });
        }
        return list;
    }

    private static void WriteDebug(ConcurrentDictionary<string, string> capturedResponses, string suffix, string? finalUrl = null, string? pageTitle = null)
    {
        try
        {
            // req-fix：统一使用 DebugFileManager 管理的 debug 目录（%AppData%\UsageMonitor\debug），
            // 消除与本类重复定义的 DebugDir，并修复此前“写入 ProjectRoot\logs\debug、却清理 AppData\debug”的目录错位。
            var debugDir = DebugFileManager.GetDebugDirectory();
            Directory.CreateDirectory(debugDir);
            DebugFileManager.CleanupOldDebugFiles();
            var fileName = $"MiniMax-DomExtract-{suffix}.json";
            var path = Path.Combine(debugDir, fileName);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"// time: {DateTime.Now:O}");
            sb.AppendLine($"// captured endpoint count: {capturedResponses.Count}");
            if (finalUrl != null) sb.AppendLine($"// final url: {finalUrl}");
            if (pageTitle != null) sb.AppendLine($"// page title: {pageTitle}");
            sb.AppendLine();
            foreach (var kv in capturedResponses)
            {
                sb.AppendLine($"// {kv.Key}");
                sb.AppendLine(kv.Value.Length > 10000 ? kv.Value.Substring(0, 10000) + "..." : kv.Value);
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            // req-060：补日志记录
            UsageMonitor.Core.Services.FileLogger.Debug("MiniMaxDomExtractor", $"WriteDebug failed: {ex.Message}");
        }
    }

    private sealed class DomMetrics
    {
        public string? PageTitle { get; set; }
        public string? Interval5hAriaLabel { get; set; }
        public string? WeeklyAriaLabel { get; set; }
        public string? VideoAriaLabel { get; set; }
        public string? CreditHint { get; set; }
        public double IntervalUsedPercent { get; set; } = -1;
        public double IntervalTotalPercent { get; set; } = -1;
        public double WeeklyUsedPercent { get; set; } = -1;
        public double WeeklyTotalPercent { get; set; } = -1;
    }
}
