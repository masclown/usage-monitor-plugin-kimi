using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Deepseek;

/// <summary>
/// req-084：DeepSeek 双模式插件入口。
/// 根据配置自动选择 API 模式或网页模式进行用量查询。
/// </summary>
public class DeepseekDualModeProvider : IUsageProvider
{
    private readonly DeepseekProvider _apiProvider = new();
    private readonly DeepseekWebProvider _webProvider = new();

    /// <inheritdoc />
    public string ProviderId => "deepseek";

    /// <inheritdoc />
    public string DisplayName => "DeepSeek";

    /// <inheritdoc />
    public string Version => "2.0.0";

    /// <inheritdoc />
    public string Author => "UsageMonitor";

    /// <inheritdoc />
    public string Description => "DeepSeek 用量查询（支持 API 模式和网页模式）";

    /// <inheritdoc />
    public string? IconPath => _apiProvider.IconPath;

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields
    {
        get
        {
            var fields = new List<ConfigField>
            {
                // 模式选择字段
                DeepseekConfig.CreateModeField(ProviderId)
            };

            // 添加 API 模式字段
            fields.AddRange(_apiProvider.ConfigFields);

            // 添加网页模式字段（Cookie 等）
            fields.Add(StandardWebConfigFields.Cookie(ProviderId));
            fields.Add(StandardWebConfigFields.Region(ProviderId, "CN", "CN", "Global"));
            fields.Add(StandardWebConfigFields.AutoRefresh(ProviderId, true));

            return fields;
        }
    }

    /// <inheritdoc />
    public BrowserLoginConfig? LoginConfig => _webProvider.LoginConfig;

    /// <inheritdoc />
    public IReadOnlyList<string> DefaultRenderKinds => _webProvider.DefaultRenderKinds;

    /// <inheritdoc />
    public IReadOnlyList<CardChartKind> SupportedCardCharts => _webProvider.SupportedCardCharts;

    /// <inheritdoc />
    public IReadOnlyList<IUsageChartFactory> ChartFactories => _webProvider.ChartFactories;

    /// <inheritdoc />
    public IReadOnlyList<IUsageChartFactory2>? CustomChartFactories => _webProvider.CustomChartFactories;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedRingChartMetrics => _webProvider.SupportedRingChartMetrics;

    /// <inheritdoc />
    public bool SupportsPeriodSwitch => _webProvider.SupportsPeriodSwitch;

    /// <inheritdoc />
    public IReadOnlyList<string>? ExtraTooltipLines => _webProvider.ExtraTooltipLines;

    /// <inheritdoc />
    public IReadOnlyList<BalanceItem> BalanceItems => _apiProvider.BalanceItems;

    /// <inheritdoc />
    public IReadOnlyList<HeatMapTierConfig>? HeatMapTiers => _webProvider.HeatMapTiers;

    /// <inheritdoc />
    public MetricBarData? CardMetricBarData => _webProvider.CardMetricBarData;

    /// <inheritdoc />
    public MetricGridData? CardMetricGridData => _webProvider.CardMetricGridData;

    /// <inheritdoc />
    public Func<int, TooltipContent>? LineTooltipProvider => _webProvider.LineTooltipProvider;

    /// <summary>
    /// 根据配置模式查询用量信息。
    /// API 模式：调用 DeepSeek API 查询余额。
    /// 网页模式：通过浏览器获取平台数据（支持图表）。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用量信息</returns>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var mode = DeepseekConfig.GetQueryMode(config);

        return mode switch
        {
            DeepseekConfig.ModeWeb => await QueryWebModeAsync(config, ct),
            _ => await QueryApiModeAsync(config, ct)
        };
    }

    /// <summary>
    /// API 模式查询。
    /// </summary>
    private async Task<UsageInfo> QueryApiModeAsync(ProviderConfig config, CancellationToken ct)
    {
        var result = await _apiProvider.GetUsageAsync(config, ct);
        result.Extra["query_mode"] = "api";
        return result;
    }

    /// <summary>
    /// 网页模式查询。
    /// </summary>
    private async Task<UsageInfo> QueryWebModeAsync(ProviderConfig config, CancellationToken ct)
    {
        var result = await _webProvider.GetUsageAsync(config, ct);
        result.Extra["query_mode"] = "web";

        // 网页模式失败时，提示可切换到 API 模式
        if (!result.IsSuccess)
        {
            result.Extra["fallback_hint"] = "网页模式查询失败，可尝试切换到 API 模式";
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var mode = DeepseekConfig.GetQueryMode(config);

        return mode switch
        {
            DeepseekConfig.ModeWeb => await _webProvider.ValidateConfigAsync(config, ct),
            _ => await _apiProvider.ValidateConfigAsync(config, ct)
        };
    }

    /// <inheritdoc />
    public async Task SetPeriodAsync(string period, CancellationToken ct = default)
    {
        await _webProvider.SetPeriodAsync(period, ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_apiProvider is IAsyncDisposable apiDisposable)
            await apiDisposable.DisposeAsync();
        if (_webProvider is IAsyncDisposable webDisposable)
            await webDisposable.DisposeAsync();
    }
}
