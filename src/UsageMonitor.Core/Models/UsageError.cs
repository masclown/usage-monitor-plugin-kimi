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