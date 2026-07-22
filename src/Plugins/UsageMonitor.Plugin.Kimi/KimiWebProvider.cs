using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Kimi;

/// <summary>
/// req-085：Kimi 网页模式用量查询插件。
/// 通过 kimi.com 内部 API 获取订阅信息和用量数据。
/// 继承 <see cref="WebPluginBase"/> 复用浏览器生命周期管理。
/// </summary>
public class KimiWebProvider : WebPluginBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>req-060-004：反序列化选项提为 static readonly，避免每次 API 调用重建。</summary>
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>req-067-002：百分比正则提为 static readonly + Compiled，避免每次 DOM 兜底解析重新编译。</summary>
    private static readonly System.Text.RegularExpressions.Regex _usagePercentRegex =
        new(@"(\d+\.?\d*)%", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "kimi_web";

    /// <inheritdoc />
    public override string DisplayName => "Kimi (网页模式)";

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override string Author => "UsageMonitor";

    /// <inheritdoc />
    public override string Description => "通过 Kimi 平台网页获取订阅信息和用量数据（支持图表展示）";

    /// <inheritdoc />
    protected override string LoginUrl => "https://www.kimi.com/";

    /// <inheritdoc />
    protected override string UsageUrl => "https://www.kimi.com/code/console";

    /// <inheritdoc />
    protected override string[] CookieDomainFilters => new[] { ".kimi.com", "www.kimi.com" };

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        StandardWebConfigFields.Cookie(ProviderId),
        StandardWebConfigFields.Region(ProviderId, "CN", "CN", "Global"),
        StandardWebConfigFields.AutoRefresh(ProviderId, true),
        StandardWebConfigFields.Headless(ProviderId, false)
    };

    /// <inheritdoc />
    public override IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring
    };

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent", "Usage" };
    
        /// <summary>
        /// req-098：Kimi 网页模式任务栏迷你图声明：半圆环 + 文字（基础两件套）。
        /// </summary>
        public override IReadOnlyList<UsageMonitor.Core.Plugins.MiniChart.MiniChartKind> SupportedMiniCharts => new[]
        {
            UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniRingChart,
            UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniText
        };
    
        /// <summary>
        /// req-098：Kimi 网页模式任务栏迷你图内容：主指标（已用百分比）+ Credits（余额）。
        /// </summary>
        public override IReadOnlyList<UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind> MiniChartDataTypes => new[]
        {
            UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.PrimaryMetric,
            UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.Credits
        };

    /// <summary>req-099/bug5：Kimi 卡片 V2 进度条——5h/7天限额 + 额度已用（ratio 兼容 0-1 与 0-100）。</summary>
    protected override MetricBarData? BuildCardMetricBarData(UsageInfo usage)
    {
        var bars = new System.Collections.Generic.List<MetricBarItem>();
        var r5 = ReadExtraDouble(usage, "ratelimit_5h_ratio", -1);
        if (r5 >= 0)
        {
            var reset5 = ReadExtraString(usage, "ratelimit_5h_reset");
            bars.Add(new MetricBarItem("5h 限额", System.Math.Min(100, r5 <= 1.0 ? r5 * 100 : r5),
                RightText: string.IsNullOrEmpty(reset5) ? null : reset5));
        }
        var r7 = ReadExtraDouble(usage, "ratelimit_7d_ratio", -1);
        if (r7 >= 0)
        {
            var reset7 = ReadExtraString(usage, "ratelimit_7d_reset");
            bars.Add(new MetricBarItem("7 天限额", System.Math.Min(100, r7 <= 1.0 ? r7 * 100 : r7),
                RightText: string.IsNullOrEmpty(reset7) ? null : reset7));
        }
        var amt = ReadExtraDouble(usage, "amount_used_ratio", -1);
        if (amt >= 0) bars.Add(new MetricBarItem("额度已用", System.Math.Min(100, amt <= 1.0 ? amt * 100 : amt)));
        return bars.Count > 0 ? new MetricBarData(bars) : null;
    }

    /// <summary>req-099/bug5：Kimi 卡片 V2 数字网格——订阅档位 + Research 用量。</summary>
    protected override MetricGridData? BuildCardMetricGridData(UsageInfo usage)
    {
        var items = new System.Collections.Generic.List<MetricGridItem>();
        var sub = ReadExtraString(usage, "subscription_title");
        if (!string.IsNullOrEmpty(sub)) items.Add(new MetricGridItem("订阅", sub));
        var rt = ReadExtraLong(usage, "research_total", -1);
        if (rt >= 0)
        {
            var ru = ReadExtraLong(usage, "research_used", 0);
            items.Add(new MetricGridItem("Research", $"{ru}/{rt}"));
        }
        return items.Count > 0 ? new MetricGridData(items) : null;
    }

    /// <summary>
    /// 解析用量页面，提取订阅信息和用量数据。
    /// 优先尝试调用内部 API，失败时回退到 DOM 解析。
    /// </summary>
    /// <param name="page">已导航到用量页面的 IPage 实例</param>
    /// <returns>解析后的 UsageInfo</returns>
    protected override async Task<UsageInfo> ParseUsagePageAsync(IPage page)
    {
        var usageInfo = new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            LastUpdated = DateTime.UtcNow
        };

        try
        {
            // 尝试从 localStorage 获取 access_token 调用内部 API
            var token = await GetAccessTokenAsync(page);
            if (!string.IsNullOrEmpty(token))
            {
                LogInfo("获取到 access_token，尝试调用内部 API");
                var apiResult = await FetchDataFromInternalApiAsync(token);
                if (apiResult != null)
                {
                    MergeApiResult(usageInfo, apiResult);
                    return usageInfo;
                }
            }

            // 回退到 DOM 解析
            LogInfo("内部 API 调用失败，回退到 DOM 解析");
            await ParseDomAsync(page, usageInfo);
        }
        catch (Exception ex)
        {
            LogError("ParseUsagePageAsync 异常", ex);
            usageInfo.IsSuccess = false;
            usageInfo.ErrorMessage = ex.Message;
        }

        return usageInfo;
    }

    /// <summary>
    /// 从页面 localStorage 获取 access_token。
    /// </summary>
    /// <param name="page">IPage 实例</param>
    /// <returns>access_token 字符串，失败返回 null</returns>
    private async Task<string?> GetAccessTokenAsync(IPage page)
    {
        try
        {
            var token = await page.EvaluateAsync<string?>(
                "() => localStorage.getItem('access_token')");
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception ex)
        {
            LogWarn($"获取 access_token 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 调用 Kimi 内部 API 获取数据。
    /// </summary>
    /// <param name="token">access_token</param>
    /// <returns>API 响应数据，失败返回 null</returns>
    private async Task<KimiWebApiResponse?> FetchDataFromInternalApiAsync(string token)
    {
        try
        {
            var result = new KimiWebApiResponse();

            // API 1: 获取订阅统计（用量进度）
            var statsUrl = "https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscriptionStats";
            var statsResponse = await PostJsonAsync<KimiSubscriptionStatsResponse>(statsUrl, token, new { });
            if (statsResponse != null)
            {
                result.SubscriptionStats = statsResponse;
            }

            // API 2: 获取订阅详情
            var subUrl = "https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscription";
            var subResponse = await PostJsonAsync<KimiSubscriptionResponse>(subUrl, token, new { });
            if (subResponse != null)
            {
                result.Subscription = subResponse;
            }

            // API 3: 获取功能用量
            var usageUrl = "https://www.kimi.com/api/user/usage";
            var usageResponse = await PostJsonAsync<KimiUserUsageResponse>(usageUrl, token, new { });
            if (usageResponse != null)
            {
                result.UserUsage = usageResponse;
            }

            // API 4: 获取使用记录（Kimi Code）
            var requestsUrl = "https://www.kimi.com/apiv2/kimi.gateway.code.v1.UsageService/ListUnifiedRequests";
            var requestsResponse = await PostJsonAsync<KimiUnifiedRequestsResponse>(requestsUrl, token, new { pageSize = 10 });
            if (requestsResponse != null)
            {
                result.UnifiedRequests = requestsResponse;
            }

            return result;
        }
        catch (Exception ex)
        {
            LogError("调用内部 API 失败", ex);
            return null;
        }
    }

    /// <summary>
    /// 发送 POST JSON 请求到内部 API。
    /// </summary>
    /// <typeparam name="T">响应类型</typeparam>
    /// <param name="url">API URL</param>
    /// <param name="token">Bearer token</param>
    /// <param name="body">请求体</param>
    /// <returns>反序列化后的响应</returns>
    private async Task<T?> PostJsonAsync<T>(string url, string token, object body) where T : class
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            var jsonBody = JsonSerializer.Serialize(body);
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                LogWarn($"API 请求失败: {url}, Status={response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            LogError($"API 请求异常: {url}", ex);
            return null;
        }
    }

    /// <summary>
    /// 将 API 响应合并到 UsageInfo。
    /// </summary>
    /// <param name="usageInfo">目标 UsageInfo</param>
    /// <param name="apiResult">API 响应数据</param>
    private void MergeApiResult(UsageInfo usageInfo, KimiWebApiResponse apiResult)
    {
        // 解析订阅统计
        if (apiResult.SubscriptionStats != null)
        {
            var stats = apiResult.SubscriptionStats;

            // 5h Code 频限
            if (stats.RatelimitCode5h != null)
            {
                usageInfo.Extra["ratelimit_5h_ratio"] = stats.RatelimitCode5h.Ratio;
                usageInfo.Extra["ratelimit_5h_reset"] = stats.RatelimitCode5h.ResetTime ?? string.Empty;
            }

            // 7天 Code 用量
            if (stats.RatelimitCode7d != null)
            {
                usageInfo.Extra["ratelimit_7d_ratio"] = stats.RatelimitCode7d.Ratio;
                usageInfo.Extra["ratelimit_7d_reset"] = stats.RatelimitCode7d.ResetTime ?? string.Empty;
            }

            // 订阅余额（总使用量比例）
            if (stats.SubscriptionBalance != null)
            {
                usageInfo.Extra["amount_used_ratio"] = stats.SubscriptionBalance.AmountUsedRatio;
                usageInfo.Extra["kimi_code_used_ratio"] = stats.SubscriptionBalance.KimiCodeUsedRatio;
                usageInfo.Extra["expire_time"] = stats.SubscriptionBalance.ExpireTime ?? string.Empty;

                // 设置百分比用于环形图
                usageInfo.TotalAmount = 100;
                usageInfo.UsedAmount = (decimal)(stats.SubscriptionBalance.AmountUsedRatio * 100);
                usageInfo.Unit = "%";
            }
        }

        // 解析订阅详情
        if (apiResult.Subscription?.Subscription != null)
        {
            var sub = apiResult.Subscription.Subscription;
            usageInfo.Extra["subscription_title"] = sub.Goods?.Title ?? string.Empty;
            usageInfo.Extra["subscription_level"] = sub.Goods?.MembershipLevel ?? string.Empty;
            usageInfo.Extra["subscription_status"] = sub.Status ?? string.Empty;
            usageInfo.Extra["current_end_time"] = sub.CurrentEndTime ?? string.Empty;
            usageInfo.Extra["next_billing_time"] = sub.NextBillingTime ?? string.Empty;
        }

        // 解析功能用量
        if (apiResult.UserUsage != null)
        {
            var usage = apiResult.UserUsage;

            if (usage.ResearchUsage != null)
            {
                usageInfo.Extra["research_used"] = usage.ResearchUsage.Used;
                usageInfo.Extra["research_total"] = usage.ResearchUsage.Total;
                usageInfo.Extra["research_remain"] = usage.ResearchUsage.Remain;
            }

            if (usage.ImageGen != null)
            {
                usageInfo.Extra["image_gen_used"] = usage.ImageGen.Used;
                usageInfo.Extra["image_gen_total"] = usage.ImageGen.Total;
                usageInfo.Extra["image_gen_remain"] = usage.ImageGen.Remain;
            }
        }

        // 解析使用记录
        if (apiResult.UnifiedRequests?.Requests != null)
        {
            var requests = apiResult.UnifiedRequests.Requests;
            usageInfo.Extra["request_count"] = requests.Count;

            // 统计成功/失败
            var successCount = requests.Count(r => r.Success);
            usageInfo.Extra["request_success_count"] = successCount;
            usageInfo.Extra["request_fail_count"] = requests.Count - successCount;
        }

        // 设置 Quantity（req-086 新字段）
        if (apiResult.SubscriptionStats?.SubscriptionBalance != null)
        {
            var ratio = apiResult.SubscriptionStats.SubscriptionBalance.AmountUsedRatio;
            usageInfo.Quantity = new Quantity((decimal)(ratio * 100), new PercentUnit());
        }
    }

    /// <summary>
    /// DOM 解析回退方案。
    /// </summary>
    /// <param name="page">IPage 实例</param>
    /// <param name="usageInfo">目标 UsageInfo</param>
    private async Task ParseDomAsync(IPage page, UsageInfo usageInfo)
    {
        try
        {
            // 等待用量进度元素加载
            await page.WaitForSelectorAsync("[class*='usage'], [class*='progress'], [class*='quota']",
                new PageWaitForSelectorOptions { Timeout = 10000 });

            // 提取用量百分比文本
            var usageText = await page.EvaluateAsync<string?>(@"() => {
                const el = document.querySelector('[class*=""usage""], [class*=""progress""], [class*=""quota""]');
                return el ? el.textContent : null;
            }");

            if (!string.IsNullOrWhiteSpace(usageText))
            {
                // 尝试解析百分比数字
                var match = _usagePercentRegex.Match(usageText);
                if (match.Success && decimal.TryParse(match.Groups[1].Value,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
                {
                    usageInfo.TotalAmount = 100;
                    usageInfo.UsedAmount = percent;
                    usageInfo.Unit = "%";
                }
            }

            usageInfo.Extra["parse_method"] = "dom_fallback";
        }
        catch (Exception ex)
        {
            LogWarn($"DOM 解析失败: {ex.Message}");
            usageInfo.Extra["parse_method"] = "dom_failed";
        }
    }
}

/// <summary>
/// Kimi 网页模式 API 响应聚合。
/// </summary>
internal class KimiWebApiResponse
{
    public KimiSubscriptionStatsResponse? SubscriptionStats { get; set; }
    public KimiSubscriptionResponse? Subscription { get; set; }
    public KimiUserUsageResponse? UserUsage { get; set; }
    public KimiUnifiedRequestsResponse? UnifiedRequests { get; set; }
}

/// <summary>
/// GetSubscriptionStats API 响应。
/// </summary>
internal class KimiSubscriptionStatsResponse
{
    [JsonPropertyName("ratelimitCode5h")]
    public KimiRatelimit? RatelimitCode5h { get; set; }

    [JsonPropertyName("ratelimitCode7d")]
    public KimiRatelimit? RatelimitCode7d { get; set; }

    [JsonPropertyName("subscriptionBalance")]
    public KimiSubscriptionBalance? SubscriptionBalance { get; set; }
}

internal class KimiRatelimit
{
    [JsonPropertyName("ratio")]
    public double Ratio { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("resetTime")]
    public string? ResetTime { get; set; }
}

internal class KimiSubscriptionBalance
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("feature")]
    public string? Feature { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("amountUsedRatio")]
    public double AmountUsedRatio { get; set; }

    [JsonPropertyName("kimiCodeUsedRatio")]
    public double KimiCodeUsedRatio { get; set; }

    [JsonPropertyName("expireTime")]
    public string? ExpireTime { get; set; }
}

/// <summary>
/// GetSubscription API 响应。
/// </summary>
internal class KimiSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public KimiSubscription? Subscription { get; set; }

    [JsonPropertyName("balances")]
    public List<KimiSubscriptionBalance>? Balances { get; set; }

    [JsonPropertyName("capabilities")]
    public List<KimiCapability>? Capabilities { get; set; }
}

