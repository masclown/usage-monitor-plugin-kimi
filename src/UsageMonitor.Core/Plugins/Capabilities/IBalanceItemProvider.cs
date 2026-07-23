namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-069 F-08：可选能力接口——插件为余额快照区域提供的额外数据项。
/// <para>req-008：插件可通过此属性提供 <see cref="Models.BalanceItem"/> 集合（覆盖/追加/隐藏默认项）。</para>
/// <para>req-107 B6 渐进拆分（F-08 ISP 原则第四步）：<see cref="IUsageProvider"/> 继承此接口，
/// <see cref="BalanceItems"/> 成员由本接口承载。
/// 旧插件继续实现 <see cref="IUsageProvider"/> 即可（继承得到默认空集合实现）。</para>
/// </summary>
public interface IBalanceItemProvider
{
    /// <summary>
    /// 插件为余额快照区域提供的额外数据项。
    /// 默认返回空集合——主窗口组装 VM 会按内置默认 4 项（累计 / 峰值 / 活跃 / 积分余额）填充。
    /// 插件可返回非空集合以覆盖同名项、追加额外项或隐藏默认项。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<Models.BalanceItem> BalanceItems => System.Array.Empty<Models.BalanceItem>();
}
