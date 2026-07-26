using System.Text.RegularExpressions;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Security;

/// <summary>
/// 凭据占位符域名同源约束（安全加固）：声明式插件 http 端点若携带 Cookie / 敏感配置占位符
/// （<c>{cookieHeader}</c>、<c>{cookie:名}</c>、<c>{config:敏感键}</c>），其展开后的目标域名必须
/// 命中声明包可推导的"官方域集合"（loginConfig / fetch.capture / usageUrls / credentialDomains），
/// 阻断恶意或被篡改声明包把用户登录态外发到任意外部 HTTPS 服务器的通道。
/// </summary>
public static class CredentialDomainGuard
{
    /// <summary>Cookie 占位符检测（{cookieHeader} 或 {cookie:名}）。</summary>
    private static readonly Regex CookiePlaceholderPattern =
        new(@"\{cookie(Header\}|:)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>配置占位符检测（{config:键}，键名交由敏感注册表判定）。</summary>
    private static readonly Regex ConfigPlaceholderPattern =
        new(@"\{config:([^}]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 汇总声明包可推导的凭据允许域集合（小写、无前导点）：
    /// 显式 credentialDomains + loginConfig 各域名/URL + fetch.capture（含变体）+ usageUrls。
    /// </summary>
    /// <param name="manifest">插件合并清单。</param>
    public static HashSet<string> CollectAllowedDomains(PluginManifest manifest)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest == null) return domains;

        // 1. 显式声明（纯 API 型声明包无登录节时的唯一声明途径）
        foreach (var d in manifest.CredentialDomains) AddDomain(domains, d);

        // 2. 登录声明：登录 URL / Cookie 域过滤 / 成功判定域
        if (manifest.LoginConfig is { } login)
        {
            AddDomain(domains, login.LoginUrl);
            AddDomain(domains, login.ValidateUrl);
            AddDomain(domains, login.RequiredCookieDomain);
            AddDomain(domains, login.LoginSuccessHost);
            AddDomain(domains, login.LoggedInHost);
            foreach (var f in login.CookieDomainFilters) AddDomain(domains, f);
        }

        // 3. 浏览器捕获声明（含变体）
        if (manifest.Fetch?.Capture is { } capture)
        {
            AddDomain(domains, capture.NavigateUrl);
            AddDomain(domains, capture.CookieDomain);
            foreach (var variant in capture.Variants.Values)
            {
                AddDomain(domains, variant.NavigateUrl);
                AddDomain(domains, variant.CookieDomain);
            }
        }

        // 4. 多语言用量页 URL
        foreach (var url in manifest.UsageUrls.Values) AddDomain(domains, url);

        return domains;
    }

    /// <summary>
    /// 判断目标 host 是否命中允许域集合：与某域完全相等，或为其子域（host 以 ".域" 结尾）。
    /// </summary>
    /// <param name="host">展开后请求 URL 的 host。</param>
    /// <param name="allowedDomains">允许域集合（已归一化）。</param>
    public static bool IsHostAllowed(string? host, IReadOnlyCollection<string> allowedDomains)
    {
        if (string.IsNullOrWhiteSpace(host) || allowedDomains == null || allowedDomains.Count == 0)
            return false;
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var domain in allowedDomains)
        {
            if (string.Equals(h, domain, StringComparison.OrdinalIgnoreCase)) return true;
            if (h.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>检测端点模板（URL + 请求头）是否携带 Cookie 占位符。</summary>
    /// <param name="endpoint">http 端点声明。</param>
    public static bool HasCookiePlaceholder(FetchEndpoint endpoint)
    {
        if (endpoint == null) return false;
        if (!string.IsNullOrEmpty(endpoint.UrlTemplate) && CookiePlaceholderPattern.IsMatch(endpoint.UrlTemplate))
            return true;
        return endpoint.Headers.Values.Any(v => !string.IsNullOrEmpty(v) && CookiePlaceholderPattern.IsMatch(v));
    }

    /// <summary>
    /// 检测端点模板是否携带敏感配置占位符（{config:键} 且键命中 <see cref="SensitiveConfigKeyRegistry"/>）。
    /// </summary>
    /// <param name="endpoint">http 端点声明。</param>
    public static bool HasSensitiveConfigPlaceholder(FetchEndpoint endpoint)
    {
        if (endpoint == null) return false;
        if (TemplateHasSensitiveConfig(endpoint.UrlTemplate)) return true;
        return endpoint.Headers.Values.Any(TemplateHasSensitiveConfig);
    }

    /// <summary>检测单个模板串中的 {config:键} 是否命中敏感键判定。</summary>
    /// <param name="template">URL 或请求头模板（可空）。</param>
    private static bool TemplateHasSensitiveConfig(string? template)
    {
        if (string.IsNullOrEmpty(template)) return false;
        foreach (Match m in ConfigPlaceholderPattern.Matches(template))
        {
            if (SensitiveConfigKeyRegistry.IsSensitive(m.Groups[1].Value))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 尝试从 UrlTemplate 提取字面 host（authority 段不含占位符时）；含占位符或非法时返回 null，
    /// 交由运行期展开后校验。供 PluginValidator 静态校验使用。
    /// </summary>
    /// <param name="urlTemplate">http 端点 URL 模板。</param>
    public static string? TryGetLiteralHost(string? urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(urlTemplate)) return null;
        const string prefix = "https://";
        if (!urlTemplate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = urlTemplate.Substring(prefix.Length);
        var slashIdx = rest.IndexOfAny(new[] { '/', '?', '#' });
        var authority = slashIdx >= 0 ? rest.Substring(0, slashIdx) : rest;
        if (authority.Contains('{') || authority.Length == 0) return null;
        // 去掉端口 / 用户信息
        var atIdx = authority.LastIndexOf('@');
        if (atIdx >= 0) authority = authority.Substring(atIdx + 1);
        var colonIdx = authority.IndexOf(':');
        if (colonIdx >= 0) authority = authority.Substring(0, colonIdx);
        return string.IsNullOrWhiteSpace(authority) ? null : authority.ToLowerInvariant();
    }

    /// <summary>
    /// 归一化并收集域名：入参可为纯域名（可带前导点）或 URL（提取 host）；空白忽略。
    /// </summary>
    /// <param name="domains">目标集合。</param>
    /// <param name="raw">原始域名或 URL。</param>
    private static void AddDomain(HashSet<string> domains, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var value = raw.Trim();
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                value = uri.Host;
            else
                return;
        }
        value = value.TrimStart('.').TrimEnd('.').ToLowerInvariant();
        if (value.Length > 0) domains.Add(value);
    }
}
