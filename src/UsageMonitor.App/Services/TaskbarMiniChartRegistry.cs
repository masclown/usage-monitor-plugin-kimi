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

    /// <summary>
    /// 构建注册键：ProviderId + ChartId 复合键。
    /// <para>修复：旧实现仅以 ProviderId 为键，同一 Provider 声明多个 mini 图表（如 MiniMax 的 ring + text）时，
    /// 后注册者会覆盖先注册者，导致任务栏只能显示最后一个图表（选中 ring 不显示）。改为复合键后多图共存。</para>
    /// <para>ChartId 为空时回退仅用 ProviderId（兼容旧单描述符注册路径）。</para>
    /// </summary>
    private static string MakeKey(MiniChartDescriptor descriptor)
        => string.IsNullOrWhiteSpace(descriptor.ChartId)
            ? descriptor.ProviderId
            : $"{descriptor.ProviderId}|{descriptor.ChartId}";

    /// <inheritdoc />
    public bool Register(MiniChartDescriptor descriptor)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (string.IsNullOrWhiteSpace(descriptor.ProviderId))
            throw new ArgumentException("MiniChartDescriptor.ProviderId 不能为空", nameof(descriptor));

        // 以 ProviderId+ChartId 复合键注册，同一 Provider 的多个 mini 图表互不覆盖。
        var key = MakeKey(descriptor);
        if (_descriptors.TryAdd(key, descriptor))
        {
            FileLogger.Info(
                "TaskbarMiniChartRegistry",
                $"Register {descriptor.ProviderId} ChartId={descriptor.ChartId ?? "-"} Kind={descriptor.Kind}");
            return true;
        }

        // 已存在 → 覆盖（Latest Wins 语义，仅针对同一复合键）
        _descriptors[key] = descriptor;
        FileLogger.Info(
            "TaskbarMiniChartRegistry",
            $"Update {descriptor.ProviderId} ChartId={descriptor.ChartId ?? "-"} Kind={descriptor.Kind}");
        return false;
    }

    /// <inheritdoc />
    public bool Unregister(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return false;
        // 移除该 Provider 名下的全部描述符（含带 ChartId 的多个 mini 图表）。
        var removed = false;
        foreach (var kv in _descriptors)
        {
            if (string.Equals(kv.Value.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                removed |= _descriptors.TryRemove(kv.Key, out _);
        }
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
        // 向后兼容：返回该 Provider 名下的首个描述符（多图场景建议用 GetAll 按 ChartId 精确过滤）。
        return _descriptors.Values.FirstOrDefault(d =>
            string.Equals(d.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IEnumerable<MiniChartDescriptor> GetAll()
    {
        // 按 ProviderId 、再按 ChartId 升序，保证渲染顺序稳定（多次刷新顺序一致）
        return _descriptors.Values
            .OrderBy(d => d.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.ChartId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
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