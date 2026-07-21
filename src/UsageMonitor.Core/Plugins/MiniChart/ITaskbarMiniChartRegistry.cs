namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B2：Taskbar 迷你图注册中心接口。
/// <para>
/// 插件在 <c>OnLoad()</c> / <see cref="IPluginLifecycle.InitializeAsync"/> 中调用
/// <see cref="Register"/> 注册自己的 <see cref="MiniChartDescriptor"/>，
/// Taskbar 渲染层通过 <see cref="GetAll"/> / <see cref="Get"/> 拉取并按 Kind 选模板渲染。
/// </para>
/// <para>
/// 设计参考：
/// <list type="bullet">
///   <item><description>类似 PluginManager 的全局单例模式（App 层持有唯一实例）。</description></item>
///   <item><description>线程安全：Register/Unregister 可能在 UI 线程或插件加载线程调用，使用 ConcurrentDictionary。</description></item>
/// </list>
/// </para>
/// </summary>
public interface ITaskbarMiniChartRegistry
{
    /// <summary>
    /// 注册一个迷你图描述符。同 ProviderId 重复注册时**覆盖**（Latest Wins）。
    /// </summary>
    /// <param name="descriptor">描述符（必填，ProviderId 不能为空）</param>
    /// <returns>是否新注册（true）或覆盖（false）。</returns>
    bool Register(MiniChartDescriptor descriptor);

    /// <summary>注销指定 ProviderId 的描述符。</summary>
    /// <returns>是否存在并被移除。</returns>
    bool Unregister(string providerId);

    /// <summary>获取指定 ProviderId 的描述符，不存在返回 null。</summary>
    MiniChartDescriptor? Get(string providerId);

    /// <summary>获取所有已注册的描述符（按 ProviderId 升序，便于稳定渲染）。</summary>
    IEnumerable<MiniChartDescriptor> GetAll();

    /// <summary>已注册的描述符数量（用于测试 / 调试）。</summary>
    int Count { get; }

    /// <summary>清空所有注册（仅用于测试 / 插件卸载场景）。</summary>
    void Clear();
}