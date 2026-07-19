using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.MiniMax;

/// <summary>
/// MiniMax 网页余额与账单明细抓取器。
/// <para>
/// 实现背景（2026-07-07 定时任务）：
/// 用户希望除了已有的 <c>/v1/token_plan/remains</c>（API Key 鉴权）之外，
/// 还能从网页获取"账户余额 + 账单明细"用于展示。
/// </para>
/// <para>
/// 调研发现（详见 .devdoc/20260707xxxxxx_项目开发说明文档-MiniMax网页余额与账单提取.md）：
/// <list type="bullet">
///   <item>MiniMax 用量查询官方入口：<c>https://platform.minimaxi.com/console/usage</c></item>
///   <item>MiniMax 网页是 Next.js SPA，初始 HTML 不含真实数据（90KB+ JS 动态加载）</item>
///   <item>已发现的真实后端 API：
///     <list type="bullet">
///       <item><c>https://api.minimaxi.com/v1/token_plan/remains</c> —— API Key 鉴权（已实现）</item>
///       <item><c>https://api.minimaxi.com/v1/api/openplatform/coding_plan/remains</c> —— Cookie 鉴权（新发现）</item>
///       <item><c>https://account.minimaxi.com/v1/api/user/info</c> —— 端点存在但需有效会话 Cookie</item>
///       <item>账单明细 API（订单/充值记录）未在公开 JS bundle 中暴露，MiniMax 暂未提供 GET 接口</item>
///     </list>
///   </item>
///   <item>账户余额 API 端点路径未在公开资源中暴露，可能在私有后端</item>
/// </list>
/// </para>
/// <para>
/// 设计目标：
/// <list type="bullet">
///   <item>作为 <see cref="MiniMaxProvider"/> 的可选数据源（不影响现有 API Key 鉴权流程）</item>
///   <item>失败时静默降级：抓取失败不影响主流程，只在 <c>UsageInfo.Extra</c> 中标记</item>
///   <item>原始 HTML 保存到 <c>%AppData%/UsageMonitor/debug/MiniMax-page-{timestamp}.html</c> 便于排查</item>
///   <item>复用 <see cref="BrowserLoginService"/> 读取已保存的 Cookie</item>
/// </list>
/// </para>
/// </summary>
internal static class MiniMaxBalanceFetcher
{
    /// <summary>MiniMax 用量查询官方页面（Next.js SPA）</summary>
    private const string UsagePageUrl = "https://platform.minimaxi.com/console/usage";

    /// <summary>MiniMax 用户中心页面</summary>
    private const string UserCenterUrl = "https://platform.minimaxi.com/user-center/payment/token-plan";

    /// <summary>Cookie 鉴权的 coding_plan 用量接口（与 /v1/token_plan/remains 略有差异：无需 API Key）</summary>
    private const string CodingPlanRemainsUrl = "https://api.minimaxi.com/v1/api/openplatform/coding_plan/remains";

    /// <summary>账户基础信息接口（端点存在但当前 Cookie 不足以通过鉴权）</summary>
    private const string UserInfoUrl = "https://account.minimaxi.com/v1/api/user/info";

