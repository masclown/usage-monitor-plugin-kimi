using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.OpenAI;

/// <summary>
/// OpenAI API 用量查询插件（预留实现）
/// 查询 OpenAI 平台的 API 用量和额度信息
/// <para>
/// req-065 B14：继承 <see cref="HttpUsageProviderBase"/>，消除重复样板代码。
/// </para>
/// </summary>
public class OpenAIProvider : HttpUsageProviderBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "openai";

    /// <inheritdoc />
    public override string DisplayName => "OpenAI";

    /// <inheritdoc />
    public override string Description => "查询 OpenAI API 的用量和额度信息";

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        // req-013：从 StandardConfigFields 工厂方法生成"重复声明模板"，字段 key / i18n key / 类型 / required 全部对齐重构前。
        StandardConfigFields.ApiKey("OpenAI"),
        StandardConfigFields.BaseUrl("OpenAI", "https://api.openai.com"),
        StandardConfigFields.Organization("OpenAI")
    };

    /// <summary>
    /// 查询 OpenAI API 的用量信息
    /// 调用 /v1/usage 或 /dashboard/billing 接口
    /// </summary>
    public override async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var apiKey = config.GetValue("ApiKey");
        if (ValidateApiKey(apiKey) is { } apiKeyError)
            return apiKeyError;

        if (ValidateBaseUrl(config.GetValue("BaseUrl"), "https://api.openai.com", out var baseUrl) is { } urlError)
            return urlError;

        try
        {
            return await QueryUsageAsync(baseUrl, apiKey!, config.GetValue("Organization"), ct);
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
    /// 调用 OpenAI API 查询用量
    /// </summary>
    private async Task<UsageInfo> QueryUsageAsync(string baseUrl, string apiKey, string? organization, CancellationToken ct)
    {
        // 查询当月使用情况（使用 UTC 时间确保时区一致性）
        var startDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var endDate = DateTime.UtcNow;
        var startTimestamp = new DateTimeOffset(startDate).ToUnixTimeSeconds();
        var endTimestamp = new DateTimeOffset(endDate).ToUnixTimeSeconds();

        var usageResponse = await GetJsonAsync<OpenAIUsageResponse>(
            baseUrl,
            $"/v1/organization/usage?start_time={startTimestamp}&end_time={endTimestamp}",
            req =>
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                if (!string.IsNullOrEmpty(organization))
                    req.Headers.Add("OpenAI-Organization", organization);
            },
            ct);

        if (usageResponse?.Data == null)
        {
            return CreateError("无法解析API响应");
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
            // req-067 B21：统一使用 UTC 时间存储，避免时区问题
            LastUpdated = DateTime.UtcNow,
            Extra = new Dictionary<string, object>
            {
                ["dataPoints"] = usageResponse.Data.Length,
                ["period"] = $"{startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}"
            }
        };
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
