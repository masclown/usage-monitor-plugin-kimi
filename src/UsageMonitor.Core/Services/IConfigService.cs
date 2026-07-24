using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-069 F-10：配置服务接口——为 DI 容器做准备。
/// 定义配置管理的核心契约，包括读写、变更通知、Provider 配置访问。
/// </summary>
public interface IConfigService
{
    /// <summary>当前应用配置（注意：直接修改字段不经过线程安全锁，并发场景应使用 <see cref="UpdateSettings"/>）</summary>
    AppSettings Settings { get; }

    /// <summary>配置文件完整路径</summary>
    string ConfigFilePath { get; }

    /// <summary>配置变更事件</summary>
    event EventHandler? ConfigChanged;

    /// <summary>上次 Save() 失败的错误信息（null 表示成功）</summary>
    string? LastSaveError { get; }

    /// <summary>上次 Load() 失败的错误信息（null 表示成功）</summary>
    string? LastLoadError { get; }

    /// <summary>加载配置文件</summary>
    void Load();

    /// <summary>保存配置文件</summary>
    void Save();

    /// <summary>线程安全的配置修改 API。在锁内执行 mutator，避免与并发 Save/Load 产生竞态。</summary>
    /// <param name="mutator">在锁内执行的修改委托</param>
    void UpdateSettings(Action<AppSettings> mutator);

    /// <summary>从磁盘重新加载 Provider 配置（用于外部修改 config.json 后同步）</summary>
    void ReloadProviderConfigsFromDisk();

    /// <summary>获取指定 Provider 的配置（不存在时返回默认配置）</summary>
    /// <param name="providerId">Provider 唯一标识</param>
    /// <param name="provider">可选的 Provider 实例，用于从插件声明中读取默认值</param>
    ProviderConfig GetProviderConfig(string providerId, IUsageProvider? provider = null);

    /// <summary>更新指定 Provider 的配置</summary>
    /// <param name="providerId">Provider 唯一标识</param>
    /// <param name="config">新的配置值</param>
    void UpdateProviderConfig(string providerId, ProviderConfig config);

    /// <summary>设置全局用量色阶配置</summary>
    /// <param name="tiers">色阶列表</param>
    void SetUsageTierConfig(IReadOnlyList<UsageTierConfig> tiers);
}