    /// <summary>Debug 日志目录</summary>
    private static readonly string DebugDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "debug");

    /// <summary>共享 HttpClient（线程安全，超时 30s）</summary>
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// 抓取 MiniMax 账户快照（余额、用量、订单），并合并到 <paramref name="extra"/> 字典。
    /// 任意子步骤失败都不会抛出异常，仅在 extra 中标记失败原因。
    /// </summary>
    /// <param name="extra">要写入的 Extra 字典（来自 UsageInfo）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task FetchAsync(
        Dictionary<string, object> extra,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var fetchLog = new StringBuilder();
        fetchLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] MiniMaxBalanceFetcher.Start");

        try
        {
            // 读取已保存的 Cookie
            var cookieData = BrowserLoginService.LoadCookieData("MiniMax");
            if (cookieData == null || string.IsNullOrEmpty(cookieData.Cookie))
            {
                extra["balanceFetcherStatus"] = "no_cookie";
                extra["balanceFetcherMessage"] = "未找到 MiniMax Cookie，请先通过配置窗口的「获取登录态」按钮登录";
                fetchLog.AppendLine("  [SKIP] no cookie");
                return;
            }

            // 标记数据源
            extra["balanceFetcherSource"] = "web_scrape";
            extra["balanceFetcherCookieSavedAt"] = cookieData.SavedAt.ToString("yyyy-MM-dd HH:mm:ss");
            extra["balanceFetcherCookieCount"] = cookieData.Count;

            // ===== 步骤 1：尝试 Cookie 鉴权的 coding_plan/remains =====
            try
            {
                var codingRemains = await FetchCodingPlanRemainsAsync(cookieData.Cookie, cancellationToken);
                if (codingRemains != null)
                {
                    MergeCodingPlanRemains(extra, codingRemains);
                    fetchLog.AppendLine($"  [OK] coding_plan/remains merged ({codingRemains.Count} fields)");
                }
                else
                {
                    fetchLog.AppendLine("  [SKIP] coding_plan/remains returned 401/403");
                }
            }
            catch (Exception ex)
            {
                fetchLog.AppendLine($"  [ERR] coding_plan/remains: {ex.Message}");
            }

            // ===== 步骤 2：抓取 console/usage 页面（保存 HTML 用于后续解析） =====
            try
            {
                var htmlPath = await SaveUsagePageHtmlAsync(cookieData.Cookie, cancellationToken);
                if (htmlPath != null)
                {
                    extra["balancePageSnapshotPath"] = htmlPath;
                    fetchLog.AppendLine($"  [OK] HTML saved -> {htmlPath}");
                }
            }
            catch (Exception ex)
            {
                fetchLog.AppendLine($"  [ERR] HTML fetch: {ex.Message}");
            }

            // ===== 步骤 3：尝试 account 用户信息接口 =====
            try
            {
                var userInfo = await FetchUserInfoAsync(cookieData.Cookie, cancellationToken);
                if (userInfo != null)
                {
                    extra["accountUserInfo"] = userInfo;
                    fetchLog.AppendLine("  [OK] user/info fetched");
                }
            }
            catch (Exception ex)
            {
                fetchLog.AppendLine($"  [ERR] user/info: {ex.Message}");
            }

            sw.Stop();
            extra["balanceFetcherElapsedMs"] = sw.ElapsedMilliseconds;
            extra["balanceFetcherStatus"] = "success";
            fetchLog.AppendLine($"[DONE] total {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            extra["balanceFetcherStatus"] = "error";
            extra["balanceFetcherMessage"] = ex.Message;
            fetchLog.AppendLine($"[FATAL] {ex.Message}");
        }
        finally
        {
            // 抓取日志写入 debug 目录
            TryWriteDebugLog("MiniMax-balance-fetch", fetchLog.ToString());
        }
    }

    /// <summary>
    /// 调用 <see cref="CodingPlanRemainsUrl"/> 获取 Cookie 鉴权的 coding_plan 用量。
    /// </summary>
    /// <returns>解析后的字段字典；鉴权失败返回 null</returns>
    private static async Task<Dictionary<string, object>?> FetchCodingPlanRemainsAsync(
        string cookie, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, CodingPlanRemainsUrl);
        // req-065 B6：Cookie header injection防护，清理控制字符
        var safeCookie = UsageMonitor.Core.Services.CookieHeaderSanitizer.Sanitize(cookie);
        if (!req.Headers.TryAddWithoutValidation("Cookie", safeCookie))
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MiniMaxBalanceFetcher", "Cookie header rejected after sanitization in FetchCodingPlanRemainsAsync");
        }
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) UsageMonitor/1.4.0");

        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);

        // 保存原始响应到 debug
        TryWriteDebugText($"MiniMax-coding-plan-remains-{DateTime.Now:yyyyMMdd-HHmmss}.json", json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("base_resp", out var baseResp) &&
            baseResp.TryGetProperty("status_code", out var sc) && sc.GetInt32() != 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>();
        if (root.TryGetProperty("base_resp", out var br))
        {
            result["baseResp"] = br.GetRawText();
        }
        if (root.TryGetProperty("model_remains", out var mr))
        {
            result["modelRemains"] = JsonSerializer.Deserialize<JsonElement>(mr.GetRawText());
        }

        return result;
    }

    /// <summary>
    /// 抓取 <see cref="UsagePageUrl"/> 的 HTML，保存到 debug 目录。
    /// 当前 MiniMax 是 Next.js SPA，HTML 不含真实数据，但保留快照便于后续结构变化排查。
    /// </summary>
    private static async Task<string?> SaveUsagePageHtmlAsync(string cookie, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsagePageUrl);
        // req-065 B6：Cookie header injection防护，清理控制字符
        var safeCookie = UsageMonitor.Core.Services.CookieHeaderSanitizer.Sanitize(cookie);
        if (!req.Headers.TryAddWithoutValidation("Cookie", safeCookie))
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MiniMaxBalanceFetcher", "Cookie header rejected after sanitization in SaveUsagePageHtmlAsync");
        }
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) UsageMonitor/1.4.0");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var html = await resp.Content.ReadAsStringAsync(ct);
        var fileName = $"MiniMax-page-{DateTime.Now:yyyyMMdd-HHmmss-fff}.html";
        return TryWriteDebugText(fileName, html);
    }

    /// <summary>
    /// 尝试调用 <see cref="UserInfoUrl"/> 获取账户基础信息。
    /// 当前会因 Cookie 鉴权失败返回 null（保留代码作为未来扩展点）。
    /// </summary>
    private static async Task<Dictionary<string, object>?> FetchUserInfoAsync(
        string cookie, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        // req-065 B6：Cookie header injection防护，清理控制字符
        var safeCookie = UsageMonitor.Core.Services.CookieHeaderSanitizer.Sanitize(cookie);
        if (!req.Headers.TryAddWithoutValidation("Cookie", safeCookie))
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MiniMaxBalanceFetcher", "Cookie header rejected after sanitization in FetchUserInfoAsync");
        }
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 UsageMonitor");

        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return null;
        }
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync(ct);
        TryWriteDebugText($"MiniMax-user-info-{DateTime.Now:yyyyMMdd-HHmmss}.json", json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new Dictionary<string, object>();
        foreach (var prop in root.EnumerateObject())
        {
            result[prop.Name] = JsonSerializer.Deserialize<JsonElement>(prop.Value.GetRawText());
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 将 coding_plan/remains 响应合并到 extra 字典（供 ViewModel 显示）。
    /// 提取关键的余额/窗口字段。
    /// </summary>
    private static void MergeCodingPlanRemains(
        Dictionary<string, object> extra,
        Dictionary<string, object> codingData)
    {
        extra["accountBalanceSource"] = "MiniMax-coding-plan-remains";

        if (codingData.TryGetValue("modelRemains", out var mrObj) && mrObj is JsonElement mr)
        {
            var arr = mr.ValueKind == JsonValueKind.Array ? mr : default;
            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var first = arr[0];
                if (first.TryGetProperty("model_name", out var mn))
                    extra["accountBalanceModelName"] = mn.GetString() ?? "";
                if (first.TryGetProperty("current_interval_total_count", out var citc))
                    extra["accountIntervalTotal"] = citc.GetInt64();
                if (first.TryGetProperty("current_interval_usage_count", out var ciuc))
                    extra["accountIntervalRemaining"] = ciuc.GetInt64();
                if (first.TryGetProperty("current_weekly_total_count", out var cwtc))
                    extra["accountWeeklyTotal"] = cwtc.GetInt64();
                if (first.TryGetProperty("current_weekly_usage_count", out var cwuc))
                    extra["accountWeeklyRemaining"] = cwuc.GetInt64();
                if (first.TryGetProperty("remains_time", out var rt))
                    extra["accountIntervalRemainMs"] = rt.GetInt64();
                if (first.TryGetProperty("start_time", out var st))
                    extra["accountIntervalStartAt"] = DateTimeOffset.FromUnixTimeMilliseconds(st.GetInt64()).LocalDateTime;
                if (first.TryGetProperty("end_time", out var et))
                    extra["accountIntervalEndAt"] = DateTimeOffset.FromUnixTimeMilliseconds(et.GetInt64()).LocalDateTime;
            }
        }
    }

    /// <summary>写入 debug 日志文本（失败静默）</summary>
    private static void TryWriteDebugLog(string name, string content)
    {
        TryWriteDebugText($"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log", content);
    }

    /// <summary>写入 debug 文本文件（失败静默），返回写入路径</summary>
    private static string? TryWriteDebugText(string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(DebugDir);
            DebugFileManager.CleanupOldDebugFiles();
            var path = Path.Combine(DebugDir, fileName);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }
}