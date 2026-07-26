using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;
using UsageMonitor.Core.Security;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// Stage E（spec P4）：纯声明包通用运行器——实现 <see cref="IUsageProvider"/> 的全部成员，
/// 数据完全由 <see cref="PluginManifest"/>（plugin.json / fetch.json / display.json / defaults.json 合并）驱动。
/// <para>取数链路：BrowserCaptureService（capture 模式端点）+ DeclarativeHttpFetcher（http 模式端点）
/// → DeclarativeCaptureExecutor 声明映射 → UsageInfo。新 Provider 只需编写声明包（零 DLL、零 C#）即可接入，
/// 与内置时代的手写插件（如 MiniMaxProvider）功能等价。</para>
/// </summary>
public sealed class DeclarativeProvider : IUsageProvider
{
    private readonly PluginManifest _manifest;
    private readonly string _pluginDirectory;

    /// <summary>凭据允许域集合缓存（首次取数时从清单推导，供凭据域名同源约束）。</summary>
    private IReadOnlyCollection<string>? _credentialDomains;

    /// <summary>
    /// 创建声明包运行器实例。
    /// </summary>
    /// <param name="manifest">已通过 PluginValidator 校验的合并清单（providerId 必填）。</param>
    /// <param name="pluginDirectory">声明包所在目录（供日志/诊断，允许为空）。</param>
    public DeclarativeProvider(PluginManifest manifest, string pluginDirectory = "")
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifest.ProviderId))
            throw new ArgumentException("声明包缺少 providerId", nameof(manifest));
        _pluginDirectory = pluginDirectory ?? string.Empty;

        // 登录声明归一：ProviderId 缺省时补齐（声明包内可省略，避免与顶层 providerId 重复维护）。
        if (_manifest.LoginConfig != null && string.IsNullOrWhiteSpace(_manifest.LoginConfig.ProviderId))
            _manifest.LoginConfig.ProviderId = manifest.ProviderId!;
    }

    /// <summary>声明包所在目录（供宿主诊断显示）。</summary>
    public string PluginDirectory => _pluginDirectory;

    /// <inheritdoc />
    public string ProviderId => _manifest.ProviderId!;

    /// <inheritdoc />
    public string DisplayName => _manifest.DisplayName ?? _manifest.ProviderId!;

    /// <inheritdoc />
    /// <remarks>声明包不随包分发图标；宿主 ProviderIconService 按 LoginConfig 域名抓取 favicon 缓存。</remarks>
    public string? IconPath => null;

    /// <inheritdoc />
    public string Version => _manifest.Meta?.Version ?? "1.0.0";

    /// <inheritdoc />
    public string Author => _manifest.Meta?.Author ?? string.Empty;

    /// <inheritdoc />
    public string Description => _manifest.Meta?.Description ?? string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields => _manifest.ConfigFields;

    /// <inheritdoc />
    /// <remarks>来自声明包 loginConfig 节（BrowserLoginConfig 全量属性均为纯数据）。</remarks>
    public BrowserLoginConfig? LoginConfig => _manifest.LoginConfig;

    /// <inheritdoc />
    public IReadOnlyList<ErrorGuidanceRule> ErrorGuidance => _manifest.ErrorGuidance;

    /// <inheritdoc />
    public CardDeclaration? Card => _manifest.Card;

    /// <inheritdoc />
    public TaskbarDeclaration? Taskbar => _manifest.Taskbar;

    /// <summary>刷新策略——来自声明包 refresh 节（缺省 = 使用全局刷新间隔）。</summary>
    public RefreshPolicy? RefreshPolicy => _manifest.Refresh;

    /// <summary>折叠态仍可见部件——来自声明包 card.collapseVisibleParts（空集合归一为 null，沿用宿主默认）。</summary>
    public IReadOnlyList<string>? CollapseVisibleParts
        => _manifest.Card?.CollapseVisibleParts is { Count: > 0 } parts ? parts : null;

    /// <summary>
    /// 查询用量（声明驱动）：capture 端点走通用浏览器抓取，http 端点走声明式直连；
    /// 两路响应合并后由 <see cref="DeclarativeCaptureExecutor"/> 统一映射为 extras，再构造 UsageInfo。
    /// </summary>
    /// <param name="config">Provider 配置（Cookie / ApiKey / Region 等声明字段）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var fetch = _manifest.Fetch;
        if (fetch == null)
            return CreateError("插件声明包缺少 fetch 节，无法取数", UsageErrorCodes.ConfigMissing);

        // Cookie 自愈：config 缺失时回退已保存登录态并回填内存配置。
        // req-110 P2-2：按 _accountId 提示键优先读账号级 cookie 文件（cookies/{Provider}.{Account}.json），缺失回退 Provider 级。
        var cookie = config.GetValue("Cookie")?.Trim();
        var userAgent = config.GetValue("_userAgent");
        if (string.IsNullOrWhiteSpace(cookie))
        {
            try
            {
                var saved = BrowserLoginService.LoadCookieData(ProviderId, config.GetValue("_accountId"));
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Cookie))
                {
                    cookie = saved.Cookie.Trim();
                    if (string.IsNullOrWhiteSpace(userAgent)) userAgent = saved.UserAgent;
                    config.SetValue("Cookie", cookie);
                    if (!string.IsNullOrWhiteSpace(saved.UserAgent))
                        config.SetValue("_userAgent", saved.UserAgent);
                    FileLogger.Info(LogSource, $"Cookie 已从 cookies/{ProviderId}.json 恢复并回填配置（len={cookie.Length}）");
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warn(LogSource, $"Cookie 回退读取失败：{ex.Message}");
            }
        }

        var captureEndpoints = fetch.Endpoints
            .Where(ep => !string.Equals(ep.Mode, "http", StringComparison.OrdinalIgnoreCase)).ToList();
        var httpAlways = fetch.Endpoints
            .Where(ep => string.Equals(ep.Mode, "http", StringComparison.OrdinalIgnoreCase) && !ep.Fallback).ToList();
        var httpFallback = fetch.Endpoints
            .Where(ep => string.Equals(ep.Mode, "http", StringComparison.OrdinalIgnoreCase) && ep.Fallback).ToList();

        // 需要浏览器捕获但无任何登录态/直连端点时，直接返回引导文案（避免空跑浏览器）。
        if (string.IsNullOrWhiteSpace(cookie) && captureEndpoints.Count > 0 && httpAlways.Count == 0)
            return CreateError("未配置登录态，请在设置界面点击「🌐 获取登录态」完成登录", UsageErrorCodes.CredentialMissing);

        try
        {
            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, string>? dom = null;

            // 1. capture 路径：通用浏览器抓取（Cookie 会话）。
            if (captureEndpoints.Count > 0 && !string.IsNullOrWhiteSpace(cookie))
            {
                var (navigateUrl, cookieDomain) = ResolveCaptureTarget(fetch, config);
                if (string.IsNullOrEmpty(navigateUrl) || string.IsNullOrEmpty(cookieDomain))
                {
                    FileLogger.Warn(LogSource, "声明包缺少 fetch.capture（导航 URL / Cookie 域），跳过浏览器捕获路径");
                }
                else
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var capture = await BrowserCaptureService.CaptureAsync(new BrowserCaptureRequest
                    {
                        Cookie = cookie!,
                        UserAgent = userAgent,
                        CookieDomain = cookieDomain!,
                        NavigateUrl = navigateUrl!,
                        CaptureUrlMatches = CollectCaptureMatches(fetch),
                        DomFields = fetch.Dom
                    }, ct);
                    sw.Stop();
                    if (capture != null)
                    {
                        if (capture.LoginInvalid)
                            return CreateError("登录态已失效，请重新获取登录态", UsageErrorCodes.AuthInvalid);
                        foreach (var kv in capture.Responses) responses[kv.Key] = kv.Value;
                        dom = capture.Dom;
                        FileLogger.Info(LogSource, $"浏览器捕获完成（{sw.ElapsedMilliseconds}ms，{responses.Count} 个响应）");
                    }
                    else
                    {
                        FileLogger.Warn(LogSource, $"浏览器捕获失败（{sw.ElapsedMilliseconds}ms），尝试 http 端点回退");
                    }
                }
            }

            // 2. http 常规端点：声明式直连（如纯 API 型 Provider）。
            if (httpAlways.Count > 0)
            {
                foreach (var kv in await DeclarativeHttpFetcher.FetchAsync(httpAlways, config.GetValue, cookie, CredentialDomains, ct))
                    responses[kv.Key] = kv.Value;
            }

            // 3. 首轮声明映射。
            var result = DeclarativeCaptureExecutor.Execute(fetch, responses, dom);
            var primaryKey = _manifest.Card?.PrimaryMetric;

            // 4. 主指标缺失时执行 http 回退端点并重映射（替代旧插件的"DOM 主路径 + API 回退"双路径 C#）。
            if (httpFallback.Count > 0 && !HasPrimaryMetric(result.Extras, primaryKey))
            {
                FileLogger.Warn(LogSource, "主指标缺失，执行 http 回退端点");
                foreach (var kv in await DeclarativeHttpFetcher.FetchAsync(httpFallback, config.GetValue, cookie, CredentialDomains, ct))
                    responses[kv.Key] = kv.Value;
                result = DeclarativeCaptureExecutor.Execute(fetch, responses, dom);
            }

            if (!HasPrimaryMetric(result.Extras, primaryKey))
                return CreateError("未能获取主指标数据（登录态可能已失效，请重新获取登录态）", UsageErrorCodes.DataEmpty);

            return BuildUsageInfo(result, primaryKey!);
        }
        catch (HttpRequestException ex)
        {
            return CreateError($"网络请求失败：{ex.Message}", UsageErrorCodes.NetworkError);
        }
        // req-065 B7：取消与超时分类——用户主动取消显示"用户取消"，网络超时显示"请求超时"。
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CreateError("用户取消", UsageErrorCodes.Cancelled);
        }
        catch (TaskCanceledException)
        {
            return CreateError("请求超时（30s），服务商接口可能响应缓慢或不可达，请稍后重试", UsageErrorCodes.Timeout);
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogSource, $"GetUsageAsync 异常：{ex.Message}", ex);
            return CreateError(ex.Message);
        }
    }

    /// <summary>
    /// 验证配置有效性：直接执行一次取数并检查是否成功（与旧插件行为一致）。
    /// </summary>
    /// <param name="config">待验证配置。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var result = await GetUsageAsync(config, ct);
        return result.IsSuccess;
    }

    /// <summary>日志来源标识（带 ProviderId 前缀，便于多声明包共存时区分）。</summary>
    private string LogSource => $"DeclarativeProvider[{ProviderId}]";

    /// <summary>凭据允许域集合（懒推导：loginConfig / fetch.capture / usageUrls / credentialDomains）。</summary>
    private IReadOnlyCollection<string> CredentialDomains
        => _credentialDomains ??= CredentialDomainGuard.CollectAllowedDomains(_manifest);

    /// <summary>判断 extras 是否已含主指标（primaryKey 未声明时视为不满足，避免静默空数据）。</summary>
    private static bool HasPrimaryMetric(IReadOnlyDictionary<string, object> extras, string? primaryKey)
        => !string.IsNullOrEmpty(primaryKey) && extras.TryGetValue(primaryKey!, out var v) && v != null;

    /// <summary>
    /// 解析浏览器捕获目标（导航 URL + Cookie 域）：优先 fetch.capture 声明（含按配置字段的变体切换），
    /// 缺省回退 usageUrls 首项 + loginConfig 域名推断。
    /// </summary>
    /// <param name="fetch">取数声明。</param>
    /// <param name="config">Provider 配置（变体字段取值）。</param>
    private (string? NavigateUrl, string? CookieDomain) ResolveCaptureTarget(FetchDeclaration fetch, ProviderConfig config)
    {
        var navigateUrl = fetch.Capture?.NavigateUrl;
        var cookieDomain = fetch.Capture?.CookieDomain;

        // 变体切换：按声明的配置字段值（如 Region=Global）覆盖默认导航 URL / Cookie 域。
        if (fetch.Capture?.VariantField is { Length: > 0 } variantField)
        {
            var variantValue = config.GetValue(variantField)?.Trim();
            if (!string.IsNullOrEmpty(variantValue))
            {
                var hit = fetch.Capture.Variants
                    .FirstOrDefault(kv => string.Equals(kv.Key, variantValue, StringComparison.OrdinalIgnoreCase));
                if (hit.Value != null)
                {
                    if (!string.IsNullOrEmpty(hit.Value.NavigateUrl)) navigateUrl = hit.Value.NavigateUrl;
                    if (!string.IsNullOrEmpty(hit.Value.CookieDomain)) cookieDomain = hit.Value.CookieDomain;
                }
            }
        }

        // 回退：无 capture 声明时用 usageUrls 首项 + loginConfig 域名推断（尽力而为）。
        if (string.IsNullOrEmpty(navigateUrl) && _manifest.UsageUrls.Count > 0)
            navigateUrl = _manifest.UsageUrls.Values.First();
        if (string.IsNullOrEmpty(cookieDomain))
        {
            var domain = _manifest.LoginConfig?.RequiredCookieDomain
                         ?? _manifest.LoginConfig?.CookieDomainFilters.FirstOrDefault();
            if (!string.IsNullOrEmpty(domain))
                cookieDomain = domain!.StartsWith(".", StringComparison.Ordinal) ? domain : "." + domain;
        }
        return (navigateUrl, cookieDomain);
    }

    /// <summary>汇总浏览器捕获需匹配的接口 URL 子串（capture 端点 + 聚合 + 账号身份声明）。</summary>
    private static List<string> CollectCaptureMatches(FetchDeclaration fetch)
    {
        var matches = new List<string>();
        foreach (var ep in fetch.Endpoints)
        {
            if (string.Equals(ep.Mode, "http", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(ep.UrlMatch)) matches.Add(ep.UrlMatch);
        }
        foreach (var agg in fetch.Aggregates)
            if (!string.IsNullOrEmpty(agg.UrlMatch)) matches.Add(agg.UrlMatch);
        if (fetch.AccountId != null && !string.IsNullOrEmpty(fetch.AccountId.UrlMatch))
            matches.Add(fetch.AccountId.UrlMatch);
        return matches;
    }

    /// <summary>
    /// 从声明映射结果构造 UsageInfo：主指标（0-100 百分比语义）填充核心字段，
    /// render_kinds 从 card.renderKinds 补写，账号身份哈希为 account_id。
    /// </summary>
    /// <param name="result">声明执行结果（extras + 平台稳定 ID）。</param>
    /// <param name="primaryKey">主指标 extras 键（card.primaryMetric）。</param>
    private UsageInfo BuildUsageInfo(CaptureResult result, string primaryKey)
    {
        var extras = new Dictionary<string, object>(result.Extras);

        // 渲染能力集合：声明 renderKinds 直接进 extras（宿主首屏渲染依据）。
        if (_manifest.Card?.RenderKinds is { Count: > 0 } renderKinds)
            extras["render_kinds"] = new List<string>(renderKinds);

        var primary = Convert.ToDouble(extras[primaryKey], System.Globalization.CultureInfo.InvariantCulture);
        var usage = new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            // req-067 B21：统一 UTC 存储，避免时区问题
            LastUpdated = DateTime.UtcNow
        };
#pragma warning disable CS0618 // 遗留 UsedAmount/TotalAmount 字段仍是宿主主指标消费路径，声明包按百分比语义填充
        usage.UsedAmount = (decimal)primary;
        usage.TotalAmount = 100m;
        usage.Unit = "%";
#pragma warning restore CS0618
        usage.PopulateQuantityFromLegacy();
        usage.AccountId = AccountIdHasher.Compute(ProviderId, result.StableId);
        usage.Extra = extras;
        return usage;
    }

    /// <summary>创建失败态 UsageInfo（错误消息由宿主按 errorGuidance 声明映射为引导文案）。
    /// <para>req-116：附带稳定错误码（<see cref="UsageErrorCodes"/>），供插件 matchCodes 去语言化匹配。</para></summary>
    /// <param name="message">原始错误消息。</param>
    /// <param name="code">稳定错误码（可空 = 未分类）。</param>
    private UsageInfo CreateError(string message, string? code = null)
    {
        var info = UsageInfo.CreateError(ProviderId, DisplayName, message);
        if (code != null)
            info.Error = new UsageError(UsageErrorKind.Unknown, message) { Code = code };
        return info;
    }
}
