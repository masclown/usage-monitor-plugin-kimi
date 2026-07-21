using System.Collections.Concurrent;
using UsageMonitor.Core.Plugins.MiniChart;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Services;

/// <summary>
/// req-088 B2：Taskbar 迷你图注册中心单例实现。
/// <para>
/// 用 App 静态单例模式（与 <see cref="PluginManager"/> 风格一致）保持 Core 项目纯净。
/// 线程安全：使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 存储描述符，
/// Register/Unregister 可在 UI 线程或插件加载线程安全调用。
/// </para>
/// <para>
/// 生命周期：
/// <list type="number">
///   <item><description>App 启动时由 <c>App.xaml.cs</c> 创建实例。</description></item>
///   <item><description>插件在 <c>OnLoad()</c> 中通过 <c>PluginContext.MiniChartRegistry</c> 调用 Register。</description></item>
///   <item><description>TaskbarWindow 渲染时调用 GetAll() 拉取当前所有描述符。</description></item>
///   <item><description>插件卸载（StopAsync）时调用 Unregister 清理。</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class TaskbarMiniChartRegistry : ITaskbarMiniChartRegistry
{
    private readonly ConcurrentDictionary<string, MiniChartDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool Register(MiniChartDescriptor descriptor)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (string.IsNullOrWhiteSpace(descriptor.ProviderId))
            throw new ArgumentException("MiniChartDescriptor.ProviderId 不能为空", nameof(descriptor));

        // req-fix-RegistryIsNewRegistration 修复：原写法 `var old = AddOrUpdate(...)` 是错的——
        // ConcurrentDictionary.AddOrUpdate 返回的是操作完成后字典中的"最终值"（新值），
        // 不是被替换的旧值。所以 `old` 永远等于 `descriptor`，`old == null` 永远为 false，
        // 每次 Register 都被错判为"覆盖"。同时 `ContainsKey` 后置检查有竞态窗口。
        //
        // 正确做法：先 TryAdd（返回 true 即新增成功），否则用索引器 setter 覆盖（内部走 TryUpdate 线程安全）。

        // 1. 快路径：TryAdd 成功即新增
        if (_descriptors.TryAdd(descriptor.ProviderId, descriptor))
        {
            FileLogger.Info(
                "TaskbarMiniChartRegistry",
                $"Register {descriptor.ProviderId} Kind={descriptor.Kind}");
            return true;
        }

        // 2. 慢路径：已存在 → 覆盖（Latest Wins 语义）
        // 索引器 setter 内部用 TryUpdate 实现，线程安全且自动重试
        _descriptors[descriptor.ProviderId] = descriptor;
        FileLogger.Info(
            "TaskbarMiniChartRegistry",
            $"Update {descriptor.ProviderId} Kind={descriptor.Kind}");
        return false;
    }

    /// <inheritdoc />
    public bool Unregister(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return false;
        var removed = _descriptors.TryRemove(providerId, out _);
        if (removed)
        {
            UsageMonitor.Core.Services.FileLogger.Info(
                "TaskbarMiniChartRegistry", $"Unregister {providerId}");
        }
        return removed;
    }

    /// <inheritdoc />
    public MiniChartDescriptor? Get(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        return _descriptors.TryGetValue(providerId, out var d) ? d : null;
    }

    /// <inheritdoc />
    public IEnumerable<MiniChartDescriptor> GetAll()
    {
        // 按 ProviderId 升序，保证渲染顺序稳定（多次刷新顺序一致）
        return _descriptors.Values.OrderBy(d => d.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public int Count => _descriptors.Count;

    /// <inheritdoc />
    public void Clear()
    {
        _descriptors.Clear();
        UsageMonitor.Core.Services.FileLogger.Warn(
            "TaskbarMiniChartRegistry", "Clear: 所有迷你图描述符已清空");
    }
}