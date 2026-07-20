using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.MiniMax;

/// <summary>
/// MiniMax (MiniMax Open Platform) Token Plan usage query plugin.
/// Queries subscription package remaining usage via GET /v1/token_plan/remains endpoint.
/// Supports both CN (api.minimaxi.com) and Global (api.minimax.io) regions.
/// </summary>
/// <remarks>
/// Key semantic notes (from OpenClaw Issue #81156):
/// - current_*_usage_count means "remaining count" (not used); used = total - usage
/// - Response has no *_remaining_count / *_remaining_percent fields; must calculate manually
/// - model_name is like "MiniMax-M*" / "MiniMax-Text-01", not "general"
///
/// Auth notes: /v1/token_plan/remains does NOT accept Bearer Token (returns 1004 even with Authorization).
/// It only accepts Cookie Session auth. This plugin supports:
/// 1. User manually paste Cookie (fallback)
/// 2. Auto-extract from system Edge/Chrome Cookie database (if user already logged in)
/// </remarks>
public class MiniMaxProvider : HttpUsageProviderBase
{
    /// <summary>CN region API base URL (uses www. subdomain which SPA actually uses)</summary>
    private const string DefaultCnBaseUrl = "https://www.minimaxi.com";

    /// <summary>Global region API base URL</summary>
    private const string DefaultGlobalBaseUrl = "https://www.minimax.io";

    /// <summary>Query path - actual endpoint used by MiniMax web console (Cookie-auth, returns percent)</summary>
    private const string UsagePath = "/backend/account/token_plan/remains_percent";

    /// <summary>MiniMax domain list (for browser Cookie extraction)</summary>
    private static readonly string[] MiniMaxDomains = { "minimaxi.com", "api.minimaxi.com", "api.minimax.io" };

    /// <summary>req-060：JSON 反序列化配置复用。</summary>
    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Debug log directory (raw API/DOM dumps) - same dir as FileLogger.LogDir/debug</summary>
    private static readonly string DebugDir = Path.Combine(
        UsageMonitor.Core.Services.FileLogger.LogDir, "debug");

    /// <summary>HTTP client (thread-safe, global singleton)</summary>
    private static readonly HttpClient _httpClient = new()
    {
        // 30s: MiniMax API server is overseas, network latency can be high
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "MiniMax";

    /// <inheritdoc />
    public override string DisplayName => "MiniMax";

    /// <inheritdoc />
    public override string? IconPath => null;

    /// <inheritdoc />
    public override string Version => "1.4.0";

    /// <summary>Source name used in FileLogger</summary>
    private const string LogSource = "MiniMaxProvider";

    /// <inheritdoc />
    public override string Author => "UsageMonitor";

    /// <inheritdoc />
    public override string Description => "Query MiniMax Token Plan usage (5h / weekly windows)";

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        new ConfigField("ApiKey", I18n.T("plugin.MiniMax.field.ApiKey.name"), ConfigFieldType.Password, false,
            placeholder: I18n.T("plugin.MiniMax.field.ApiKey.placeholder")),
        new ConfigField("Cookie", I18n.T("plugin.MiniMax.field.Cookie.name"), ConfigFieldType.Password, false,
            placeholder: I18n.T("plugin.MiniMax.field.Cookie.placeholder")),
        new ConfigField("Region", I18n.T("plugin.MiniMax.field.Region.name"), ConfigFieldType.Select, false,
            defaultValue: "CN",
            options: new[] { "CN", "Global" }),
        // 卡片显示控制 - 默认全开
        new ConfigField("Show5hBar", I18n.T("plugin.MiniMax.field.Show5hBar.name"), ConfigFieldType.Boolean, false,
            defaultValue: "true"),
        new ConfigField("ShowWeeklyBar", I18n.T("plugin.MiniMax.field.ShowWeeklyBar.name"), ConfigFieldType.Boolean, false,
            defaultValue: "true"),
        new ConfigField("ShowVideo5hBar", I18n.T("plugin.MiniMax.field.ShowVideo5hBar.name"), ConfigFieldType.Boolean, false,
            defaultValue: "true"),
        new ConfigField("ShowVideoWeeklyBar", I18n.T("plugin.MiniMax.field.ShowVideoWeeklyBar.name"), ConfigFieldType.Boolean, false,
            defaultValue: "true")
    };

