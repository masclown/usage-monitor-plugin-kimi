using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Plugin.OpenAI;

/// <summary>
/// OpenAI API 用量查询插件（预留实现）
/// 查询 OpenAI 平台的 API 用量和额度信息
/// </summary>
public class OpenAIProvider : IUsageProvider
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    public string ProviderId => "openai";

    /// <inheritdoc />
    public string DisplayName => "OpenAI";

    /// <inheritdoc />
    public string? IconPath => null;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "UsageMonitor";

    /// <inheritdoc />
    public string Description => "查询 OpenAI API 的用量和额度信息";

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        new ConfigField("ApiKey", "API Key", ConfigFieldType.Password, true,
            placeholder: "sk-xxxxxxxxxxxxxxxx"),
        new ConfigField("BaseUrl", "API 地址", ConfigFieldType.Text, false,
            defaultValue: "https://api.openai.com",
            placeholder: "https://api.openai.com"),
        new ConfigField("Organization", "Organization ID", ConfigFieldType.Text, false,
            placeholder: "org-xxxxxxxx (可选)")
    };

    /// <summary>
    /// 查询 OpenAI API 的用量信息
    /// 调用 /v1/usage 或 /dashboard/billing 接口
    /// </summary>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config)
    {
        var apiKey = config.GetValue("ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "API Key 未配置");
        }

        var baseUrl = config.GetValue("BaseUrl") ?? "https://api.openai.com";

        try
        {
            return await QueryUsageAsync(baseUrl, apiKey, config.GetValue("Organization"));
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
    /// 调用 OpenAI API 查询用量
    /// </summary>
    private async Task<UsageInfo> QueryUsageAsync(string baseUrl, string apiKey, string? organization)
    {
        // 查询当月使用情况
        var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var endDate = DateTime.Now;
        var startTimestamp = new DateTimeOffset(startDate).ToUnixTimeSeconds();
        var endTimestamp = new DateTimeOffset(endDate).ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}/v1/organization/usage?start_time={startTimestamp}&end_time={endTimestamp}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrEmpty(organization))
            request.Headers.Add("OpenAI-Organization", organization);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return UsageInfo.CreateError(ProviderId, DisplayName,
                $"API返回错误 ({(int)response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var usageResponse = JsonSerializer.Deserialize<OpenAIUsageResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (usageResponse?.Data == null)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "无法解析API响应");
        }

        // 汇总所有结果
        var totalUsed = usageResponse.Data.Sum(d => d.Amount);

        return new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            UsedAmount = totalUsed,
            Unit = "USD",
            LastUpdated = DateTime.Now,
            Extra = new Dictionary<string, object>
            {
                ["dataPoints"] = usageResponse.Data.Length,
                ["period"] = $"{startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}"
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
/// OpenAI 用量API响应模型
/// </summary>
internal class OpenAIUsageResponse
{
    [JsonPropertyName("data")]
    public OpenAIUsageData[]? Data { get; set; }
}

/// <summary>
/// OpenAI 单项用量数据
/// </summary>
internal class OpenAIUsageData
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("line_item")]
    public string? LineItem { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
