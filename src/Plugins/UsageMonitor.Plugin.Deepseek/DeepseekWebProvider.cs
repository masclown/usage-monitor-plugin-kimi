using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Deepseek;

/// <summary>
/// req-084：DeepSeek 网页模式用量查询插件。
/// 通过 platform.deepseek.com 内部 API 获取余额和用量数据。
/// 继承 <see cref="WebPluginBase"/> 复用浏览器生命周期管理。
/// </summary>
public class DeepseekWebProvider : WebPluginBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "deepseek_web";

    /// <inheritdoc />
    public override string DisplayName => "DeepSeek (网页模式)";

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override string Author => "UsageMonitor";

    /// <inheritdoc />
    public override string Description => "通过 DeepSeek 平台网页获取余额和用量信息（支持图表展示）";

    /// <inheritdoc />
    protected override string LoginUrl => "https://platform.deepseek.com/";

    /// <inheritdoc />
    protected override string UsageUrl => "https://platform.deepseek.com/usage";

    /// <inheritdoc />
    protected override string[] CookieDomainFilters => new[] { ".deepseek.com", "platform.deepseek.com" };

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
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring, CardChartKind.HeatMap
    };

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent", "Balance" };

    /// <inheritdoc />
    public override IReadOnlyList<BalanceItem> BalanceItems => new[]
    {
        new BalanceItem { Label = "总余额", Value = "--", Detail = null },
        new BalanceItem { Label = "赠送余额", Value = "--", Detail = null },
        new BalanceItem { Label = "充值余额", Value = "--", Detail = null }
    };

    /// <summary>
    /// 解析用量页面，提取余额和用量数据。
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
            // 尝试从 localStorage 获取 userToken 调用内部 API
            var token = await GetUserTokenAsync(page);
            if (!string.IsNullOrEmpty(token))
            {
                LogInfo("获取到 userToken，尝试调用内部 API");
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
    /// 从页面 localStorage 获取 userToken。
    /// </summary>
    /// <param name="page">IPage 实例</param>
    /// <returns>userToken 字符串，失败返回 null</returns>
    private async Task<string?> GetUserTokenAsync(IPage page)
    {
        try
        {
            var token = await page.EvaluateAsync<string?>(
                "() => localStorage.getItem('userToken')");
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception ex)
        {
            LogWarn($"获取 userToken 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 调用 DeepSeek 内部 API 获取数据。
    /// </summary>
    /// <param name="token">userToken</param>
    /// <returns>API 响应数据，失败返回 null</returns>
    private async Task<DeepseekWebApiResponse?> FetchDataFromInternalApiAsync(string token)
    {
        try
        {
            // API 1: 获取用户摘要（余额信息）
            var summaryUrl = "https://platform.deepseek.com/api/v0/users/get_user_summary";
            var summaryResponse = await PostJsonAsync<DeepseekSummaryResponse>(
                summaryUrl, token, new { });

            if (summaryResponse?.Data?.BizData == null)
            {
                LogWarn("get_user_summary API 返回空数据");
                return null;
            }

            var result = new DeepseekWebApiResponse
            {
                Summary = summaryResponse.Data.BizData
            };

            // API 2: 获取用量明细（按 API Key 分组）
            var costUrl = "https://platform.deepseek.com/api/v0/users/by_api_key/cost";
            var costResponse = await PostJsonAsync<DeepseekCostResponse>(
                costUrl, token, new { });

            if (costResponse?.Data != null)
            {
                result.CostDetails = costResponse.Data;
            }

            // API 3: 获取 Token 用量统计
            var amountUrl = "https://platform.deepseek.com/api/v0/users/by_api_key/amount";
            var amountResponse = await PostJsonAsync<DeepseekAmountResponse>(
                amountUrl, token, new { });

            if (amountResponse?.Data != null)
            {
                result.AmountDetails = amountResponse.Data;
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
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
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
    private void MergeApiResult(UsageInfo usageInfo, DeepseekWebApiResponse apiResult)
    {
        var summary = apiResult.Summary;
        if (summary == null) return;

        // 解析余额信息
        decimal totalBalance = 0;
        decimal grantedBalance = 0;
        decimal toppedUpBalance = 0;

        if (summary.NormalWallets != null && summary.NormalWallets.Count > 0)
        {
            if (decimal.TryParse(summary.NormalWallets[0].Balance, NumberStyles.Any, CultureInfo.InvariantCulture, out var normal))
            {
                toppedUpBalance = normal;
                totalBalance += normal;
            }
        }

        if (summary.BonusWallets != null && summary.BonusWallets.Count > 0)
        {
            if (decimal.TryParse(summary.BonusWallets[0].Balance, NumberStyles.Any, CultureInfo.InvariantCulture, out var bonus))
            {
                grantedBalance = bonus;
                totalBalance += bonus;
            }
        }

        // 设置余额字段
        usageInfo.TotalAmount = totalBalance;
        usageInfo.Unit = "CNY";

        // 存储到 Extra 供卡片显示
        usageInfo.Extra["total_balance"] = totalBalance.ToString("F2", CultureInfo.InvariantCulture);
        usageInfo.Extra["granted_balance"] = grantedBalance.ToString("F2", CultureInfo.InvariantCulture);
        usageInfo.Extra["topped_up_balance"] = toppedUpBalance.ToString("F2", CultureInfo.InvariantCulture);

        // 解析 Token 用量
        if (!string.IsNullOrEmpty(summary.MonthlyTokenUsage) &&
            long.TryParse(summary.MonthlyTokenUsage, NumberStyles.Any, CultureInfo.InvariantCulture, out var monthlyTokens))
        {
            usageInfo.UsedTokens = monthlyTokens;
            usageInfo.Extra["monthly_token_usage"] = monthlyTokens;
        }

        // 解析用量明细（用于图表）
        if (apiResult.CostDetails != null)
        {
            var costData = new Dictionary<string, decimal>();
            foreach (var item in apiResult.CostDetails)
            {
                if (!string.IsNullOrEmpty(item.ApiKeyName) && item.TotalCost.HasValue)
                {
                    costData[item.ApiKeyName] = item.TotalCost.Value;
                }
            }
            usageInfo.Extra["cost_by_api_key"] = costData;
        }

        if (apiResult.AmountDetails != null)
        {
            var amountData = new Dictionary<string, long>();
            foreach (var item in apiResult.AmountDetails)
            {
                if (!string.IsNullOrEmpty(item.ApiKeyName) && item.TotalAmount.HasValue)
                {
                    amountData[item.ApiKeyName] = item.TotalAmount.Value;
                }
            }
            usageInfo.Extra["amount_by_api_key"] = amountData;
        }

        // 设置 Quantity（req-086 新字段）
        usageInfo.Quantity = new Quantity(usageInfo.UsedTokens, new TokenUnit("token"));
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
            // 等待余额元素加载
            await page.WaitForSelectorAsync(".balance-item, .wallet-balance, [class*='balance']",
                new PageWaitForSelectorOptions { Timeout = 10000 });

            // 提取余额文本
            var balanceText = await page.EvaluateAsync<string?>(@"() => {
                const el = document.querySelector('.balance-item, .wallet-balance, [class*=""balance""]');
                return el ? el.textContent : null;
            }");

            if (!string.IsNullOrWhiteSpace(balanceText))
            {
                // 尝试解析数字
                var match = System.Text.RegularExpressions.Regex.Match(balanceText, @"[\d,]+\.?\d*");
                if (match.Success && decimal.TryParse(match.Value.Replace(",", ""), 
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var balance))
                {
                    usageInfo.TotalAmount = balance;
                    usageInfo.Unit = "CNY";
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
/// DeepSeek 网页模式 API 响应聚合。
/// </summary>
internal class DeepseekWebApiResponse
{
    public DeepseekBizData? Summary { get; set; }
    public List<DeepseekCostItem>? CostDetails { get; set; }
    public List<DeepseekAmountItem>? AmountDetails { get; set; }
}

/// <summary>
/// get_user_summary API 响应。
/// </summary>
internal class DeepseekSummaryResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public DeepseekSummaryData? Data { get; set; }
}

internal class DeepseekSummaryData
{
    [JsonPropertyName("biz_data")]
    public DeepseekBizData? BizData { get; set; }
}

internal class DeepseekBizData
{
    [JsonPropertyName("normal_wallets")]
    public List<DeepseekWallet>? NormalWallets { get; set; }

    [JsonPropertyName("bonus_wallets")]
    public List<DeepseekWallet>? BonusWallets { get; set; }

    [JsonPropertyName("monthly_token_usage")]
    public string? MonthlyTokenUsage { get; set; }
}

internal class DeepseekWallet
{
    [JsonPropertyName("balance")]
    public string? Balance { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

/// <summary>
/// by_api_key/cost API 响应。
/// </summary>
internal class DeepseekCostResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public List<DeepseekCostItem>? Data { get; set; }
}

internal class DeepseekCostItem
{
    [JsonPropertyName("api_key_name")]
    public string? ApiKeyName { get; set; }

    [JsonPropertyName("total_cost")]
    public decimal? TotalCost { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

/// <summary>
/// by_api_key/amount API 响应。
/// </summary>
internal class DeepseekAmountResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public List<DeepseekAmountItem>? Data { get; set; }
}

internal class DeepseekAmountItem
{
    [JsonPropertyName("api_key_name")]
    public string? ApiKeyName { get; set; }

    [JsonPropertyName("total_amount")]
    public long? TotalAmount { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}
