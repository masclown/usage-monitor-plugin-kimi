namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-069 F-08：可选能力接口——插件自定义刷新策略。
/// <para>req-102：插件可覆盖此属性以声明自己的刷新策略（例如 MiniMax 的 5h 限额需频繁刷新）。
/// 宿主 <c>RefreshService</c> 按插件声明的策略执行刷新。</para>
/// <para>req-107 B6 渐进拆分（F-08 ISP 原则第三步）：<see cref="IUsageProvider"/> 继承此接口，
/// <see cref="RefreshPolicy"/> 成员由本接口承载。
/// 旧插件继续实现 <see cref="IUsageProvider"/> 即可（继承得到默认 null 实现）。</para>
/// </summary>
public interface IRefreshPolicyProvider
{
    /// <summary>
    /// 插件刷新策略——声明插件自定义的刷新频率/规则。
    /// 默认 null 表示使用全局刷新间隔（<c>AppSettings.RefreshIntervalSeconds</c>）。
    /// </summary>
    Models.RefreshPolicy? RefreshPolicy => null;
}