    /// <summary>
    /// MiniMax 卡片在数据未到位时即应展示的渲染能力集合。
    /// 与 <see cref="MiniMaxDomExtractor.BuildUsageInfo"/> 末段写入的 mm_render_kinds 保持一致，
    /// 让首屏渲染时 5h/周/视频赠送/订阅胶囊等区块就先可见，数据刷新后再由运行时实际值覆盖。
    /// </summary>
    public override IReadOnlyList<string> DefaultRenderKinds => new[]
    {
        "subscriptionTitle", "primaryBar", "weeklyBar", "videoProgress",
        "summary", "ranking", "credits"
    };

    /// <summary>
    /// MiniMax 卡片支持的图表类型：折线图（每日 Token 用量趋势）+ 热力图（每日 Token 用量日历）。
    /// <para>
    /// 两者数据均来自 usage_summary 接口：折线图取 daily_token_usage（按日期升序的每日 Token 数），
    /// 热力图取 date_model_usage（带 date 的每日 total_token）。覆盖接口默认的通用三件套，
    /// 让 MiniMax 只暴露有真实数据支撑的两种图表，供配置窗口生成对应复选框。
    /// </para>
    /// </summary>
    public override IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line,
        CardChartKind.HeatMap
    };

    /// <summary>
    /// req-008：MiniMax 插件为余额快照提供的额外数据项。
    /// <para>
    /// 返回空集合让主窗口 VM 走默认 4 项（累计 / 峰值 / 活跃 / 积分余额），这几个项的 Value / Detail
    /// 已在 <c>UpdateFromMiniMaxDom</c> 中由 <c>mm_totalTokens / mm_mostActiveDay / mm_activeDays /
    /// mm_remainingCredits</c> 等 mm_* 字段填充。返回空集合可以避免与默认 4 项重复拼接。
    /// </para>
    /// </summary>
    public override IReadOnlyList<UsageMonitor.Core.Models.BalanceItem> BalanceItems => System.Array.Empty<UsageMonitor.Core.Models.BalanceItem>();

    // ============== REQ-083 SDK v2 新增可选属性 ==============

    /// <summary>
    /// MiniMax 为"度量进度条组"提供的 V2 数据（REQ-083）。
    /// <para>
    /// 当前轮为占位实现（返回 null），主窗口 DataTemplateSelector 会回退到旧 CardLimitBarsTemplate。
    /// 后续 sprint 可根据 MiniMaxDomExtractor 提取的 mm_5hUsedPercent / mm_weeklyUsedPercent /
    /// mm_videoInterval* 等 mm_* 字段构造 MetricBarData。
    /// </para>
    /// </summary>
    public override UsageMonitor.Core.Models.MetricBarData? CardMetricBarData => null;

    /// <summary>
    /// MiniMax 为"度量数字网格"（余额快照）提供的 V2 数据（REQ-083）。
    /// <para>
    /// 当前轮为占位实现（返回 null），主窗口 DataTemplateSelector 会回退到旧 CardBalanceTemplate。
    /// 后续 sprint 可根据 mm_totalTokens / mm_mostActiveDay / mm_activeDays / mm_remainingCredits
    /// 等字段构造 MetricGridData。
    /// </para>
    /// </summary>
    public override UsageMonitor.Core.Models.MetricGridData? CardMetricGridData => null;

    /// <summary>
    /// MiniMax 为折线图 hover tooltip 提供的 V2 TooltipContent 生成委托（REQ-083）。
    /// <para>
    /// 当前轮为占位实现（返回 null），主窗口沿用旧 ExtraTooltipLines 拼接逻辑。
    /// 后续 sprint 可返回构造 TooltipContent 的 lambda，按索引拼装 TooltipTextBlock +
    /// TooltipColorRow（缓存命中行）。
    /// </para>
    /// </summary>
    public override System.Func<int, UsageMonitor.Core.Models.TooltipContent>? LineTooltipProvider => null;

    /// <summary>
    /// req-026：MiniMax 环形图中心支持的数字类型。
    /// <para>
    /// MiniMax 同时提供「已用百分比」<c>Percent</c> 与「积分余额」<c>Credits</c> 两种数字
    /// （<c>Credits</c> 由 <c>usage_summary</c> 返回的剩余 Credits 折算为 K 数）。
    /// 覆盖接口默认仅提供 <c>["Percent"]</c> 的行为，以便设置窗口 Tab "环形图中心"
    /// 同时生成两个 CheckBox，用户可独立启用/关闭。
    /// </para>
    /// </summary>
    public override IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent", "Credits" };

    // req-066 A8：MiniMax 热力图色阶默认值，替代原 AppSettings 中硬编码的 6 档配置。
    // 启动时由 App.OnStartup 从 IUsageProvider.HeatMapTiers 装配到 ProviderHeatMapTiers。
    /// <inheritdoc />
    public override IReadOnlyList<UsageMonitor.Core.Models.HeatMapTierConfig>? HeatMapTiers => new List<UsageMonitor.Core.Models.HeatMapTierConfig>
    {
        new() { MinTokens = 0,            ColorHex = "#f3f4f6" },
        new() { MinTokens = 1,            ColorHex = "#ffe7e2" },
        new() { MinTokens = 20_000_000,   ColorHex = "#ffc6bb" },
        new() { MinTokens = 100_000_000,  ColorHex = "#ffa595" },
        new() { MinTokens = 200_000_000,  ColorHex = "#ff7b64" },
        new() { MinTokens = 300_000_000,  ColorHex = "#ff5a3d" },
    };

    /// <summary>
    /// req-007：MiniMax 卡片在主窗口折线图右上角显示“近 7 天 / 近 30 天”周期切换按钮。
    /// <para>
    /// 完整 daily 数据（最多 168 天）由 <see cref="MiniMaxDomExtractor"/> 写入
    /// <c>Extra["mm_dailyTokenValues"]</c> / <c>Extra["mm_dailyTokenDates"]</c>，调用方拿到后会在 VM
    /// 端按 <see cref="App.Controls.ChartPeriods"/> 对应窗口重新切片，<see cref="SetPeriodAsync"/>
    /// 在这里仅记录用户选择的 period + 写一条 Info 日志供后期跟踪。
    /// </para>
    /// </summary>
    public override bool SupportsPeriodSwitch => true;

    /// <summary>
    /// req-007：MiniMax 折线图 hover tooltip 扩展文本行：调用量与缓存命中率。
    /// <para>
    /// 值根据当前 Extra 中的当日调用汇总与缓存命中率动态拼接；如果 Extra 未提供这些指标则返回
    /// 原始的「调用 {value:0.##}」文本，避免 tooltip 出现空行。
    /// </para>
    /// </summary>
    public override IReadOnlyList<string> ExtraTooltipLines
    {
        get
        {
            // req-034 修复：不再返回静态标签，数值由 ViewModel 动态生成
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// req-007：处理主窗口折线图周期切换。
    /// <para>
    /// 记录用户选中的 <paramref name="period"/> 以供后续日志/调试使用；这里不重复拉接口——
    /// usage_summary 一次返回足够多的历史点（≤ 168 天），VM 端在已缓存的完整数据上按窗口切片即可。
    /// 返回 <see cref="Task.CompletedTask"/> 以保证接口契约同步。
    /// </para>
    /// </summary>
    public override Task SetPeriodAsync(string period, CancellationToken ct = default)
    {
        UsageMonitor.Core.Services.FileLogger.Info(LogSource,
            $"SetPeriodAsync invoked: period={period}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Query MiniMax Token Plan usage.
    /// </summary>
    public override async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var baseUrl = ResolveBaseUrl(config);
        var apiKey = config.GetValue("ApiKey")?.Trim();
        var cookie = config.GetValue("Cookie")?.Trim();
        var userAgent = config.GetValue("_userAgent");

        // 自愈：若 config.json 未持有 Cookie（例如配置曾被写坏而重置），回退读取
        // %AppData%\UsageMonitor\cookies\MiniMax.json 中已保存的登录态并回填内存配置，避免用户重复登录。
        if (string.IsNullOrWhiteSpace(cookie))
        {
            try
            {
                var saved = UsageMonitor.Core.Services.BrowserLoginService.LoadCookieData(ProviderId);
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Cookie))
                {
                    cookie = saved.Cookie.Trim();
                    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = saved.UserAgent;
                    config.SetValue("Cookie", cookie);
                    if (!string.IsNullOrWhiteSpace(saved.UserAgent))
                        config.SetValue("_userAgent", saved.UserAgent);
                    UsageMonitor.Core.Services.FileLogger.Info(LogSource,
                        $"Cookie recovered from cookies/{ProviderId}.json (len={cookie.Length}); backfilled into config.");
                }
            }
            catch (Exception ex)
            {
                UsageMonitor.Core.Services.FileLogger.Warn(LogSource,
                    $"Cookie fallback read failed: {ex.Message}");
            }
        }

        // Need at least one auth method
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(cookie))
        {
            return CreateError(
                "Please login via the 'Get login state' button in settings (Cookie is the primary auth), or optionally set a Token Plan subscription key.");
        }

        try
        {
            // PRIMARY: extract usage via Playwright DOM (Cookie session).
            // Per project decision 2026-07-13: DOM is main source; API is fallback.
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                UsageMonitor.Core.Services.FileLogger.Info(LogSource,
                    "GetUsageAsync start. Trying DOM extraction (primary)...");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // req-062 B1: Pass region to DOM extractor for dynamic cookie domain and URL selection
                var region = config.GetValue("Region") ?? "CN";
                var domUsage = await MiniMaxDomExtractor.ExtractAsync(
                    cookie,
                    userAgent ?? string.Empty,
                    region);
                sw.Stop();
                if (domUsage != null && domUsage.IsSuccess)
                {
                    UsageMonitor.Core.Services.FileLogger.Info(LogSource,
                        $"DOM extraction OK in {sw.ElapsedMilliseconds}ms. primary={domUsage.UsedAmount}%");
                    return domUsage;
                }
                UsageMonitor.Core.Services.FileLogger.Warn(LogSource,
                    $"DOM extraction returned null/failed in {sw.ElapsedMilliseconds}ms. Cookie expired; skipping auto re-login (use Get login state button or it will be auto-launched from UI thread).");
                // Note: BrowserLoginService requires UI dispatcher (Playwright launches Edge).
                // Auto-triggering from a non-UI refresh thread is unsafe; the user will see
                // the failure and click "Get MiniMax login state" from the settings dialog
                // (or call us through a tray-context action that we wire up in MainWindow).
                // For now we just fall back to API.
            }
            else
            {
                UsageMonitor.Core.Services.FileLogger.Warn(LogSource,
                    "No cookie configured; falling back to API path");
            }

            // FALLBACK: API call.
            var usage = await QueryRemainsAsync(baseUrl, apiKey, cookie);
            return usage;
        }
        catch (HttpRequestException ex)
        {
            return CreateError($"Network request failed: {ex.Message}");
        }
        // req-065 B7：取消与超时分类，用户主动取消显示"用户取消"，网络超时显示"请求超时"
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CreateError("用户取消");
        }
        catch (TaskCanceledException)
        {
            return CreateError(
                $"请求超时（30s）。MiniMax API 服务器可能响应缓慢或不可达，请稍后重试。");
        }
        catch (JsonException ex)
        {
            return CreateError($"JSON parse failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return CreateError(ex.Message);
        }
    }

    /// <summary>
    /// Resolve base URL from Region and BaseUrl config.
    /// </summary>
    private static string ResolveBaseUrl(ProviderConfig config)
    {
        var baseUrl = config.GetValue("BaseUrl");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return NormalizeBaseUrl(baseUrl);
        }

        var region = config.GetValue("Region");
        return string.Equals(region, "Global", StringComparison.OrdinalIgnoreCase)
            ? DefaultGlobalBaseUrl
            : DefaultCnBaseUrl;
    }

    /// <summary>
    /// Normalize BaseUrl: auto-prepend https://, strip trailing slash, validate as legal absolute URI.
    /// Accepts formats like "api.minimaxi.com" / "https://api.minimaxi.com/" / "http://example.com".
    /// </summary>
    private static string NormalizeBaseUrl(string raw)
    {
        var trimmed = raw.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return DefaultCnBaseUrl;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Call MiniMax /v1/token_plan/remains endpoint.
    /// Uses Authorization: Bearer auth (Token Plan subscription key).
    /// Optional Cookie header (fallback auth).
    /// </summary>
    private async Task<UsageInfo> QueryRemainsAsync(string baseUrl, string? apiKey, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}{UsagePath}");
        // Prefer Bearer auth (Token Plan key). Fall back to Cookie if no API key.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        else if (!string.IsNullOrWhiteSpace(cookie))
        {
            // Cookie session auth - new endpoint requires x-group-id header from cookie
            // req-065 B6：Cookie header injection防护，清理控制字符
            var safeCookie = UsageMonitor.Core.Services.CookieHeaderSanitizer.Sanitize(cookie);
            if (!request.Headers.TryAddWithoutValidation("Cookie", safeCookie))
            {
                UsageMonitor.Core.Services.FileLogger.Warn("MiniMaxProvider", "Cookie header rejected after sanitization");
            }
            // Extract minimax_group_id_v2 from cookie string for x-group-id header
            var groupId = ExtractCookieValue(cookie, "minimax_group_id_v2");
            if (!string.IsNullOrEmpty(groupId))
            {
                request.Headers.Add("x-group-id", groupId);
            }
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) UsageMonitor/{Version}");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return CreateError(
                $"API error ({(int)response.StatusCode}): {SanitizeErrorBody(errorBody)}");
        }

        var json = await response.Content.ReadAsStringAsync();
        WriteDebugResponse(baseUrl, json);

        var remainsResponse = JsonSerializer.Deserialize<MiniMaxRemainsResponse>(json, s_jsonOptions);

        if (remainsResponse == null)
        {
            return CreateError("Failed to parse API response");
        }

        if (remainsResponse.BaseResp != null && remainsResponse.BaseResp.StatusCode != 0)
        {
            var msg = remainsResponse.BaseResp.StatusMsg ?? "Unknown error";
            var hint = MapErrorCodeToHint(remainsResponse.BaseResp.StatusCode);
            return CreateError(
                $"API business error (code={remainsResponse.BaseResp.StatusCode}): {msg}\n\nTroubleshooting: {hint}");
        }

        var modelRemains = remainsResponse.ModelRemains;
        if (modelRemains == null || modelRemains.Count == 0)
        {
            return CreateError(
                "Response has no model_remains data. Cookie may be expired, please re-login.");
        }

        // Prefer "general" model (text usage). Fall back to first entry.
        var selected = modelRemains.FirstOrDefault(m => m.ModelName == "general") ?? modelRemains[0];
        return BuildUsageInfo(selected, modelRemains);
    }

    /// <summary>
    /// Extract a single cookie value from a cookie string (e.g. "k1=v1; k2=v2" -> "v1" for "k1").
    /// Used to extract minimax_group_id_v2 for x-group-id header.
    /// </summary>
    private static string? ExtractCookieValue(string cookieString, string name)
    {
        foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = part.Substring(0, eqIdx).Trim();
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring(eqIdx + 1).Trim();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Call MiniMax /backend/account/token_plan/usage_summary endpoint.
    /// Returns historical usage: daily_token_usage (168 days, simple line chart data),
    /// date_model_usage (per-day x per-model breakdown, heatmap data), summary stats.
    /// Requires Cookie auth (cannot use API Key).
    /// Returns null if cookie lacks minimax_group_id_v2 or call fails.
    /// </summary>
    private async Task<MiniMaxUsageSummaryResponse?> QueryUsageSummaryAsync(string baseUrl, string cookie)
    {
        var path = "/backend/account/token_plan/usage_summary";
        var groupId = ExtractCookieValue(cookie, "minimax_group_id_v2");
        if (string.IsNullOrEmpty(groupId))
        {
            // Without group_id we cannot auth - silently skip
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}{path}");
        // req-065 B6：Cookie header injection防护，清理控制字符
        var safeCookie = UsageMonitor.Core.Services.CookieHeaderSanitizer.Sanitize(cookie);
        if (!request.Headers.TryAddWithoutValidation("Cookie", safeCookie))
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MiniMaxProvider", "Cookie header rejected after sanitization in QueryUsageSummaryAsync");
        }
        request.Headers.Add("x-group-id", groupId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) UsageMonitor/{Version}");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"usage_summary API returned {(int)response.StatusCode}: {SanitizeErrorBody(await response.Content.ReadAsStringAsync(), 100)}");
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<MiniMaxUsageSummaryResponse>(json, s_jsonOptions);
    }

    /// <summary>
    /// Build UsageInfo from MiniMax API response.
    /// New endpoint returns percent strings (e.g. "66%") and counts (e.g. -1 for unlimited).
    /// We show percent primarily, falling back to count-based calc when percent is missing.
    /// </summary>
    private static UsageInfo BuildUsageInfo(MiniMaxModelRemains selected, IReadOnlyList<MiniMaxModelRemains> all)
    {
        var usageInfo = new UsageInfo
        {
            ProviderId = ProviderIdStatic,
            ProviderName = "MiniMax",
            IsSuccess = true,
            // req-067 B21：统一使用 UTC 时间存储，避免时区问题
            LastUpdated = DateTime.UtcNow
        };

        // Try to parse percent strings. The new API returns -1 for unlimited totals,
        // so we cannot compute percent from counts. Prefer the percent string directly.
        double intervalUsedPercent = ParsePercent(selected.CurrentIntervalUsedPercent);
        double intervalTotalPercent = ParsePercent(selected.CurrentIntervalTotalPercent);
        double weeklyUsedPercent = ParsePercent(selected.CurrentWeeklyUsedPercent);
        double weeklyTotalPercent = ParsePercent(selected.CurrentWeeklyTotalPercent);

        long intervalTotal = selected.CurrentIntervalTotalCount > 0 ? selected.CurrentIntervalTotalCount : 0;
        long intervalUsed = selected.CurrentIntervalTotalCount > 0
            ? Math.Max(0, selected.CurrentIntervalTotalCount - selected.CurrentIntervalUsageCount) : 0;
        long weeklyTotal = selected.CurrentWeeklyTotalCount > 0 ? selected.CurrentWeeklyTotalCount : 0;
        long weeklyUsed = selected.CurrentWeeklyTotalCount > 0
            ? Math.Max(0, selected.CurrentWeeklyTotalCount - selected.CurrentWeeklyUsageCount) : 0;

        // Pick a primary window for the legacy TotalAmount/UsedAmount fields.
        if (intervalTotal > 0)
        {
            usageInfo.TotalAmount = intervalTotal;
            usageInfo.UsedAmount = intervalUsed;
            usageInfo.Unit = "次";
        }
        else if (weeklyTotal > 0)
        {
            usageInfo.TotalAmount = weeklyTotal;
            usageInfo.UsedAmount = weeklyUsed;
            usageInfo.Unit = "次";
        }
        else
        {
            // Unlimited (total == -1). Show percent instead.
            usageInfo.Unit = "%";
        }

        var extras = new Dictionary<string, object>
        {
            ["modelName"] = selected.ModelName ?? "unknown",
            // Primary displayed metric: used percent of 5h window
            ["intervalUsedPercent"] = intervalUsedPercent,
            ["intervalTotalPercent"] = intervalTotalPercent,
            ["intervalUsedAsRemaining"] = selected.CurrentIntervalUsageCount,
            ["intervalTotalCount"] = selected.CurrentIntervalTotalCount,
            ["intervalRemainsTimeMs"] = selected.RemainsTime,
            // Weekly
            ["weeklyUsedPercent"] = weeklyUsedPercent,
            ["weeklyTotalPercent"] = weeklyTotalPercent,
            ["weeklyUsedAsRemaining"] = selected.CurrentWeeklyUsageCount,
            ["weeklyTotalCount"] = selected.CurrentWeeklyTotalCount,
            ["weeklyRemainsTimeMs"] = selected.WeeklyRemainsTime,
            ["cookieAuth"] = true
        };

        if (selected.StartTime > 0)
            extras["intervalStartTime"] = DateTimeOffset.FromUnixTimeMilliseconds(selected.StartTime).LocalDateTime;
        if (selected.EndTime > 0)
            extras["intervalEndTime"] = DateTimeOffset.FromUnixTimeMilliseconds(selected.EndTime).LocalDateTime;
        if (selected.WeeklyStartTime > 0)
            extras["weeklyStartTime"] = DateTimeOffset.FromUnixTimeMilliseconds(selected.WeeklyStartTime).LocalDateTime;
        if (selected.WeeklyEndTime > 0)
            extras["weeklyEndTime"] = DateTimeOffset.FromUnixTimeMilliseconds(selected.WeeklyEndTime).LocalDateTime;

        extras["allModelSummary"] = all
            .Select(m => $"{m.ModelName}: interval={m.CurrentIntervalUsedPercent ?? "N/A"}, weekly={m.CurrentWeeklyUsedPercent ?? "N/A"}")
            .ToArray();

        usageInfo.Extra = extras;
        return usageInfo;
    }

    /// <summary>
    /// Parse percent string like "66%" to 66.0. Returns -1 if invalid.
    /// </summary>
    private static double ParsePercent(string? percent)
    {
        if (string.IsNullOrWhiteSpace(percent)) return -1;
        var trimmed = percent.Trim().TrimEnd('%');
        return double.TryParse(trimmed, out var v) ? v : -1;
    }

    /// <summary>ProviderId static accessor (used by internal BuildUsageInfo)</summary>
    private static string ProviderIdStatic => "MiniMax";

    /// <inheritdoc />
    public override UsageMonitor.Core.Models.BrowserLoginConfig LoginConfig { get; } = new()
    {
        ProviderId = "MiniMax",
        LoginUrl = "https://platform.minimaxi.com",
        CookieDomainFilters = new[] { "minimaxi.com", "api.minimaxi.com", "api.minimax.io" },
        // 2 minutes default: covers typical QR-code login within ~30s after phone confirm,
        // plus a safety margin. Long enough to not give up early, short enough to avoid
        // idle waits when the user is not at the keyboard.
        LoginTimeout = TimeSpan.FromMinutes(2),
        UiButtonText = "Get MiniMax login state",
        ValidateUrl = "https://platform.minimaxi.com/console/usage",
        // Strict login detection (avoid landing page misdetection):
        // - Landing page (https://platform.minimaxi.com/) has path = "/" - treated as login page
        // - After login, URL changes to /console/plan or /user-center/* - treated as logged in
        RequiredCookieDomain = "minimaxi.com",
        // Real auth tokens set by unified-login flow:
        //   _token                - JWT session token (exp ~10h)
        //   minimax_group_id_v2   - team ID, also used as x-group-id for /backend/account/* APIs
        // We require _token; the other two are nice-to-have.
        RequiredCookieNames = new[] { "_token" },
        LoginUrlKeywords = new[] { "login", "unified-login", "signin", "sign-in", "signup", "register" },
        LoginSuccessHost = "platform.minimaxi.com",
        // After login, URL path must contain one of these (登录后才有的页面，避免文档站等公开页误判)
        LoggedInPathKeywords = new[] { "/console/", "/user-center/", "/plan", "/usage" },
    };

    /// <inheritdoc />
    public override async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var result = await GetUsageAsync(config, ct);
        return result.IsSuccess;
    }

    /// <summary>
    /// Map MiniMax API error codes to user-friendly self-diagnosis hints.
    /// Reference: https://platform.minimaxi.com/docs/api-reference/errorcode
    /// </summary>
    private static string MapErrorCodeToHint(int code) => code switch
    {
        1002 => "Request rate limit exceeded. Please try again later, or lower plugin refresh frequency (Settings > Refresh interval).",
        1004 => "API Key auth failed. Possible reasons: 1) Key is NOT a Token Plan subscription key (must be from MiniMax subscription page, not regular pay-as-you-go Key); 2) Key revoked/expired; 3) Key has extra spaces; 4) Subscription expired.",
        1008 => "Insufficient account balance. Please top up on MiniMax Open Platform.",
        2013 => "Request parameter error. Please check if BaseUrl in config is complete (e.g. https://api.minimaxi.com).",
        2049 => "API Key invalid (currently not using Key auth, should be Cookie credential expired).",
        2056 => "Token Plan resource limit exceeded. Please wait for next 5h or weekly window reset, or consider upgrading plan.",
        1024 => "Server internal error. Please retry later.",
        _ => "See MiniMax error code documentation (https://platform.minimaxi.com/docs/api-reference/errorcode) for details; or check raw response JSON in %AppData%/UsageMonitor/debug/."
    };

    /// <summary>
    /// Write each call's raw response JSON to %AppData%/UsageMonitor/debug/ for field-mapping debugging.
    /// Silently fail on errors (does not affect main flow).
    /// </summary>
    private static void WriteDebugResponse(string baseUrl, string json)
    {
        try
        {
            Directory.CreateDirectory(DebugDir);
            DebugFileManager.CleanupOldDebugFiles();
            var fileName = $"MiniMax-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
            var path = Path.Combine(DebugDir, fileName);
            var content = $"// baseUrl: {baseUrl}\n// time: {DateTime.Now:O}\n\n{json}";
            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            // req-060：补日志记录，便于排查调试文件写入失败
            UsageMonitor.Core.Services.FileLogger.Debug(LogSource, $"WriteDebugResponse failed: {ex.Message}");
        }
    }
}

