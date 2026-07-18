using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Deepseek;

/// <summary>
/// Deepseek API 用量查询插件
/// 通过 Deepseek API 查询账户余额和用量信息
/// API 基础地址：https://api.deepseek.com/
/// </summary>
public class DeepseekProvider : IUsageProvider
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    public string ProviderId => "deepseek";

    /// <inheritdoc />
    public string DisplayName => "Deepseek";

    /// <inheritdoc />
    public string? IconPath => null;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "UsageMonitor";

    /// <inheritdoc />
    public string Description => "查询 Deepseek API 的账户余额和用量信息";

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        // req-013：从 StandardConfigFields 工厂方法生成"重复声明模板"，字段 key / i18n key / 类型 / required 全部对齐重构前。
        StandardConfigFields.ApiKey("Deepseek"),
        StandardConfigFields.BaseUrl("Deepseek", "https://api.deepseek.com")
    };

    /// <summary>
    /// 查询 Deepseek API 的用量信息
    /// 调用 /user/balance 接口获取账户余额
    /// </summary>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config)
    {
        var apiKey = config.GetValue("ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "API Key 未配置");
        }

        var baseUrl = config.GetValue("BaseUrl") ?? "https://api.deepseek.com";

        try
        {
            // 查询账户余额
            var balanceInfo = await QueryBalanceAsync(baseUrl, apiKey);
            return balanceInfo;
        }
        catch (HttpRequestException ex)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, $"网络请求失败: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, $"解析响应失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, ex.Message);
        }
    }

    /// <summary>
    /// 调用 Deepseek /user/balance 接口查询余额
    /// </summary>
    private async Task<UsageInfo> QueryBalanceAsync(string baseUrl, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/user/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return UsageInfo.CreateError(ProviderId, DisplayName,
                $"API返回错误 ({(int)response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var balanceResponse = JsonSerializer.Deserialize<DeepseekBalanceResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (balanceResponse == null)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "无法解析API响应");
        }

        // 构建用量信息
        var usageInfo = new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            LastUpdated = DateTime.Now
        };

        if (balanceResponse.BalanceInfos != null && balanceResponse.BalanceInfos.Count > 0)
        {
            var primary = balanceResponse.BalanceInfos[0];

            // 已用和总额
            if (decimal.TryParse(primary.TotalGranted, out var totalGranted))
                usageInfo.TotalAmount = totalGranted;

            if (decimal.TryParse(primary.TotalUsed, out var totalUsed))
                usageInfo.UsedAmount = totalUsed;

            if (decimal.TryParse(primary.TotalLeft, out var totalLeft))
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

    /// <summary>
    /// 验证API Key是否有效
    /// </summary>
    public async Task<bool> ValidateConfigAsync(ProviderConfig config)
    {
        var result = await GetUsageAsync(config);
        return result.IsSuccess;
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
