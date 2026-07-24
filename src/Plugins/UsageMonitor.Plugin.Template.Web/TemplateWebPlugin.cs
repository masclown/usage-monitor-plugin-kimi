using Microsoft.Playwright;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Plugin.Template.Web;

/// <summary>
/// req-086-3.5：网页插件模板——展示如何基于 <see cref="WebPluginBase"/> 快速开发一个网页插件。
/// <para>
/// 复制此项目并重命名，修改 <see cref="ProviderId"/> / <see cref="DisplayName"/> /
/// <see cref="LoginUrl"/> / <see cref="UsageUrl"/> / <see cref="CookieDomainFilters"/> /
/// <see cref="ParseUsagePageAsync"/> 即可完成一个新网页插件。
/// </para>
/// </summary>
public class TemplateWebPlugin : WebPluginBase
{
    // ===================== 基本信息（必须修改） =====================

    /// <inheritdoc />
    public override string ProviderId => "template-web";

    /// <inheritdoc />
    public override string DisplayName => "Template Web";

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override string Author => "Your Name";

    /// <inheritdoc />
    public override string Description => "网页插件模板：展示如何基于 WebPluginBase 开发新插件";

    /// <inheritdoc />
    public override string? IconPath => null; // 可选：设置图标路径

    // ===================== 配置字段（按需修改） =====================

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        StandardWebConfigFields.Cookie(ProviderId),
        StandardWebConfigFields.Region(ProviderId, "CN", "CN", "Global"),
        StandardWebConfigFields.AutoRefresh(ProviderId, true),
        StandardWebConfigFields.Proxy(ProviderId),
        StandardWebConfigFields.Headless(ProviderId, false),
    };

    // ===================== 登录配置（必须修改） =====================

    /// <inheritdoc />
    public override BrowserLoginConfig? LoginConfig => new()
    {
        LoginUrl = LoginUrl,
        CookieDomainFilters = CookieDomainFilters,
        ValidateUrl = UsageUrl,
    };

    // ===================== 网页插件抽象属性（必须修改） =====================

    /// <summary>登录入口 URL</summary>
    protected override string LoginUrl => "https://example.com/login";

    /// <summary>用量页面 URL</summary>
    protected override string UsageUrl => "https://example.com/console/usage";

    /// <summary>Cookie 域名过滤列表</summary>
    protected override string[] CookieDomainFilters => new[] { ".example.com" };

    /// <summary>无头模式开关（默认 false，调试用 true）</summary>
    protected override bool Headless => false;

    // ===================== 共享 HttpClient（必须实现） =====================

    /// <summary>共享 HttpClient（用于 API 回退路径）</summary>
    protected override HttpClient Http { get; } = new();

    // ===================== 核心解析逻辑（必须实现） =====================

    /// <summary>
    /// 解析用量页面（子类必须实现）。
    /// <para>
    /// 这是模板方法的核心：在已登录、已导航到用量页面的 <paramref name="page"/> 上，
    /// 提取用量数据并填充到 <see cref="UsageInfo"/>。
    /// </para>
    /// <para>
    /// 推荐使用 <see cref="Services.WebPageParser"/> 提供的 CSS Selector / XPath / Regex 三种提取模式。
    /// </para>
    /// </summary>
    /// <param name="page">已导航到用量页面的 IPage 实例</param>
    /// <returns>解析后的 UsageInfo</returns>
    protected override async Task<UsageInfo> ParseUsagePageAsync(IPage page)
    {
        try
        {
            // 示例：使用 WebPageParser 提取页面数据
            // var parser = new Services.WebPageParser(page);
            // var usedText = await parser.ExtractAsync("css:.usage-used", Services.WebPageParser.ExtractMode.CssSelector);
            // var totalText = await parser.ExtractAsync("css:.usage-total", Services.WebPageParser.ExtractMode.CssSelector);

            // 示例：直接通过 Playwright API 提取
            var usedElement = await page.QuerySelectorAsync(".usage-used");
            var totalElement = await page.QuerySelectorAsync(".usage-total");

            if (usedElement == null || totalElement == null)
            {
                return CreateError("未找到用量数据元素，请检查页面结构或登录态");
            }

            var usedText = await usedElement.InnerTextAsync();
            var totalText = await totalElement.InnerTextAsync();

            // 解析数值（根据实际页面格式调整）
            if (!decimal.TryParse(usedText, out var used) ||
                !decimal.TryParse(totalText, out var total))
            {
                return CreateError($"解析用量数据失败: used={usedText}, total={totalText}");
            }

            // 使用 req-086-3.4 新字段 Quantity 表示用量
            return new UsageInfo
            {
                ProviderId = ProviderId,
                ProviderName = DisplayName,
                IsSuccess = true,
                Quantity = new Quantity(used, new CurrencyUnit("USD")),
                // 兼容旧字段（可选，但建议同时写入以支持旧版主窗口）
                UsedAmount = used,
                TotalAmount = total,
                Unit = "USD",
                LastUpdated = DateTime.Now,
            };
        }
        catch (Exception ex)
        {
            LogError("ParseUsagePageAsync 异常", ex);
            return CreateError($"解析页面异常: {ex.Message}");
        }
    }

    // ===================== 可选：图表注册 =====================

    /// <inheritdoc />
    public override IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring
    };

    // ===================== 可选：生命周期钩子 =====================

    /// <inheritdoc />
    public override async Task InitializeAsync(PluginContext context)
    {
        await base.InitializeAsync(context);
        LogInfo("TemplateWebPlugin 初始化完成");
    }

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        await base.StartAsync();
        LogInfo("TemplateWebPlugin 启动完成");
    }

    /// <inheritdoc />
    public override async Task StopAsync()
    {
        await base.StopAsync();
        LogInfo("TemplateWebPlugin 停止完成");
    }
}
