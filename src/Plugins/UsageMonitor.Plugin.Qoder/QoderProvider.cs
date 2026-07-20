using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Qoder;

/// <summary>
/// req-087-B1：Qoder 插件入口。
/// 纯网页模式插件，通过 DOM 抓取 Credits 用量数据。
/// </summary>
public class QoderProvider : IUsageProvider
{
    private readonly QoderWebProvider _webProvider = new();

    /// <inheritdoc />
    public string ProviderId => "qoder";

    /// <inheritdoc />
    public string DisplayName => "Qoder";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "UsageMonitor";

    /// <inheritdoc />
    public string Description => "Qoder Credits 用量监控（网页模式）";

    /// <inheritdoc />
    public string? IconPath => null;

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields => _webProvider.ConfigFields;

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
    public IReadOnlyList<BalanceItem> BalanceItems => _webProvider.BalanceItems;

    /// <inheritdoc />
    public IReadOnlyList<HeatMapTierConfig>? HeatMapTiers => _webProvider.HeatMapTiers;

    /// <inheritdoc />
    public MetricBarData? CardMetricBarData => _webProvider.CardMetricBarData;

    /// <inheritdoc />
    public MetricGridData? CardMetricGridData => _webProvider.CardMetricGridData;

    /// <inheritdoc />
    public Func<int, TooltipContent>? LineTooltipProvider => _webProvider.LineTooltipProvider;

    /// <summary>
    /// 查询用量信息（委托给网页模式）。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用量信息</returns>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var result = await _webProvider.GetUsageAsync(config, ct);
        result.Extra["query_mode"] = "web";
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        return await _webProvider.ValidateConfigAsync(config, ct);
    }

    /// <inheritdoc />
    public async Task SetPeriodAsync(string period, CancellationToken ct = default)
    {
        await _webProvider.SetPeriodAsync(period, ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_webProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
