using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Deepseek;

/// <summary>
/// Deepseek API 用量查询插件
/// 通过 Deepseek API 查询账户余额和用量信息
/// API 基础地址：https://api.deepseek.com/
/// <para>
/// req-065 B14：继承 <see cref="HttpUsageProviderBase"/>，消除重复样板代码。
/// </para>
/// </summary>
public class DeepseekProvider : HttpUsageProviderBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "deepseek";

    /// <inheritdoc />
    public override string DisplayName => "Deepseek";

    /// <inheritdoc />
    public override string Description => "查询 Deepseek API 的账户余额和用量信息";

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        // req-013：从 StandardConfigFields 工厂方法生成"重复声明模板"，字段 key / i18n key / 类型 / required 全部对齐重构前。
        StandardConfigFields.ApiKey("Deepseek"),
        StandardConfigFields.BaseUrl("Deepseek", "https://api.deepseek.com")
    };

    /// <summary>
    /// 查询 Deepseek API 的用量信息
    /// 调用 /user/balance 接口获取账户余额
    /// </summary>
    public override async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var apiKey = config.GetValue("ApiKey");
        if (ValidateApiKey(apiKey) is { } apiKeyError)
            return apiKeyError;

        if (ValidateBaseUrl(config.GetValue("BaseUrl"), "https://api.deepseek.com", out var baseUrl) is { } urlError)
            return urlError;

        try
        {
            // 查询账户余额
            var balanceInfo = await QueryBalanceAsync(baseUrl, apiKey!, ct);
            return balanceInfo;
        }
        catch (HttpRequestException ex)
        {
            return CreateError($"网络请求失败: {ex.Message}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            return CreateError($"解析响应失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return CreateError(ex.Message);
        }
    }

    /// <summary>
    /// 调用 Deepseek /user/balance 接口查询余额
    /// </summary>
    private async Task<UsageInfo> QueryBalanceAsync(string baseUrl, string apiKey, CancellationToken ct)
    {
        var balanceResponse = await GetJsonAsync<DeepseekBalanceResponse>(
            baseUrl,
            "/user/balance",
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey),
            ct);

        if (balanceResponse == null)
        {
            return CreateError("无法解析API响应");
        }

        // 构建用量信息
        var usageInfo = new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            // req-067 B21：统一使用 UTC 时间存储，避免时区问题
            LastUpdated = DateTime.UtcNow
        };

        if (balanceResponse.BalanceInfos != null && balanceResponse.BalanceInfos.Count > 0)
        {
            var primary = balanceResponse.BalanceInfos[0];

            // 已用和总额
            // req-060：加 InvariantCulture 避免不同区域设置下解析失败
            if (decimal.TryParse(primary.TotalGranted, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalGranted))
                usageInfo.TotalAmount = totalGranted;

            if (decimal.TryParse(primary.TotalUsed, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalUsed))
                usageInfo.UsedAmount = totalUsed;

            if (decimal.TryParse(primary.TotalLeft, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalLeft))
            {
                // 如果TotalLeft可用，用TotalUsed+TotalLeft作为TotalAmount
                if (usageInfo.TotalAmount == 0)
                    usageInfo.TotalAmount = totalUsed + totalLeft;
            }

            usageInfo.Unit = "CNY"; // Deepseek 默认人民币计价

            // 额外信息
            if (!string.IsNullOrEmpty(primary.Currency))
                usageInfo.Unit = primary.Currency;
        }

        // 是否已认证
        usageInfo.Extra["isVerified"] = balanceResponse.IsAvailable;

        return usageInfo;
    }
}

/// <summary>
/// Deepseek 余额API响应模型
/// </summary>
internal class DeepseekBalanceResponse
{
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("balance_infos")]
    public List<DeepseekBalanceInfo>? BalanceInfos { get; set; }
}

/// <summary>
/// Deepseek 单项余额信息
/// </summary>
internal class DeepseekBalanceInfo
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("total_balance")]
    public string? TotalBalance { get; set; }

    [JsonPropertyName("total_granted")]
    public string? TotalGranted { get; set; }

    [JsonPropertyName("total_used")]
    public string? TotalUsed { get; set; }

    [JsonPropertyName("total_left")]
    public string? TotalLeft { get; set; }
}
