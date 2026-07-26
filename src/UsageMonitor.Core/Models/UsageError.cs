using System;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 用量查询错误类型（REQ-005 SDK）。独立为可选字段后，错误状态不再需要
/// <c>IsSuccess:bool + ErrorMessage:string</c> 双字段，也方便 UI 按枚举路由"网络错误 / 鉴权失败 / 限流"等不同提示。
/// </summary>
public sealed class UsageError
{
    /// <summary>错误种类。</summary>
    public UsageErrorKind Kind { get; init; } = UsageErrorKind.Unknown;

    /// <summary>
    /// req-116：稳定错误码（如 <see cref="UsageErrorCodes.CredentialMissing"/>）。
    /// <para>供插件 errorGuidance 的 <c>matchCodes</c> 精确匹配，去除对本地化错误文案的关键字耦合；
    /// null = 未分类（仅能走关键字匹配）。</para>
    /// </summary>
    public string? Code { get; init; }

    /// <summary>人类可读的简短描述（用于 UI 与日志；不包含敏感凭据）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>HTTP 状态码（网络错误时赋值；非网络错误为 null）。</summary>
    public int? HttpStatus { get; init; }

    /// <summary>错误发生时间（UTC），便于在 UI 上排序和排查。</summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>构造一个错误实例（推荐用命名参数）。</summary>
    public UsageError() { }

    /// <summary>构造一个错误实例。</summary>
    public UsageError(UsageErrorKind kind, string message, int? httpStatus = null)
    {
        Kind = kind;
        Message = message;
        HttpStatus = httpStatus;
    }

    /// <summary>创建「网络错误」实例。</summary>
    public static UsageError Network(string message, int? httpStatus = null)
        => new(UsageErrorKind.Network, message, httpStatus);

    /// <summary>创建「鉴权失败」实例。</summary>
    public static UsageError Auth(string message)
        => new(UsageErrorKind.Auth, message);

    /// <summary>创建「限流 / 429」实例。</summary>
    public static UsageError RateLimit(string message)
        => new(UsageErrorKind.RateLimit, message);

    /// <summary>创建「数据解析失败」实例。</summary>
    public static UsageError Parse(string message)
        => new(UsageErrorKind.Parse, message);

    /// <summary>创建「未知错误」实例。</summary>
    public static UsageError Unknown(string message)
        => new(UsageErrorKind.Unknown, message);
}

/// <summary>
/// req-116：稳定错误码常量（宿主/声明式运行器生成失败态时填入 <see cref="UsageError.Code"/>）。
/// <para>插件 errorGuidance 声明用 <c>matchCodes</c> 匹配这些稳定标识，不再依赖宿主错误文案的具体措辞/语言。</para>
/// </summary>
public static class UsageErrorCodes
{
    /// <summary>未配置凭据（Cookie / API Key 缺失）。</summary>
    public const string CredentialMissing = "credential_missing";

    /// <summary>凭据无效（登录态失效 / Key 错误）。</summary>
    public const string AuthInvalid = "auth_invalid";

    /// <summary>网络错误（连接失败 / DNS 等）。</summary>
    public const string NetworkError = "network_error";

    /// <summary>请求超时。</summary>
    public const string Timeout = "timeout";

    /// <summary>用户主动取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>取数成功但无主指标数据（多为登录态失效）。</summary>
    public const string DataEmpty = "data_empty";

    /// <summary>插件声明/配置缺失（如缺 fetch 节）。</summary>
    public const string ConfigMissing = "config_missing";
}

/// <summary>错误种类（与 <see cref="UsageError"/> 配套）。</summary>
public enum UsageErrorKind
{
    /// <summary>未知 / 未分类。</summary>
    Unknown = 0,
    /// <summary>网络错误（DNS / 连接失败 / 超时等）。</summary>
    Network = 1,
    /// <summary>鉴权失败（API Key 错 / Cookie 失效等）。</summary>
    Auth = 2,
    /// <summary>被服务端限流（429 等）。</summary>
    RateLimit = 3,
    /// <summary>返回数据格式异常（JSON 解析失败 / 字段缺失）。</summary>
    Parse = 4,
    /// <summary>用户配置错误（如必填字段缺失）。</summary>
    Config = 5
}