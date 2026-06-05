using System.Reflection;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 已加载插件的包装器 - 持有插件实例及其运行时状态
/// </summary>
public class LoadedPlugin
{
    /// <summary>插件实例</summary>
    public IUsageProvider Provider { get; }

    /// <summary>插件程序集</summary>
    public Assembly Assembly { get; }

    /// <summary>插件DLL文件路径</summary>
    public string FilePath { get; }

    /// <summary>最近一次查询的用量信息</summary>
    public UsageInfo? LastUsage { get; set; }

    /// <summary>最近一次查询时间</summary>
    public DateTime? LastQueryTime { get; set; }

    /// <summary>最近一次查询是否成功</summary>
    public bool LastQuerySuccess { get; set; }

    /// <summary>插件是否已启用</summary>
    public bool IsEnabled { get; set; } = true;

    public LoadedPlugin(IUsageProvider provider, Assembly assembly, string filePath)
    {
        Provider = provider;
        Assembly = assembly;
        FilePath = filePath;
    }
}
