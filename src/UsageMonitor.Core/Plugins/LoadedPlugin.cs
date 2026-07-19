using System.Reflection;
using System.Threading;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 已加载插件的包装器 - 持有插件实例及其运行时状态
/// <para>
/// req-066 A4：状态字段加 volatile/Interlocked 保护，避免 RefreshService 后台线程写 + UI 线程读时的竞态。
/// </para>
/// </summary>
public class LoadedPlugin
{
    /// <summary>插件实例</summary>
    public IUsageProvider Provider { get; }

    /// <summary>插件程序集</summary>
    public Assembly Assembly { get; }

    /// <summary>插件DLL文件路径</summary>
    public string FilePath { get; }

    // req-066 A4：使用 volatile 保护引用类型字段，避免 UI 线程读到半构造对象
    private UsageInfo? _lastUsage;
    private volatile bool _lastQuerySuccess;
    private volatile bool _isEnabled = true;
    private long _lastQueryTimeTicks;  // DateTime 用 long 存储以实现原子读写

    /// <summary>最近一次查询的用量信息</summary>
    public UsageInfo? LastUsage
    {
        get => Volatile.Read(ref _lastUsage);
        set => Volatile.Write(ref _lastUsage, value);
    }

    /// <summary>最近一次查询时间</summary>
    public DateTime? LastQueryTime
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastQueryTimeTicks);
            return ticks == 0 ? null : new DateTime(ticks);
        }
        set => Interlocked.Exchange(ref _lastQueryTimeTicks, value?.Ticks ?? 0);
    }

    /// <summary>最近一次查询是否成功</summary>
    public bool LastQuerySuccess
    {
        get => _lastQuerySuccess;
        set => _lastQuerySuccess = value;
    }

    /// <summary>插件是否已启用</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public LoadedPlugin(IUsageProvider provider, Assembly assembly, string filePath)
    {
        Provider = provider;
        Assembly = assembly;
        FilePath = filePath;
    }
}
