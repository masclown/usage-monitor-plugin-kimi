namespace UsageMonitor.Core.Models;

/// <summary>
/// 插件运行模式枚举 - 定义插件的计费模式
/// <para>req-101：插件运行模式声明，卡片按模式显示不同 UI。</para>
/// </summary>
public enum ProviderMode
{
    /// <summary>API 模式（按量付费，显示余额）</summary>
    Api,

    /// <summary>Token Plan 模式（订阅制，显示档位）</summary>
    TokenPlan
}