internal class KimiSubscription
{
    [JsonPropertyName("goods")]
    public KimiGoods? Goods { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("currentEndTime")]
    public string? CurrentEndTime { get; set; }

    [JsonPropertyName("nextBillingTime")]
    public string? NextBillingTime { get; set; }
}

internal class KimiGoods
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("membershipLevel")]
    public string? MembershipLevel { get; set; }

    [JsonPropertyName("amounts")]
    public List<KimiAmount>? Amounts { get; set; }

    [JsonPropertyName("billingCycle")]
    public KimiBillingCycle? BillingCycle { get; set; }
}

internal class KimiAmount
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("priceInCents")]
    public string? PriceInCents { get; set; }
}

internal class KimiBillingCycle
{
    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("timeUnit")]
    public string? TimeUnit { get; set; }
}

internal class KimiCapability
{
    [JsonPropertyName("feature")]
    public string? Feature { get; set; }

    [JsonPropertyName("constraint")]
    public KimiConstraint? Constraint { get; set; }
}

internal class KimiConstraint
{
    [JsonPropertyName("parallelism")]
    public int Parallelism { get; set; }
}

/// <summary>
/// /api/user/usage API 响应。
/// </summary>
internal class KimiUserUsageResponse
{
    [JsonPropertyName("research_usage")]
    public KimiUsageItem? ResearchUsage { get; set; }