/// <summary>
/// MiniMax /v1/token_plan/remains API response model.
/// </summary>
internal class MiniMaxRemainsResponse
{
    [JsonPropertyName("base_resp")]
    public MiniMaxBaseResp? BaseResp { get; set; }

    [JsonPropertyName("model_remains")]
    public List<MiniMaxModelRemains>? ModelRemains { get; set; }
}

/// <summary>
/// MiniMax business response status.
/// </summary>
internal class MiniMaxBaseResp
{
    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("status_msg")]
    public string? StatusMsg { get; set; }
}

/// <summary>
/// MiniMax single model usage details.
/// Important: *_usage_count means "remaining count"; used = total - usage.
/// New fields for /backend/account/token_plan/remains_percent endpoint (Cookie-auth).
/// </summary>
internal class MiniMaxModelRemains
{
    [JsonPropertyName("model_name")]
    public string? ModelName { get; set; }

    // 5h window
    [JsonPropertyName("current_interval_total_count")]
    public long CurrentIntervalTotalCount { get; set; }

    [JsonPropertyName("current_interval_usage_count")]
    public long CurrentIntervalUsageCount { get; set; }

    [JsonPropertyName("remains_time")]
    public long RemainsTime { get; set; }

    /// <summary>5h 总额度百分比 (e.g. "100%")</summary>
    [JsonPropertyName("current_interval_total_percent")]
    public string? CurrentIntervalTotalPercent { get; set; }

