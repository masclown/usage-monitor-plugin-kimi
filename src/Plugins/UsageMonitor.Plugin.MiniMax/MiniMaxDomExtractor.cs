using System;
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

    /// <summary>Debug log directory (raw API/DOM dumps) - same dir as FileLogger.LogDir/debug</summary>
    private static readonly string DebugDir = Path.Combine(
        UsageMonitor.Core.Services.FileLogger.LogDir, "debug");

    /// <summary>
    /// Build a UsageInfo by reading the logged-in MiniMax usage page via Playwright.
    /// Returns null on failure (cookie invalid, page unreachable, browser error).
    /// </summary>
    /// <param name="cookie">Cookie string from %AppData%\UsageMonitor\cookies\MiniMax.json</param>
    /// <param name="userAgent">Browser User-Agent for replay</param>
    /// <param name="ct">Cancellation</param>
    public static async Task<UsageInfo?> ExtractAsync(
        string cookie, string userAgent, CancellationToken ct = default)
    {
        FileLogger.Info(LogSource, $"ExtractAsync started. cookieLen={cookie?.Length ?? 0}, uaLen={userAgent?.Length ?? 0}");
        if (string.IsNullOrWhiteSpace(cookie))
        {
            FileLogger.Warn(LogSource, "Cookie is empty, aborting");
            return null;
        }

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
                });
            FileLogger.Debug(LogSource, $"Edge launched in {stepSw.ElapsedMilliseconds}ms total");

            // Inject saved cookies. Playwright requires {Name, Value, Domain or Url} per entry.
            var cookieList = ParseCookieString(cookie);
            if (cookieList.Count == 0)
            {
                FileLogger.Warn(LogSource, "Empty cookie list parsed, skip");
                return null;
            }
            FileLogger.Info(LogSource, $"Parsed {cookieList.Count} cookies. Names: {string.Join(",", cookieList.Select(c => c.Name))}");
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
            // (the trend chart, heatmap, credit all come from here).
            var capturedResponses = new Dictionary<string, string>();
            page.Response += async (_, response) =>
            {
                try
                {
                    var url = response.Url;
                    if (response.Status == 200 &&
                        url.Contains("/backend/account/token_plan", StringComparison.OrdinalIgnoreCase))
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
            FileLogger.Info(LogSource, $"Navigating to https://platform.minimaxi.com/console/usage ...");
            var navSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await page.GotoAsync("https://platform.minimaxi.com/console/usage",
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = 30_000,
                    });
            }
            catch (Exception navEx)
            {
                FileLogger.Warn(LogSource, $"GotoAsync exception: {navEx.Message}");
            }
            FileLogger.Info(LogSource, $"Navigation completed in {navSw.ElapsedMilliseconds}ms (URL: {page.Url})");
            ct.ThrowIfCancellationRequested();

            // Give echarts a moment to render after NetworkIdle
            await page.WaitForTimeoutAsync(2000);
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
    /// Read percentages/counts/times from DOM aria-labels.
    /// Returns a Metrics record. Returned values may be null when missing.
    /// </summary>
    private static async Task<DomMetrics> ExtractDomMetricsAsync(IPage page)
    {
        // Run all DOM lookups in the browser using `evaluate`.
        var raw = await page.EvaluateAsync<string>(@"() => {
            const get = (label) => {
                const el = document.querySelector(`div[aria-label^=""${label}""]`);
                if (!el) return null;
                // First <span> inside the el is the percent number (e.g. ""66%"")
                const span = el.querySelector('span');
                return span ? span.textContent.trim() : el.textContent.trim();
            };
            const getAll = (label) => {
                const el = document.querySelector(`div[aria-label^=""${label}""]`);
                if (!el) return null;
                return el.innerText;
            };
            // For '视频赠送': it's `0 / 3` in the right column
            const getText = (label) => {
                const el = Array.from(document.querySelectorAll('div[aria-label]'))
                    .find(x => (x.getAttribute('aria-label') || '').startsWith(label));
                return el ? el.innerText.trim() : null;
            };
            return JSON.stringify({
                interval5h: get('5h 限额'),
                weekly:    get('周限额'),
                video:     getText('视频赠送'),
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
        var m = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%");
        if (m.Success && double.TryParse(m.Groups[1].Value, out var v)) return v;
        return -1;
    }

    /// <summary>Extract second percentage (total) like ""100%"" from ""已用 66% / 总额 100%"" pattern.</summary>
    private static double ExtractTotalPercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var matches = Regex.Matches(text, @"(\d+(?:\.\d+)?)\s*%");
        if (matches.Count >= 2 && double.TryParse(matches[1].Groups[1].Value, out var v)) return v;
        // fall back to first
        if (matches.Count >= 1 && double.TryParse(matches[0].Groups[1].Value, out var v2)) return v2;
        return -1;
    }

    /// <summary>
    /// Combine DOM metrics with captured JSON responses to build UsageInfo + extras dict.
    /// prefers JSON for trend data, DOM for primary numbers.
    /// </summary>
    private static void BuildUsageInfo(
        DomMetrics dom,
        Dictionary<string, string> captured,
        int cookieCount,
        out UsageInfo usageInfo,
        out Dictionary<string, object> extras)
    {
        usageInfo = new UsageInfo
        {
            ProviderId = "MiniMax",
            ProviderName = "MiniMax",
            IsSuccess = true,
            LastUpdated = DateTime.Now
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
                                extras["mm_videoIntervalUsed"] = GetLong(m, "current_interval_used_count");
                                extras["mm_videoIntervalTotal"] = GetLong(m, "current_interval_total_count");
                                extras["mm_videoWeeklyUsed"] = GetLong(m, "current_weekly_used_count");
                                extras["mm_videoWeeklyTotal"] = GetLong(m, "current_weekly_total_count");
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
        extras["intervalUsedPercent"] = interval5hUsed;
        extras["weeklyUsedPercent"] = weeklyUsed;

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
    /// using Domain (".minimaxi.com") lets Playwright match against multiple
    /// subdomains (www / platform / account) without per-cookie URL tuning.
    /// </para>
    /// <para>
    /// IMPORTANT: When Domain is set, Url must remain null — Playwright throws
    /// "Cookie should have either url or domain" if both have non-empty values
    /// that do not agree. We never set Url here.
    /// </para>
    /// </summary>
    private static List<Cookie> ParseCookieString(string cookieString)
    {
        var list = new List<Cookie>();
        if (string.IsNullOrWhiteSpace(cookieString)) return list;
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
                Domain = ".minimaxi.com",
                Path = "/",
                // Url intentionally left null — Domain above satisfies Playwright's
                // "either url or domain" requirement and applies to all subdomains.
            });
        }
        return list;
    }

    private static void WriteDebug(Dictionary<string, string> capturedResponses, string suffix, string? finalUrl = null, string? pageTitle = null)
    {
        try
        {
            Directory.CreateDirectory(DebugDir);
            var fileName = $"MiniMax-DomExtract-{suffix}.json";
            var path = Path.Combine(DebugDir, fileName);
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
        catch { /* ignore */ }
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
