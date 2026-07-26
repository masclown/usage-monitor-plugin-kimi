using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Security;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Plugins.Declarative;

/// <summary>
/// Stage E：声明式 HTTP 直连取数器（spec P4 SDK 新能力①）。
/// <para>执行 fetch 声明里 mode=http 的端点：按模板展开 URL / 请求头（支持 <c>{config:字段Key}</c>、
/// <c>{cookie:Cookie名}</c>、<c>{cookieHeader}</c> 占位符），经 req-056 SSRF 校验后直接发 HTTP 请求，
/// 把响应体按"URL → JSON 文本"存入捕获字典，供 <see cref="DeclarativeCaptureExecutor"/> 按 urlMatch 统一映射。
/// 替代过去插件 C# 手写的 API 请求代码（如 MiniMax QueryRemainsAsync 的 x-group-id 头逻辑）。</para>
/// </summary>
public static class DeclarativeHttpFetcher
{
    private const string LogSource = "DeclarativeHttpFetcher";

    /// <summary>共享 HttpClient（线程安全；30s 超时覆盖海外服务商高延迟场景）。</summary>
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// 执行一批 http 模式端点，返回"展开后 URL → 响应体"字典（失败端点跳过并记日志，不抛出）。
    /// </summary>
    /// <param name="endpoints">待执行端点（调用方已按 mode=http 过滤）。</param>
    /// <param name="configValue">配置字段取值委托（供 {config:Key} 占位符）。</param>
    /// <param name="cookie">Cookie 串（供 {cookie:名} / {cookieHeader} 占位符；可空）。</param>
    /// <param name="credentialAllowedDomains">凭据允许域集合（CredentialDomainGuard.CollectAllowedDomains 产出；
    /// null/空 = 声明包无可推导官方域，此时携带 Cookie 占位符的端点一律拒绝）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<Dictionary<string, string>> FetchAsync(
        IEnumerable<FetchEndpoint> endpoints,
        Func<string, string?> configValue,
        string? cookie,
        IReadOnlyCollection<string>? credentialAllowedDomains = null,
        CancellationToken ct = default)
    {
        var responses = new Dictionary<string, string>();
        foreach (var ep in endpoints)
        {
            if (string.IsNullOrWhiteSpace(ep.UrlTemplate)) continue;
            try
            {
                var url = ExpandPlaceholders(ep.UrlTemplate!, configValue, cookie);

                // 凭据域名同源约束：携带 Cookie / 敏感配置占位符的端点，目标域必须命中声明包官方域集合，
                // 阻断恶意声明包把登录态/API Key 外发到任意外部 HTTPS 域（纯字符串判定，无 DNS 依赖，先于 SSRF 校验）。
                if (!PassesCredentialDomainCheck(ep, url, credentialAllowedDomains)) continue;

                // req-056 SSRF 防护：展开后的真实 URL 必须为 https 且非内网/环回地址。
                if (!BaseUrlValidator.TryValidate(url, out var ssrfError))
                {
                    FileLogger.Warn(LogSource, $"http 端点 URL 未通过 SSRF 校验，跳过：{ssrfError}");
                    continue;
                }

                using var request = new HttpRequestMessage(
                    string.Equals(ep.Method, "POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get,
                    url);
                foreach (var header in ep.Headers)
                {
                    var value = ExpandPlaceholders(header.Value, configValue, cookie);
                    if (string.IsNullOrEmpty(value)) continue; // 占位符展开为空（如无 Cookie）时不携带该头
                    // Cookie 头统一走 req-065 B6 注入防护清理
                    if (string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
                        value = CookieHeaderSanitizer.Sanitize(value);
                    request.Headers.TryAddWithoutValidation(header.Key, value);
                }
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (request.Headers.UserAgent.Count == 0)
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) UsageMonitor");

                var response = await Http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    FileLogger.Warn(LogSource, $"http 端点返回 {(int)response.StatusCode}：{ep.UrlMatch}");
                    continue;
                }
                responses[url] = await response.Content.ReadAsStringAsync(ct);
                FileLogger.Info(LogSource, $"http 端点抓取成功：{ep.UrlMatch}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 用户取消向上传递，由调用方统一分类
            }
            catch (Exception ex)
            {
                FileLogger.Warn(LogSource, $"http 端点抓取失败（{ep.UrlMatch}）：{ex.Message}");
            }
        }
        return responses;
    }

    /// <summary>
    /// 凭据域名同源检查：端点携带 Cookie 占位符时，目标 host 必须命中允许域（无允许域则一律拒绝）；
    /// 仅携带敏感配置占位符时，有允许域则强制命中，无允许域时告警放行（兼容无登录节的纯 API 声明包）。
    /// </summary>
    /// <param name="ep">端点声明。</param>
    /// <param name="url">展开后的真实 URL。</param>
    /// <param name="allowedDomains">凭据允许域集合（可空）。</param>
    /// <returns>true=允许发送；false=拦截（已记日志）。</returns>
    private static bool PassesCredentialDomainCheck(FetchEndpoint ep, string url, IReadOnlyCollection<string>? allowedDomains)
    {
        var carriesCookie = CredentialDomainGuard.HasCookiePlaceholder(ep);
        var carriesSensitiveConfig = CredentialDomainGuard.HasSensitiveConfigPlaceholder(ep);
        if (!carriesCookie && !carriesSensitiveConfig) return true;

        string host;
        try { host = new Uri(url).Host; }
        catch (UriFormatException)
        {
            FileLogger.Warn(LogSource, $"凭据端点 URL 无法解析 host，拒绝发送：{ep.UrlMatch}");
            return false;
        }

        if (allowedDomains is { Count: > 0 })
        {
            if (CredentialDomainGuard.IsHostAllowed(host, allowedDomains)) return true;
            FileLogger.Warn(LogSource,
                $"凭据域名同源约束拦截：端点目标域 {host} 不在声明包官方域集合内，已拒绝发送（{ep.UrlMatch}）");
            return false;
        }

        // 无任何可推导官方域：Cookie 外发一律拒绝；敏感配置占位符告警放行（用户为该插件主动录入的凭据）。
        if (carriesCookie)
        {
            FileLogger.Warn(LogSource,
                $"凭据域名同源约束拦截：声明包未声明任何官方域（loginConfig/capture/usageUrls/credentialDomains 均缺失），" +
                $"携带 Cookie 占位符的端点已拒绝发送（{ep.UrlMatch}）");
            return false;
        }
        FileLogger.Warn(LogSource,
            $"凭据端点目标域 {host} 无法验证（声明包未声明 credentialDomains），建议声明后启用强制校验（{ep.UrlMatch}）");
        return true;
    }

    /// <summary>
    /// 展开模板占位符：<c>{config:Key}</c> → 配置字段值；<c>{cookie:名}</c> → Cookie 串中对应值；
    /// <c>{cookieHeader}</c> → 完整 Cookie 串。未识别的占位符原样保留。
    /// </summary>
    /// <param name="template">URL 或请求头模板。</param>
    /// <param name="configValue">配置字段取值委托。</param>
    /// <param name="cookie">Cookie 串（可空）。</param>
    public static string ExpandPlaceholders(string template, Func<string, string?> configValue, string? cookie)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{(config|cookie):([^}]+)\}|\{cookieHeader\}",
            m =>
            {
                if (m.Value == "{cookieHeader}") return cookie?.Trim() ?? string.Empty;
                var kind = m.Groups[1].Value;
                var name = m.Groups[2].Value;
                return kind switch
                {
                    "config" => configValue(name)?.Trim() ?? string.Empty,
                    "cookie" => ExtractCookieValue(cookie, name) ?? string.Empty,
                    _ => m.Value
                };
            });
    }

    /// <summary>
    /// 从 Cookie 串（"k1=v1; k2=v2"）提取指定名称的值（供 {cookie:名} 占位符，如 x-group-id 请求头）。
    /// </summary>
    /// <param name="cookieString">Cookie 串（可空）。</param>
    /// <param name="name">Cookie 名（大小写不敏感）。</param>
    public static string? ExtractCookieValue(string? cookieString, string name)
    {
        if (string.IsNullOrWhiteSpace(cookieString) || string.IsNullOrWhiteSpace(name)) return null;
        foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx <= 0) continue;
            var key = part.Substring(0, eqIdx).Trim();
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return part.Substring(eqIdx + 1).Trim();
        }
        return null;
    }
}
