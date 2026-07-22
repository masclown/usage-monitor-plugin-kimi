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

    /// <summary>req-101：运行模式，默认 API 模式；Token Plan 插件（如 MiniMax）override 返回 <see cref="ProviderMode.TokenPlan"/>。</summary>
    public virtual ProviderMode Mode => ProviderMode.Api;

    /// <summary>req-101：订阅档位字段名（Token Plan 模式专用），默认 null。</summary>
    public virtual string? SubscriptionTierField => null;

    /// <summary>req-100：卡片字段映射，默认 null（使用默认字段名）。</summary>
    public virtual FieldMapping? CardFieldMapping => null;

    /// <summary>req-092：将插件原始数据映射为标准字段名字典，默认 null（回退 ExtractStandardFields）。</summary>
    public virtual IReadOnlyDictionary<string, object>? MapToStandardFields(UsageInfo usage) => null;

    /// <inheritdoc />
    public abstract string Author { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<ConfigField> ConfigFields { get; }

    /// <summary>
    /// LoginConfig 懒构造缓存（首次访问时根据 <see cref="LoginUrl"/>/<see cref="UsageUrl"/>/
    /// <see cref="CookieDomainFilters"/> 自动构建通用 BrowserLoginConfig）。
    /// 子类若需更精细配置（如 MiniMax 的 RequiredCookieDomain / LoggedInPathKeywords）可 override 此属性。
    /// </summary>
    private BrowserLoginConfig? _cachedLoginConfig;

    /// <inheritdoc />
    public virtual BrowserLoginConfig? LoginConfig
    {
        get
        {
            // 懒加载：首次访问时基于已声明的 LoginUrl/UsageUrl/CookieDomainFilters 构建默认 LoginConfig。
            // 这样所有继承自 WebPluginBase 的插件（Deepseek/Kimi/Qoder 等）无需各自 override，
            // 设置界面即可自动显示"🌐 获取登录态"按钮。
            // 子类若有更精细需求（如 MiniMax 的 RequiredCookieDomain / LoggedInPathKeywords）可 override 此属性。
            if (_cachedLoginConfig == null && !string.IsNullOrEmpty(LoginUrl))
            {
                _cachedLoginConfig = BuildDefaultLoginConfig();
            }
            return _cachedLoginConfig;
        }
    }

    /// <summary>
    /// 基于已声明的 <see cref="LoginUrl"/>/<see cref="UsageUrl"/>/<see cref="CookieDomainFilters"/>
    /// 构建通用 BrowserLoginConfig。子类如有特殊登录判定需求可 override <see cref="LoginConfig"/>。
    /// <para>
    /// 默认配置足以覆盖绝大多数 web 插件：登录后导航到 <see cref="UsageUrl"/>，
    /// 等待 <see cref="CookieDomainFilters"/> 任一域名下的会话 Cookie 出现即视为登录成功。
    /// </para>
    /// </summary>
    private BrowserLoginConfig BuildDefaultLoginConfig()
    {
        return new BrowserLoginConfig
        {
            ProviderId = ProviderId,
            LoginUrl = LoginUrl,
            CookieDomainFilters = CookieDomainFilters,
            // 默认登录验证页：登录后跳转到用量页面（浏览器自动从 LoginUrl 跟随 redirect）
            ValidateUrl = UsageUrl,
            // 按钮文字：默认通用文案，子类可覆盖为更精确描述（如 MiniMax 的 "Get MiniMax login state"）
            UiButtonText = "🌐 获取登录态",
            // 登录等待超时：2 分钟，与 MiniMax 默认值对齐
            LoginTimeout = TimeSpan.FromMinutes(2),
            // 通用登录页关键字：覆盖绝大多数 web 插件的登录路径
            // （login/oauth/auth/signin/signup/register/unified-login/passport）
            LoginUrlKeywords = new[] { "login", "unified-login", "signin", "sign-in", "signup", "register", "auth", "passport", "oauth" }
        };
    }

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
    /// <remarks>req-098：在 WebPluginBase 中提供 virtual 实现供子类 override，接口 default 实现作为兑底。</remarks>
    public virtual IReadOnlyList<UsageMonitor.Core.Plugins.MiniChart.MiniChartKind> SupportedMiniCharts => new[]
    {
        UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniRingChart,
        UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniText
    };

    /// <inheritdoc />
    /// <remarks>req-098：在 WebPluginBase 中提供 virtual 实现供子类 override。</remarks>
    public virtual IReadOnlyList<UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind> MiniChartDataTypes => new[]
    {
        UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.PrimaryMetric,
        UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.Credits,
        UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.ResetTime
    };

    /// <inheritdoc />
    /// <remarks>req-105：在 WebPluginBase 中提供 virtual 实现供子类 override。</remarks>
    public virtual IReadOnlyList<string> ToolTipFields => new[]
    {
        "ProviderName",
        "DataName",
        "CurrentValue",
        "RefreshCountdown"
    };

    /// <inheritdoc />
    public virtual IReadOnlyList<BalanceItem> BalanceItems => Array.Empty<BalanceItem>();

    /// <inheritdoc />
    public virtual IReadOnlyList<HeatMapTierConfig>? HeatMapTiers => null;

    /// <summary>req-099/bug5：最后一次成功查询的用量（供 V2 卡片数据构建）。</summary>
    protected UsageInfo? LastUsage { get; private set; }

    /// <inheritdoc />
    /// <remarks>req-099/bug5：默认从 <see cref="LastUsage"/> 走子类 <see cref="BuildCardMetricBarData"/> 构建；
    /// 子类只需 override 构建方法即可让卡片在新框架下渲染 V2 进度条。</remarks>
    public virtual MetricBarData? CardMetricBarData
        => LastUsage == null ? null : BuildCardMetricBarData(LastUsage);

    /// <inheritdoc />
    /// <remarks>req-099/bug5：默认从 <see cref="LastUsage"/> 走子类 <see cref="BuildCardMetricGridData"/> 构建。</remarks>
    public virtual MetricGridData? CardMetricGridData
        => LastUsage == null ? null : BuildCardMetricGridData(LastUsage);

    /// <summary>req-099/bug5：子类将自身抓取数据映射为“度量进度条组” V2 模型（默认 null=沿用旧模板）。</summary>
    /// <param name="usage">最后一次成功用量。</param>
    protected virtual MetricBarData? BuildCardMetricBarData(UsageInfo usage) => null;

    /// <summary>req-099/bug5：子类将自身抓取数据映射为“度量数字网格” V2 模型（默认 null=沿用旧模板）。</summary>
    /// <param name="usage">最后一次成功用量。</param>
    protected virtual MetricGridData? BuildCardMetricGridData(UsageInfo usage) => null;

    // req-099/bug5：Extra 值为 object 装箱，提供类型容错读取助手供子类构建 V2 模型。
    /// <summary>从 Extra 容错读取 double（失败返回 fallback）。</summary>
    protected static double ReadExtraDouble(UsageInfo usage, string key, double fallback = 0)
        => usage.Extra != null && usage.Extra.TryGetValue(key, out var v) && v != null
           && double.TryParse(System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
               System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)
           ? d : fallback;

    /// <summary>从 Extra 容错读取 long（失败返回 fallback）。</summary>
    protected static long ReadExtraLong(UsageInfo usage, string key, long fallback = 0)
        => usage.Extra != null && usage.Extra.TryGetValue(key, out var v) && v != null
           && long.TryParse(System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), out var n)
           ? n : fallback;

    /// <summary>从 Extra 读取字符串（缺失返回空串）。</summary>
    protected static string ReadExtraString(UsageInfo usage, string key)
        => usage.Extra != null && usage.Extra.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    /// <inheritdoc />
    public virtual Func<int, TooltipContent>? LineTooltipProvider => null;

    /// <inheritdoc />
    /// <remarks>
    /// req-fix-MiniMaxCollapseVisibleParts 修复：在基类提供 <c>virtual</c> 默认实现（<c>null</c>），
    /// 让派生类能用 <c>override</c> 关键字（避免 <c>new</c> 隐藏接口默认方法导致多态失效）。
    /// </remarks>
    public virtual IReadOnlyList<string>? CollapseVisibleParts => null;

    // ============== 抽象属性：子类必须声明 ==============

    /// <summary>登录入口 URL（如 https://platform.minimaxi.com）</summary>
    protected abstract string LoginUrl { get; }

    /// <summary>用量页面 URL（如 https://platform.minimaxi.com/console/usage）</summary>
    protected abstract string UsageUrl { get; }

    /// <summary>Cookie 域名过滤列表（用于判定登录态）</summary>
    protected abstract string[] CookieDomainFilters { get; }

    /// <summary>
    /// 无头模式开关（默认 <c>true</c>，与 MiniMax 一致，避免刷新时弹出浏览器窗口打扰用户）。
    /// <para>
    /// req-fix-启动时弹空白页：主程序启动后定时刷新器会立即触发一次 <see cref="GetUsageAsync"/>。
    /// 如果 <c>Headless=false</c>，Playwright 会弹出可见 Edge 窗口显示 about:blank 空白页。
    /// 默认开启 Headless 后，浏览器仅在后台运行，用户体验与 MiniMax 一致。
    /// </para>
    /// <para>
    /// 调试用可在子类中 override 为 <c>false</c>，或通过 ConfigField 中的 <c>Headless</c> 配置项临时调整。
    /// </para>
    /// </summary>
    protected virtual bool Headless => true;

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
    /// <para>
    /// req-fix-启动时弹空白页：在启动 Playwright Edge 之前先检查 Cookie 状态，
    /// 缺失时直接返回错误，避免主程序启动时弹出 about:blank 空白窗口打扰用户。
    /// </para>
    /// </summary>
    public virtual async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        try
        {
            LogInfo("GetUsageAsync 开始");

            // req-fix-启动时弹空白页：Cookie 缺失时跳过浏览器启动，直接返回明确错误信息。
            // 这避免了主程序启动后定时刷新器立即触发 → GetUsageAsync → 启动 Playwright Edge
            // → 新建 page（默认 about:blank）→ EnsureLoginAsync 才发现无 Cookie 的链路浪费。
            if (!HasValidCookie(config))
            {
                LogInfo("Cookie 未配置，跳过浏览器启动");
                return CreateError("未配置 Cookie，请在插件设置中点击「🌐 获取登录态」按钮完成登录");
            }

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
            // req-099/bug5：缓存最后一次成功用量，供 CardMetricBarData/CardMetricGridData 构建 V2 卡片数据。
            if (result.IsSuccess)
            {
                LastUsage = result;
                // req-005-011：Web 插件输出统一补写强类型 Quantity（由 UsedAmount+Unit 派生，零回归过渡）。
                result.PopulateQuantityFromLegacy();
            }
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
    /// req-fix-启动时弹空白页：检查插件是否有可用 Cookie（从 config 或 cookies/*.json 任一来源）。
    /// 返回 <c>true</c> 时可安全启动浏览器；返回 <c>false</c> 时应跳过浏览器启动直接返回错误，
    /// 避免弹出 about:blank 空白窗口。
    /// </summary>
    private bool HasValidCookie(ProviderConfig config)
    {
        var cookie = config.GetValue("Cookie")?.Trim();
        if (!string.IsNullOrWhiteSpace(cookie)) return true;

        try
        {
            var saved = Services.BrowserLoginService.LoadCookieData(ProviderId);
            return saved != null && !string.IsNullOrWhiteSpace(saved.Cookie);
        }
        catch
        {
            return false;
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
