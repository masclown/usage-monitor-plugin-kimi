using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Kimi;

/// <summary>
/// req-085：Kimi 双模式插件入口。
/// 根据配置自动选择 API 模式或网页模式进行用量查询。
/// </summary>
public class KimiDualModeProvider : IUsageProvider
{
    private readonly KimiApiProvider _apiProvider = new();
    private readonly KimiWebProvider _webProvider = new();

    /// <inheritdoc />
    public string ProviderId => "kimi";

    /// <inheritdoc />
    public string DisplayName => "Kimi";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "UsageMonitor";

    /// <inheritdoc />
    public string Description => "Kimi 用量查询（支持 API 模式和网页模式）";

    /// <inheritdoc />
    public string? IconPath => null;

    /// <summary>req-108 Task3：Kimi 卡片显示声明根——从随 DLL 的 defaults.json 懒装载（经 PluginDefaultsLoader）。</summary>
    private UsageMonitor.Core.Models.PluginManifest? _manifest;
    private bool _manifestLoaded;
    private UsageMonitor.Core.Models.PluginManifest? Manifest
    {
        get
        {
            if (!_manifestLoaded)
            {
                _manifest = UsageMonitor.Core.Services.PluginDefaultsLoader
                    .LoadFromAssemblyDirectory(typeof(KimiDualModeProvider).Assembly.Location);
                _manifestLoaded = true;
            }
            return _manifest;
        }
    }
    public UsageMonitor.Core.Models.CardDeclaration? Card => Manifest?.Card;
    public UsageMonitor.Core.Models.TaskbarDeclaration? Taskbar => Manifest?.Taskbar;

    /// <summary>
    /// req-fix-Kimi ConfigFields 按模式动态返回字段。
    /// <para>
    /// 原实现静态合并 API 模式（ApiKey/BaseUrl）和 Web 模式（Cookie/Region/AutoRefresh）的所有字段，
    /// 导致 Web 模式下用户用 Cookie 登录后保存时，ApiKey 必填校验失败。
    /// </para>
    /// <para>改为根据当前 config 的 QueryMode 字段动态返回：</para>
    /// <list type="bullet">
    ///   <item><description>api 模式：Mode + ApiKey + BaseUrl</description></item>
    ///   <item><description>web 模式：Mode + Cookie + Region + AutoRefresh</description></item>
    /// </list>
    /// <para>注意：<see cref="IUsageProvider.ConfigFields"/> 是无参属性，配置切换时通过 ConfigService
    /// 持久化触发下次读取。运行时切换需要重新打开 PluginConfigWindow 才能看到对应字段。</para>
    /// </summary>
    public IReadOnlyList<ConfigField> ConfigFields
    {
        get
        {
            var mode = KimiConfig.GetQueryMode(_currentConfigSnapshot);
            var fields = new List<ConfigField>
            {
                // 模式选择字段（始终显示，便于切换模式）
                KimiConfig.CreateModeField(ProviderId)
            };

            if (mode == KimiConfig.ModeWeb)
            {
                // Web 模式：仅显示 Cookie + Region + AutoRefresh（无 ApiKey）
                fields.Add(StandardWebConfigFields.Cookie(ProviderId));
                fields.Add(StandardWebConfigFields.Region(ProviderId, "CN", "CN", "Global"));
                fields.Add(StandardWebConfigFields.AutoRefresh(ProviderId, true));
            }
            else
            {
                // API 模式：仅显示 ApiKey + BaseUrl
                fields.AddRange(_apiProvider.ConfigFields);
            }

            return fields;
        }
    }

    /// <summary>
    /// req-fix-Kimi ConfigFields 动态返回所需快照：MainViewModel 在装配时通过
    /// <see cref="SetCurrentConfigSnapshot"/> 注入当前 config。
    /// <para>未注入时（默认）按 API 模式返回字段（兜底）。</para>
    /// </summary>
    private ProviderConfig? _currentConfigSnapshot;

    /// <summary>
    /// req-fix-Kimi ConfigFields 动态返回所需 setter：MainViewModel 在装配 PluginItemViewModel
    /// 时调用，传入当前 config 快照供 ConfigFields getter 按模式返回字段。
    /// <para>建议仅在 MainViewModel 装配 plugin 列表时调用一次，运行时切换模式需重新调用。</para>
    /// </summary>
    public void SetCurrentConfigSnapshot(ProviderConfig? config)
    {
        _currentConfigSnapshot = config;
    }

    /// <inheritdoc />
    public BrowserLoginConfig? LoginConfig => _webProvider.LoginConfig;

    /// <inheritdoc />
    public IReadOnlyList<string> DefaultRenderKinds => _webProvider.DefaultRenderKinds;

    /// <inheritdoc />
    IReadOnlyList<string>? IDefaultRenderKindsProvider.CollapseVisibleParts => _webProvider.CollapseVisibleParts;

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

    // ============== req-100/101/092 契约对齐 ==============

    /// <summary>
    /// req-092 B4：Kimi 标准字段映射——提取通用标准字段 + 保留查询模式（api/web），
    /// 供字段级差异增量持久化。
    /// </summary>
    public IReadOnlyDictionary<string, object>? MapToStandardFields(UsageInfo usage)
    {
        if (usage == null) return null;
        var f = new Dictionary<string, object>
        {
            [UsageFields.UsedPercent] = usage.GetUsagePercentage(),
            [UsageFields.IsSuccess] = usage.IsSuccess,
            [UsageFields.LastUpdated] = usage.LastUpdated
        };
        if (usage.Extra != null && usage.Extra.TryGetValue("query_mode", out var qm) && qm != null)
            f["kimi_query_mode"] = qm;
        return f;
    }

    /// <inheritdoc />
    public MetricBarData? CardMetricBarData => _webProvider.CardMetricBarData;

    /// <inheritdoc />
    public MetricGridData? CardMetricGridData => _webProvider.CardMetricGridData;

    /// <inheritdoc />
    public Func<int, TooltipContent>? LineTooltipProvider => _webProvider.LineTooltipProvider;

    /// <summary>
    /// 根据配置模式查询用量信息。
    /// API 模式：调用 Moonshot API 查询余额。
    /// 网页模式：通过浏览器获取平台数据（支持图表）。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用量信息</returns>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var mode = KimiConfig.GetQueryMode(config);

        return mode switch
        {
            KimiConfig.ModeWeb => await QueryWebModeAsync(config, ct),
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
        var mode = KimiConfig.GetQueryMode(config);

        return mode switch
        {
            KimiConfig.ModeWeb => await _webProvider.ValidateConfigAsync(config, ct),
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
