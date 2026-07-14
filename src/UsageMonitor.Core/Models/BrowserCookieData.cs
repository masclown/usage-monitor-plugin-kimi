using System;
using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 浏览器 Cookie 持久化数据模型 - 与销项数据助手项目的 <c>.config/cookie.json</c> 结构对齐：
/// <code>
/// {
///   "cookie": "key1=value1; key2=value2; ...",
///   "userAgent": "Mozilla/5.0 ...",
///   "savedAt": "2026-07-06T12:00:00",
///   "count": 56,
///   "domain": "minimaxi.com"
/// }
/// </code>
/// <para>
/// 设计原则：
/// <list type="bullet">
///   <item><c>cookie</c>：原始 <c>name=value</c> 拼接字符串，可直接用于 HTTP 请求头</item>
///   <item><c>userAgent</c>：浏览器 UA，便于在下载请求时回放，模拟真实请求</item>
///   <item><c>savedAt</c>：ISO 8601 时间戳，便于排查"何时登录的 / 多久没刷新了"</item>
///   <item><c>count</c>：Cookie 条数，快速判断是否拿到了合理数量的 Cookie</item>
///   <item><c>domain</c>：主要域名，便于在日志 / 调试时辨认</item>
/// </list>
/// </para>
/// </summary>
public class BrowserCookieData
{
    /// <summary>所有 Cookie 以 <c>; </c> 拼接的字符串，格式 <c>name=value</c></summary>
    public string Cookie { get; set; } = string.Empty;

    /// <summary>浏览器 User-Agent</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Cookie 保存时间（UTC ISO 8601）</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Cookie 条数</summary>
    public int Count { get; set; }

    /// <summary>主要域名（来自 <see cref="BrowserLoginConfig.CookieDomainFilters"/> 第一个命中）</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>所属服务商 ID</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>登录入口 URL（便于排查）</summary>
    public string LoginUrl { get; set; } = string.Empty;

    /// <summary>
    /// 原始 Cookie 列表（可选）。销项数据助手保留完整结构用于调试，
    /// 本项目保留以便未来支持 per-cookie 字段（如 expires/path/sameSite）。
    /// </summary>
    public List<BrowserCookieEntry>? RawCookies { get; set; }

    /// <summary>
    /// 从 Playwright 抓取的用量页面快照（登录后立即抓取）。
    /// 包含 5h 窗口和 weekly 窗口的用量数字 + 时间戳。
    /// 优先于 API 查询使用（避免依赖 API Key）。
    /// </summary>
    public UsageSnapshot? UsageSnapshot { get; set; }
}

/// <summary>单个 Cookie 的元数据（对应 CDP <c>Network.getAllCookies</c> 返回的字段）</summary>
public class BrowserCookieEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public double? Expires { get; set; }
    public bool HttpOnly { get; set; }
    public bool Secure { get; set; }
    public string? SameSite { get; set; }
}

/// <summary>
/// 用量页面快照（Playwright 登录后从 https://platform.minimaxi.com/console/usage 抓取）。
/// 用于在无 API Key 的情况下显示用量数据。
/// </summary>
public class UsageSnapshot
{
    /// <summary>5h 窗口总额（次）</summary>
    public long? IntervalTotal { get; set; }

    /// <summary>5h 窗口剩余（次）— 注意：API 字段名 *_usage_count 实际指"剩余"</summary>
    public long? IntervalUsage { get; set; }

    /// <summary>5h 窗口剩余毫秒</summary>
    public long? IntervalRemainsTime { get; set; }

    /// <summary>周窗口总额（次）</summary>
    public long? WeeklyTotal { get; set; }

    /// <summary>周窗口剩余（次）</summary>
    public long? WeeklyUsage { get; set; }

    /// <summary>周窗口剩余毫秒</summary>
    public long? WeeklyRemainsTime { get; set; }

    /// <summary>模型名（如 MiniMax-Text-01）</summary>
    public string? ModelName { get; set; }

    /// <summary>抓取时间（UTC）</summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>抓取来源 URL</summary>
    public string? SourceUrl { get; set; }
}