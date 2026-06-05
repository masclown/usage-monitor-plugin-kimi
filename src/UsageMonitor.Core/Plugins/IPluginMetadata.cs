namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 插件元数据接口 - 提供插件的描述性信息
/// IUsageProvider 已包含这些属性，此接口可作为独立的元数据契约使用
/// </summary>
public interface IPluginMetadata
{
    /// <summary>插件唯一标识</summary>
    string PluginId { get; }

    /// <summary>插件显示名称</summary>
    string Name { get; }

    /// <summary>插件版本</summary>
    string Version { get; }

    /// <summary>插件作者</summary>
    string Author { get; }

    /// <summary>插件描述</summary>
    string Description { get; }

    /// <summary>插件项目地址（可选）</summary>
    string? ProjectUrl { get; }
}
