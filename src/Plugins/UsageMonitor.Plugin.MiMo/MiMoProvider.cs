using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.MiMo;

/// <summary>
/// MiMo Token Plan 用量查询插件
/// 查询 MiMo 平台的 Token Plan 用量信息
/// <para>
/// req-065 B14：继承 <see cref="HttpUsageProviderBase"/>，消除重复样板代码。
/// </para>
/// </summary>
public class MiMoProvider : HttpUsageProviderBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "mimo";

    /// <inheritdoc />
    public override string DisplayName => "MiMo";

    /// <inheritdoc />
    public override string Description => "查询 MiMo Token Plan 的用量信息";

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        // req-013：从 StandardConfigFields 工厂方法生成"重复声明模板"，字段 key / i18n key / 类型 / required 全部对齐重构前。
        StandardConfigFields.ApiKey("MiMo"),
        StandardConfigFields.BaseUrl("MiMo", "https://api.mimo.ai")
    };

    /// <summary>
    /// 查询 MiMo Token Plan 的用量信息
    /// </summary>
    public override async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var apiKey = config.GetValue("ApiKey");
        if (ValidateApiKey(apiKey) is { } apiKeyError)
            return apiKeyError;

        if (ValidateBaseUrl(config.GetValue("BaseUrl"), "https://api.mimo.ai", out var baseUrl) is { } urlError)
            return urlError;

        try
        {
            return await QueryUsageAsync(baseUrl, apiKey!, ct);
        }
        catch (HttpRequestException ex)
        {
            return CreateError($"网络请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return CreateError(ex.Message);
        }
    }

    /// <summary>
    /// 调用 MiMo API 查询用量
    /// </summary>
    private async Task<UsageInfo> QueryUsageAsync(string baseUrl, string apiKey, CancellationToken ct)
    {
        var usageResponse = await GetJsonAsync<MiMoUsageResponse>(
            baseUrl,
            "/v1/usage",
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey),
            ct);

        if (usageResponse == null)
        {
            return CreateError("无法解析API响应");
        }

        return new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            UsedTokens = usageResponse.UsedTokens,
            TotalTokens = usageResponse.TotalTokens,
            Unit = "Tokens",
            UsedAmount = usageResponse.UsedCredits,
            TotalAmount = usageResponse.TotalCredits,
            // req-005-011：主金额指标为积分（UsedCredits），写入强类型 Quantity(CreditUnit)；Token 计数仍走 UsedTokens，故 GetUsagePercentage/GetRemainingTokens 结果不变。
            Quantity = new UsageMonitor.Core.Models.Quantity((decimal)usageResponse.UsedCredits, new UsageMonitor.Core.Models.CreditUnit("Credits")),
            ExpireDate = usageResponse.ExpireDate,
            // req-067 B21：统一使用 UTC 时间存储，避免时区问题
            LastUpdated = DateTime.UtcNow,
            Extra = new Dictionary<string, object>
            {
                ["planName"] = usageResponse.PlanName ?? "Unknown",
                ["planType"] = usageResponse.PlanType ?? "Unknown"
            }
        };
    }
}

/// <summary>
/// MiMo 用量API响应模型
/// </summary>
internal class MiMoUsageResponse
{
    [JsonPropertyName("used_tokens")]
    public long UsedTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("used_credits")]
    public decimal UsedCredits { get; set; }

    [JsonPropertyName("total_credits")]
    public decimal TotalCredits { get; set; }

    [JsonPropertyName("plan_name")]
    public string? PlanName { get; set; }

    [JsonPropertyName("plan_type")]
    public string? PlanType { get; set; }

    [JsonPropertyName("expire_date")]
    public DateTime? ExpireDate { get; set; }
}