    /// <summary>5h 已用百分比 (e.g. "66%")</summary>
    [JsonPropertyName("current_interval_used_percent")]
    public string? CurrentIntervalUsedPercent { get; set; }

    // Weekly window
    [JsonPropertyName("current_weekly_total_count")]
    public long CurrentWeeklyTotalCount { get; set; }

    [JsonPropertyName("current_weekly_usage_count")]
    public long CurrentWeeklyUsageCount { get; set; }

    [JsonPropertyName("weekly_start_time")]
    public long WeeklyStartTime { get; set; }

    [JsonPropertyName("weekly_end_time")]
    public long WeeklyEndTime { get; set; }

    [JsonPropertyName("weekly_remains_time")]
    public long WeeklyRemainsTime { get; set; }

    /// <summary>周总额度百分比 (e.g. "100%")</summary>
    [JsonPropertyName("current_weekly_total_percent")]
    public string? CurrentWeeklyTotalPercent { get; set; }

    /// <summary>周已用百分比 (e.g. "94%")</summary>
    [JsonPropertyName("current_weekly_used_percent")]
    public string? CurrentWeeklyUsedPercent { get; set; }

    [JsonPropertyName("start_time")]
    public long StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public long EndTime { get; set; }
}

/// <summary>
/// MiniMax /backend/account/token_plan/usage_summary API response model.
/// Returns 168-day daily + per-model breakdown for line chart / heatmap.
/// </summary>
internal class MiniMaxUsageSummaryResponse
{
    [JsonPropertyName("base_resp")]
    public MiniMaxBaseResp? BaseResp { get; set; }

