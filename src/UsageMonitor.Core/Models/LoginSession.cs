using System.Globalization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-091：单个登录会话段（一次连续可用的 Cookie 有效期）。
/// <para>
/// 持久化格式（JSON Lines 追加写）：每个段一行，包含：
/// <list type="bullet">
///   <item><description><see cref="StartDate"/>：本地日期（首次获取且可用）</description></item>
///   <item><description><see cref="EndDate"/>：本地日期（最近一次可用，活跃段为 null）</description></item>
///   <item><description><see cref="TriggerSource"/>：触发源（首次 / 自动重登 / 手动重登）</description></item>
/// </list>
/// </para>
/// <para>持续天数计算口径（王晨 16:40 拍板）：
/// 起算 = 第一次获取且可用的日期，截止 = 最近一次可用的日期。
/// 公式 = 最近可用日 − 首次可用日 + 1。重新获取后**重新起算**，但**历史段保留**。</para>
/// </summary>
public sealed class LoginSession
{
    /// <summary>本地日期（首次获取且可用，yyyy-MM-dd 字符串）</summary>
    public string StartDate { get; set; } = string.Empty;

    /// <summary>
    /// 本地日期（最近一次可用）。null 表示活跃段（当前正在用的 Cookie），
    /// 非 null 表示已归档的历史段（Cookie 失效后归档）。
    /// </summary>
    public string? EndDate { get; set; }

    /// <summary>触发源（FirstLogin / AutoRefresh / ManualReLogin）</summary>
    public string TriggerSource { get; set; } = "FirstLogin";

    /// <summary>写入时间戳（UTC，调试用）</summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 计算持续天数（基于本地日期）。
    /// <para>公式：<c>(EndDate ?? Today) - StartDate + 1</c>。</para>
    /// <para>持续天数为 0 表示「首次登录当天」（同一天）—— 王晨 16:57 要求此时显示空值。</para>
    /// </summary>
    /// <returns>持续天数；首次登录当天返回 0。</returns>
    public int CalculateDurationDays()
    {
        if (!DateTime.TryParseExact(StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var start))
            return 0;

        var end = EndDate == null
            ? DateTime.Today
            : (DateTime.TryParseExact(EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) ? parsed : DateTime.Today);

        var days = (end - start).Days + 1;
        return Math.Max(0, days);
    }

    /// <summary>判断当前段是否为活跃段（EndDate 为 null）</summary>
    public bool IsActive => EndDate == null;
}

/// <summary>
/// req-091：触发源枚举。
/// </summary>
public static class LoginSessionTriggers
{
    /// <summary>首次获取登录态（从未登录过）</summary>
    public const string FirstLogin = "FirstLogin";

    /// <summary>自动检测到 Cookie 失效后重新登录</summary>
    public const string AutoRelogin = "AutoRelogin";

    /// <summary>用户手动点击「获取登录态」按钮触发</summary>
    public const string ManualRelogin = "ManualRelogin";

    /// <summary>老 Cookie 文件首次发现（req-091-006 迁移）</summary>
    public const string LegacyMigration = "LegacyMigration";
}