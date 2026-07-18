using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.MiMo;

/// <summary>
/// MiMo Token Plan 用量查询插件
/// 查询 MiMo 平台的 Token Plan 用量信息
/// </summary>
public class MiMoProvider : IUsageProvider
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    public string ProviderId => "mimo";

    /// <inheritdoc />
    public string DisplayName => "MiMo";

    /// <inheritdoc />
    public string? IconPath => null;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "UsageMonitor";

    /// <inheritdoc />
    public string Description => "查询 MiMo Token Plan 的用量信息";

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        // req-013：从 StandardConfigFields 工厂方法生成"重复声明模板"，字段 key / i18n key / 类型 / required 全部对齐重构前。
        StandardConfigFields.ApiKey("MiMo"),
        StandardConfigFields.BaseUrl("MiMo", "https://api.mimo.ai")
    };

    /// <summary>
    /// 查询 MiMo Token Plan 的用量信息
    /// </summary>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config)
    {
        var apiKey = config.GetValue("ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "API Key 未配置");
        }

        var baseUrl = config.GetValue("BaseUrl") ?? "https://api.mimo.ai";

        try
        {
            return await QueryUsageAsync(baseUrl, apiKey);
        }
        catch (HttpRequestException ex)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, $"网络请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, ex.Message);
        }
    }

    /// <summary>
    /// 调用 MiMo API 查询用量
    /// </summary>
    private async Task<UsageInfo> QueryUsageAsync(string baseUrl, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/v1/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return UsageInfo.CreateError(ProviderId, DisplayName,
                $"API返回错误 ({(int)response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var usageResponse = JsonSerializer.Deserialize<MiMoUsageResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (usageResponse == null)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "无法解析API响应");
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
            ExpireDate = usageResponse.ExpireDate,
            LastUpdated = DateTime.Now,
            Extra = new Dictionary<string, object>
            {
                ["planName"] = usageResponse.PlanName ?? "Unknown",
                ["planType"] = usageResponse.PlanType ?? "Unknown"
            }
        };
    }

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public async Task<bool> ValidateConfigAsync(ProviderConfig config)
    {
        var result = await GetUsageAsync(config);
        return result.IsSuccess;
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