    [JsonPropertyName("total_days")]
    public int TotalDays { get; set; }

    [JsonPropertyName("total_token_consumed")]
    public string? TotalTokenConsumed { get; set; }

    [JsonPropertyName("usage_ranking_percent")]
    public double UsageRankingPercent { get; set; }

    [JsonPropertyName("active_days")]
    public int ActiveDays { get; set; }

    [JsonPropertyName("current_consecutive_days")]
    public int CurrentConsecutiveDays { get; set; }

    [JsonPropertyName("most_active_day")]
    public MiniMaxMostActiveDay? MostActiveDay { get; set; }

    /// <summary>Daily total token (ordered by date ascending). Length up to 168.</summary>
    [JsonPropertyName("daily_token_usage")]
    public List<long>? DailyTokenUsage { get; set; }

    /// <summary>Per-day per-model breakdown. Used as heatmap source.</summary>
    [JsonPropertyName("date_model_usage")]
    public List<MiniMaxDateModelUsage>? DateModelUsage { get; set; }
}

/// <summary>Most active day entry from usage_summary.</summary>
internal class MiniMaxMostActiveDay
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("token_count")]
    public long TokenCount { get; set; }

    [JsonPropertyName("image_count")]
    public long ImageCount { get; set; }

    [JsonPropertyName("video_count")]
    public long VideoCount { get; set; }

    [JsonPropertyName("music_count")]
    public long MusicCount { get; set; }
}

/// <summary>Per-date per-model usage breakdown (for heatmap rendering).</summary>
internal class MiniMaxDateModelUsage
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>Per-model token usage for this date.</summary>
    [JsonPropertyName("models")]
    public List<MiniMaxModelUsageEntry>? Models { get; set; }

    [JsonPropertyName("total_token")]
    public long TotalToken { get; set; }

    [JsonPropertyName("cache_hit_percent")]
    public string? CacheHitPercent { get; set; }
}

/// <summary>Single model usage within a date (heatmap cell).</summary>
internal class MiniMaxModelUsageEntry
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("total_token")]
    public long TotalToken { get; set; }

    [JsonPropertyName("input_token")]
    public long InputToken { get; set; }

    [JsonPropertyName("output_token")]
    public long OutputToken { get; set; }
}
