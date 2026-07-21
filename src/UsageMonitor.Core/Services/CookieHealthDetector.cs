using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-091-002：Cookie 失效检测探针。
/// <para>
/// 决策依据（已集齐）：
/// <list type="bullet">
///   <item><description>HTTP 状态码优先判定（默认 401/403 视为登录态问题，其他一律按网络/未知错误处理）</description></item>
///   <item><description>运行时可配置（通过 <see cref="LoginStatusCodes"/> 属性注入）</description></item>
///   <item><description>存储位置：高级设置 Tab（用户可增删改状态码列表）</description></item>
/// </list>
/// </para>
/// <para>
/// 失败原因分类：
/// <list type="bullet">
///   <item><description><see cref="FailureKind.LoginExpired"/>：命中可配置状态码集合（默认 401/403）</description></item>
///   <item><description><see cref="FailureKind.NetworkError"/>：HttpRequestException / TaskCanceledException 等网络异常</description></item>
///   <item><description><see cref="FailureKind.Unknown"/>：其他无法分类的失败</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class CookieHealthDetector
{
    /// <summary>默认状态码集合：401（未授权）+ 403（禁止访问）</summary>
    public static readonly IReadOnlyList<int> DefaultLoginStatusCodes = new[] { 401, 403 };

    private readonly IReadOnlyList<int> _loginStatusCodes;

    /// <summary>
    /// 创建检测器实例。
    /// </summary>
    /// <param name="loginStatusCodes">
    /// 判定为登录态问题的 HTTP 状态码集合（null 或空用默认 401/403）。
    /// </param>
    public CookieHealthDetector(IReadOnlyList<int>? loginStatusCodes = null)
    {
        _loginStatusCodes = (loginStatusCodes != null && loginStatusCodes.Count > 0)
            ? loginStatusCodes
            : DefaultLoginStatusCodes;
    }

    /// <summary>当前生效的「登录态」状态码集合（只读）。</summary>
    public IReadOnlyList<int> LoginStatusCodes => _loginStatusCodes;

    /// <summary>
    /// req-091：判定 HTTP 状态码是否为登录态问题。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码（如 200/401/500/...）</param>
    /// <returns>true = 登录态问题（Cookie 失效）</returns>
    public bool IsLoginExpired(int statusCode)
    {
        if (statusCode <= 0) return false;
        return _loginStatusCodes.Contains(statusCode);
    }

    /// <summary>
    /// req-091：综合判定失败原因（状态码 + 异常类型）。
    /// 优先级：状态码 > 异常类型。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码（未知场景传 -1 或 0）</param>
    /// <param name="exception">抛出的异常（成功时为 null）</param>
    public FailureKind Classify(int statusCode, Exception? exception)
    {
        // 1. 状态码命中可配置登录态集合 → 登录态问题
        if (statusCode > 0 && IsLoginExpired(statusCode))
            return FailureKind.LoginExpired;

        // 2. 网络异常类型 → 网络问题
        if (exception is HttpRequestException
            || exception is TaskCanceledException
            || exception is IOException)
            return FailureKind.NetworkError;

        // 3. 其他（未知错误、未传状态码且无异常等）
        return FailureKind.Unknown;
    }

    /// <summary>
    /// req-091：综合判定失败原因（仅异常）。
    /// </summary>
    public FailureKind Classify(Exception? exception)
        => Classify(-1, exception);

    /// <summary>
    /// req-091：综合判定失败原因（仅状态码）。
    /// </summary>
    public FailureKind Classify(int statusCode)
        => Classify(statusCode, null);
}

/// <summary>
/// req-091：用量获取失败原因分类。
/// </summary>
public enum FailureKind
{
    /// <summary>登录态失效（Cookie 过期 / 401 / 403）。触发自动重新登录流程。</summary>
    LoginExpired,

    /// <summary>网络问题（超时 / 断网 / DNS 失败）。按现有错误处理，不触发重新登录。</summary>
    NetworkError,

    /// <summary>其他未知错误（不归类）。</summary>
    Unknown,
}