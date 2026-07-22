namespace UsageMonitor.Core.Services.Auth;

/// <summary>
/// 鉴权方式枚举 - 定义插件支持的鉴权类型
/// <para>req-096：统一鉴权管理模块，解耦插件鉴权方式。</para>
/// </summary>
public enum AuthKind
{
    /// <summary>API Key 鉴权（如 Deepseek、OpenAI 等纯 API 插件）</summary>
    ApiKey,

    /// <summary>Cookie 鉴权（如 MiniMax、Kimi 等需要浏览器登录的插件）</summary>
    Cookie,

    /// <summary>OAuth 鉴权（预留，未来支持授权码模式）</summary>
    OAuth
}
