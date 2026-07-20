using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// HTTP 用量查询 Provider 基类。
/// <para>
/// req-065 B14：抽取 4 个 Provider 的重复样板代码：
/// - HttpClient 实例化与超时配置
/// - URL TrimEnd('/') 拼接
/// - 错误响应体脱敏与截断
/// - JsonSerializerOptions 复用
/// - 统一的异常处理模板
/// </para>
/// </summary>
public abstract class HttpUsageProviderBase : IUsageProvider
{
    /// <summary>
    /// 共享的 JsonSerializerOptions，避免每次新建。
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 错误响应体脱敏正则：匹配 api_key / authorization / token / secret 等敏感字段。
    /// </summary>
    private static readonly Regex SensitivePattern = new(
        @"(api[_-]?key|authorization|token|secret)[""':\s=]+\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 子类提供的 HttpClient 实例（应为 static readonly 以避免 socket 耗尽）。
    /// </summary>
    protected abstract HttpClient Http { get; }

    /// <inheritdoc />
    public abstract string ProviderId { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public virtual string? IconPath => null;

    /// <inheritdoc />
    public virtual string Version => "1.0.0";

    /// <inheritdoc />
    public virtual string Author => "UsageMonitor";

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<ConfigField> ConfigFields { get; }

    /// <inheritdoc />
    public virtual BrowserLoginConfig? LoginConfig => null;

    /// <inheritdoc />
    public virtual IReadOnlyList<string> DefaultRenderKinds => Array.Empty<string>();

    /// <inheritdoc />
    public virtual IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line,
        CardChartKind.Bar,
        CardChartKind.Ring
    };

    /// <inheritdoc />
    public virtual IReadOnlyList<IUsageChartFactory> ChartFactories => Array.Empty<IUsageChartFactory>();

    /// <inheritdoc />
    public abstract Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var result = await GetUsageAsync(config, ct);
        return result.IsSuccess;
    }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent" };

    /// <inheritdoc />
    public virtual bool SupportsPeriodSwitch => false;

    /// <inheritdoc />
    public virtual IReadOnlyList<string>? ExtraTooltipLines => null;

    /// <inheritdoc />
    public virtual Task SetPeriodAsync(string period, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual IReadOnlyList<BalanceItem> BalanceItems => Array.Empty<BalanceItem>();

    /// <inheritdoc />
    public virtual IReadOnlyList<HeatMapTierConfig>? HeatMapTiers => null;

    // ============== REQ-083 SDK v2 新增可选属性的虚默认实现 ==============

    /// <inheritdoc />
    public virtual MetricBarData? CardMetricBarData => null;

    /// <inheritdoc />
    public virtual MetricGridData? CardMetricGridData => null;

    /// <inheritdoc />
    public virtual Func<int, TooltipContent>? LineTooltipProvider => null;

    /// <summary>
    /// 发送 GET 请求并反序列化 JSON 响应。
    /// </summary>
    /// <typeparam name="T">响应模型类型</typeparam>
    /// <param name="baseUrl">API 基础地址</param>
    /// <param name="path">请求路径（以 / 开头）</param>
    /// <param name="configure">可选的请求配置回调（如添加 Authorization 头）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>反序列化后的响应对象，失败时抛出异常</returns>
    protected async Task<T?> GetJsonAsync<T>(
        string baseUrl, string path,
        Action<HttpRequestMessage>? configure = null,
        CancellationToken ct = default) where T : class
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}{path}");
        configure?.Invoke(req);

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"API error {(int)resp.StatusCode}: {SanitizeErrorBody(body)}");
        }
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    /// <summary>
    /// 错误响应体脱敏：移除敏感信息并截断到指定长度。
    /// <para>
    /// req-065 B17：防止错误响应中泄露 Authorization 头、API Key 等敏感信息。
    /// </para>
    /// </summary>
    /// <param name="body">原始错误响应体</param>
    /// <param name="maxLen">最大保留长度，默认 200</param>
    /// <returns>脱敏并截断后的字符串</returns>
    protected static string SanitizeErrorBody(string? body, int maxLen = 200)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        // 脱敏常见敏感模式
        var sanitized = SensitivePattern.Replace(body, "$1=***REDACTED***");
        return Truncate(sanitized, maxLen);
    }

    /// <summary>
    /// 截断字符串到指定长度，超出部分追加 "..."。
    /// </summary>
    protected static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "...";

    /// <summary>
    /// 创建错误 UsageInfo，统一错误格式。
    /// </summary>
    protected UsageInfo CreateError(string message) =>
        UsageInfo.CreateError(ProviderId, DisplayName, message);

    /// <summary>
    /// 验证 ApiKey 是否配置，未配置时返回错误 UsageInfo。
    /// </summary>
    /// <returns>null 表示已配置，否则返回错误 UsageInfo</returns>
    protected UsageInfo? ValidateApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return CreateError("API Key 未配置");
        return null;
    }

    /// <summary>
    /// 验证 BaseUrl 是否合法（SSRF 防护）。
    /// </summary>
    /// <returns>null 表示合法，否则返回错误 UsageInfo</returns>
    protected UsageInfo? ValidateBaseUrl(string? baseUrl, string defaultUrl, out string validBaseUrl)
    {
        validBaseUrl = baseUrl ?? defaultUrl;
        if (!BaseUrlValidator.TryValidate(validBaseUrl, out var urlError))
            return CreateError($"BaseUrl 校验失败: {urlError}");
        return null;
    }
}
