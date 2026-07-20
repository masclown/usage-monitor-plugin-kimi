using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Kimi;

/// <summary>
/// req-085：Kimi API 模式用量查询插件。
/// 通过 Moonshot API 查询账户余额信息。
/// API 基础地址：https://api.moonshot.cn/
/// </summary>
public class KimiApiProvider : HttpUsageProviderBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "kimi_api";

    /// <inheritdoc />
    public override string DisplayName => "Kimi (API)";

    /// <inheritdoc />
    public override string Description => "查询 Kimi API 的账户余额信息";

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        StandardConfigFields.ApiKey("Kimi"),
        StandardConfigFields.BaseUrl("Kimi", "https://api.moonshot.cn")
    };

    /// <inheritdoc />
    public override IReadOnlyList<BalanceItem> BalanceItems => new[]
    {
        new BalanceItem { Label = "可用余额", Value = "--", Detail = null },
        new BalanceItem { Label = "代金券", Value = "--", Detail = null },
        new BalanceItem { Label = "现金", Value = "--", Detail = null }
    };

    /// <summary>
    /// 查询 Kimi API 的用量信息。
    /// 调用 /v1/users/me/balance 接口获取账户余额。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用量信息</returns>
    public override async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var apiKey = config.GetValue("ApiKey");
        if (ValidateApiKey(apiKey) is { } apiKeyError)
            return apiKeyError;

        if (ValidateBaseUrl(config.GetValue("BaseUrl"), "https://api.moonshot.cn", out var baseUrl) is { } urlError)
            return urlError;

        try
        {
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
    /// 调用 Kimi /v1/users/me/balance 接口查询余额。
    /// </summary>
    /// <param name="baseUrl">API 基础地址</param>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用量信息</returns>
    private async Task<UsageInfo> QueryBalanceAsync(string baseUrl, string apiKey, CancellationToken ct)
    {
        var balanceResponse = await GetJsonAsync<KimiBalanceResponse>(
            baseUrl,
            "/v1/users/me/balance",
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey),
            ct);

        if (balanceResponse == null)
        {
            return CreateError("无法解析API响应");
        }

        if (balanceResponse.Code != 0)
        {
            return CreateError($"API 返回错误: code={balanceResponse.Code}");
        }

        var usageInfo = new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            LastUpdated = DateTime.UtcNow
        };

        if (balanceResponse.Data != null)
        {
            var data = balanceResponse.Data;

            // 解析余额
            var availableBalance = data.AvailableBalance;
            var voucherBalance = data.VoucherBalance;
            var cashBalance = data.CashBalance;

            // 设置总余额（可用余额）
            usageInfo.TotalAmount = availableBalance;
            usageInfo.Unit = "CNY";

            // 存储余额明细到 Extra 供卡片显示
            usageInfo.Extra["available_balance"] = availableBalance.ToString("F5", CultureInfo.InvariantCulture);
            usageInfo.Extra["voucher_balance"] = voucherBalance.ToString("F5", CultureInfo.InvariantCulture);
            usageInfo.Extra["cash_balance"] = cashBalance.ToString("F5", CultureInfo.InvariantCulture);

            // 设置 Quantity（req-086 新字段）
            usageInfo.Quantity = new Quantity(availableBalance, new CurrencyUnit("CNY"));
        }

        return usageInfo;
    }
}

/// <summary>
/// Kimi 余额 API 响应模型。
/// </summary>
internal class KimiBalanceResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public KimiBalanceData? Data { get; set; }

    [JsonPropertyName("scode")]
    public string? Scode { get; set; }

    [JsonPropertyName("status")]
    public bool Status { get; set; }
}

/// <summary>
/// Kimi 余额数据。
/// </summary>
internal class KimiBalanceData
{
    [JsonPropertyName("available_balance")]
    public decimal AvailableBalance { get; set; }

    [JsonPropertyName("voucher_balance")]
    public decimal VoucherBalance { get; set; }

    [JsonPropertyName("cash_balance")]
    public decimal CashBalance { get; set; }
}
