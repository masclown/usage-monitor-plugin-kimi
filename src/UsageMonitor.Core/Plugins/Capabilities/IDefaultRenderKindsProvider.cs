namespace UsageMonitor.Core.Plugins;

#pragma warning disable CS1591
/// <summary>
/// req-069 F-08：可选能力接口——折叠可见部件。
/// <para>req-折叠：CollapseVisibleParts 控制折叠态下哪些区段仍可见。</para>
/// <para>req-107 B6：DefaultRenderKinds 已迁移到声明式 CardDeclaration.RenderKinds（defaults.json），本接口仅保留 CollapseVisibleParts。</para>
/// </summary>
public interface IDefaultRenderKindsProvider
{
    /// <summary>
    /// 折叠态下仍可见的部件集合。
    /// 默认 null——折叠态隐藏所有限额/余额/图表。
    /// 插件可返回如 <c>["limitBars"]</c> 让折叠态保留 5h 限额进度条。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<string>? CollapseVisibleParts => null;
}
