using Microsoft.Playwright;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-086：网页插件基类，封装 Playwright 浏览器生命周期。
/// <para>
/// 模板方法流程：<see cref="GetUsageAsync"/> → <see cref="GetOrCreatePageAsync"/> →
/// <see cref="EnsureLoginAsync"/> → <see cref="NavigateToUsagePageAsync"/> →
/// <see cref="ParseUsagePageAsync"/>。
/// </para>
/// <para>
/// 子类只需实现抽象属性（LoginUrl / UsageUrl / CookieDomainFilters）和抽象方法
/// <see cref="ParseUsagePageAsync"/>，即可复用完整的浏览器登录态管理、页面导航和异常隔离。
/// </para>
/// </summary>
public abstract class WebPluginBase : PluginBase, IUsageProvider
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _browserContext;
    private IPage? _page;
    private bool _disposed;

    /// <summary>共享 HttpClient（用于 API 回退路径）</summary>
    protected abstract HttpClient Http { get; }

    /// <inheritdoc />
    public abstract string Version { get; }

    /// <inheritdoc />
    public virtual string? IconPath => null;

    /// <inheritdoc />
    public abstract string Author { get; }

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
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring
    };

    /// <inheritdoc />
    public virtual IReadOnlyList<IUsageChartFactory> ChartFactories => Array.Empty<IUsageChartFactory>();

    /// <inheritdoc />
    public virtual IReadOnlyList<IUsageChartFactory2>? CustomChartFactories => null;

    /// <inheritdoc />
    public virtual IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent" };

    /// <inheritdoc />
    public virtual bool SupportsPeriodSwitch => false;

    /// <inheritdoc />
    public virtual IReadOnlyList<string>? ExtraTooltipLines => null;

    /// <inheritdoc />
    public virtual IReadOnlyList<BalanceItem> BalanceItems => Array.Empty<BalanceItem>();

    /// <inheritdoc />
    public virtual IReadOnlyList<HeatMapTierConfig>? HeatMapTiers => null;

    /// <inheritdoc />
    public virtual MetricBarData? CardMetricBarData => null;

    /// <inheritdoc />
    public virtual MetricGridData? CardMetricGridData => null;

    /// <inheritdoc />
    public virtual Func<int, TooltipContent>? LineTooltipProvider => null;

    // ============== 抽象属性：子类必须声明 ==============

    /// <summary>登录入口 URL（如 https://platform.minimaxi.com）</summary>
    protected abstract string LoginUrl { get; }

    /// <summary>用量页面 URL（如 https://platform.minimaxi.com/console/usage）</summary>
    protected abstract string UsageUrl { get; }

    /// <summary>Cookie 域名过滤列表（用于判定登录态）</summary>
    protected abstract string[] CookieDomainFilters { get; }

    /// <summary>无头模式开关（默认 false，调试用 true）</summary>
    protected virtual bool Headless => false;

    /// <summary>页面加载超时（默认 60 秒）</summary>
    protected virtual TimeSpan PageTimeout => TimeSpan.FromSeconds(60);

    // ============== 模板方法 ==============

    /// <summary>
    /// 查询用量信息（模板方法入口）。
    /// <para>
    /// 子类可 override 此方法以完全自定义查询逻辑（如 MiniMax 使用独立 DOM 提取器）。
    /// 默认实现按模板方法流程执行：GetOrCreatePageAsync → EnsureLoginAsync →
    /// NavigateToUsagePageAsync → ParseUsagePageAsync。
    /// </para>
    /// </summary>
    public virtual async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        try
        {
            LogInfo("GetUsageAsync 开始");
            var page = await GetOrCreatePageAsync(ct);
            if (page == null)
            {
                return CreateError("无法创建浏览器页面");
            }

            if (!await EnsureLoginAsync(page, config, ct))
            {
                return CreateError("登录态无效，请重新获取登录态");
            }

            if (!await NavigateToUsagePageAsync(page, ct))
            {
                return CreateError("导航到用量页面失败");
            }

            var result = await ParseUsagePageAsync(page);
            LogInfo($"GetUsageAsync 完成: IsSuccess={result.IsSuccess}");
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CreateError("用户取消");
        }
        catch (Exception ex)
        {
            LogError("GetUsageAsync 异常", ex);
            return CreateError(ex.Message);
        }
    }

    /// <summary>
    /// 获取或创建浏览器页面（懒初始化）。
    /// </summary>
    protected virtual async Task<IPage?> GetOrCreatePageAsync(CancellationToken ct)
    {
        if (_page != null && !_page.IsClosed)
        {
            return _page;
        }

        try
        {
            _playwright ??= await Playwright.CreateAsync();
            _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "msedge",
                Headless = Headless,
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-sync",
                    "--no-first-run",
                    "--no-default-browser-check",
                }
            });

            _browserContext ??= await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                Locale = "zh-CN",
                TimezoneId = "Asia/Shanghai",
            });

            _page = await _browserContext.NewPageAsync();
            _page.SetDefaultTimeout((float)PageTimeout.TotalMilliseconds);
            return _page;
        }
        catch (Exception ex)
        {
            LogError("创建浏览器页面失败", ex);
            return null;
        }
    }

    /// <summary>
    /// 确保登录态有效（加载 Cookie 到浏览器上下文）。
    /// </summary>
    protected virtual async Task<bool> EnsureLoginAsync(IPage page, ProviderConfig config, CancellationToken ct)
    {
        // 从 config 或 cookies/*.json 恢复 Cookie
        var cookie = config.GetValue("Cookie")?.Trim();
        if (string.IsNullOrWhiteSpace(cookie))
        {
            var saved = Services.BrowserLoginService.LoadCookieData(ProviderId);
            if (saved != null && !string.IsNullOrWhiteSpace(saved.Cookie))
            {
                cookie = saved.Cookie.Trim();
                config.SetValue("Cookie", cookie);
                LogInfo("从 cookies/*.json 恢复 Cookie");
            }
        }

        if (string.IsNullOrWhiteSpace(cookie))
        {
            LogWarn("无可用 Cookie");
            return false;
        }

        // 将 Cookie 字符串注入浏览器上下文
        try
        {
            var cookies = ParseCookieString(cookie);
            await page.Context.AddCookiesAsync(cookies);
            LogInfo($"注入 {cookies.Count} 条 Cookie");
            return true;
        }
        catch (Exception ex)
        {
            LogError("注入 Cookie 失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 导航到用量页面。
    /// </summary>
    protected virtual async Task<bool> NavigateToUsagePageAsync(IPage page, CancellationToken ct)
    {
        try
        {
            LogInfo($"导航到 {UsageUrl}");
            await page.GotoAsync(UsageUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)PageTimeout.TotalMilliseconds
            });
            return true;
        }
        catch (Exception ex)
        {
            LogError($"导航失败: {UsageUrl}", ex);
            return false;
        }
    }

    /// <summary>
    /// 解析用量页面（子类必须实现）。
    /// </summary>
    /// <param name="page">已导航到用量页面的 IPage 实例</param>
    /// <returns>解析后的 UsageInfo</returns>
    protected abstract Task<UsageInfo> ParseUsagePageAsync(IPage page);

    /// <inheritdoc />
    public virtual async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var result = await GetUsageAsync(config, ct);
        return result.IsSuccess;
    }

    /// <inheritdoc />
    public virtual Task SetPeriodAsync(string period, CancellationToken ct = default)
        => Task.CompletedTask;

    // ============== 辅助方法 ==============

    /// <summary>
    /// 将 Cookie 字符串解析为 Playwright Cookie 列表。
    /// </summary>
    protected virtual List<Cookie> ParseCookieString(string cookieString)
    {
        var cookies = new List<Cookie>();
        foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx > 0)
            {
                var name = part.Substring(0, eqIdx).Trim();
                var value = part.Substring(eqIdx + 1).Trim();
                // 为每个域名过滤器和根域名创建 Cookie
                foreach (var domain in CookieDomainFilters)
                {
                    cookies.Add(new Cookie
                    {
                        Name = name,
                        Value = value,
                        Domain = domain,
                        Path = "/",
                    });
                }
            }
        }
        return cookies;
    }

    /// <summary>
    /// 创建错误 UsageInfo。
    /// </summary>
    protected UsageInfo CreateError(string message) =>
        UsageInfo.CreateError(ProviderId, DisplayName, message);

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_page != null && !_page.IsClosed)
            {
                await _page.CloseAsync();
            }
            if (_browserContext != null)
            {
                await _browserContext.CloseAsync();
            }
            if (_browser != null)
            {
                await _browser.CloseAsync();
            }
            _playwright?.Dispose();
        }
        catch (Exception ex)
        {
            LogError("释放浏览器资源失败", ex);
        }

        await base.DisposeAsync();
    }
}