    [JsonPropertyName("image_gen")]
    public KimiUsageItem? ImageGen { get; set; }

    [JsonPropertyName("deep_research")]
    public KimiPermission? DeepResearch { get; set; }

    [JsonPropertyName("membership")]
    public KimiPermission? Membership { get; set; }
}

internal class KimiUsageItem
{
    [JsonPropertyName("used")]
    public int Used { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("remain")]
    public int Remain { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

internal class KimiPermission
{
    [JsonPropertyName("has_permission")]
    public bool HasPermission { get; set; }
}

/// <summary>
/// ListUnifiedRequests API 响应。
/// </summary>
internal class KimiUnifiedRequestsResponse
{
    [JsonPropertyName("requests")]
    public List<KimiUnifiedRequest>? Requests { get; set; }
}

internal class KimiUnifiedRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("apiKey")]
    public KimiApiKey? ApiKey { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }

    [JsonPropertyName("requestTime")]
    public string? RequestTime { get; set; }

    [JsonPropertyName("callType")]
    public string? CallType { get; set; }

    [JsonPropertyName("httpStatusCode")]
    public int HttpStatusCode { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("clientType")]
    public string? ClientType { get; set; }
}

internal class KimiApiKey
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("scope")]
    public List<string>? Scope { get; set; }
}
