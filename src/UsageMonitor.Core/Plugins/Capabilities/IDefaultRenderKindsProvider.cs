namespace UsageMonitor.Core.Plugins;

#pragma warning disable CS1591
/// <summary>
/// req-069 F-08：可选能力接口——默认渲染能力 + 折叠可见部件。
/// <para>req-098：DefaultRenderKinds 让插件声明首屏渲染能力集合，避免"数据未到则卡片残缺"。
/// req-折叠：CollapseVisibleParts 控制折叠态下哪些区段仍可见。</para>
/// <para>req-107 B6 渐进拆分（F-08 ISP 原则第五步）：<see cref="IUsageProvider"/> 继承此接口，
/// <see cref="DefaultRenderKinds"/> 与 <see cref="CollapseVisibleParts"/> 成员由本接口承载。
/// 旧插件继续实现 <see cref="IUsageProvider"/> 即可（继承得到默认空集合/null 实现）。</para>
/// </summary>
public interface IDefaultRenderKindsProvider
{
    /// <summary>
    /// 插件声明的默认渲染能力集合（在首次加载、未收到任何刷新数据前生效）。
    /// 默认返回空集合——主窗口装配 VM 时按内置默认行为显示。
    /// 插件应声明一组最常声明的能力，主窗口装配 VM 时立即写入 RenderKinds，
    /// 让首屏显示与数据到位后的显示保持一致。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<string> DefaultRenderKinds => System.Array.Empty<string>();

    /// <summary>
    /// 折叠态下仍可见的部件集合。
    /// 默认 null——折叠态隐藏所有限额/余额/图表。
    /// 插件可返回如 <c>["limitBars"]</c> 让折叠态保留 5h 限额进度条。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<string>? CollapseVisibleParts => null;
}
