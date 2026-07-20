using System.ComponentModel;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-086：插件设置模型接口。
/// <para>
/// 定义插件设置与 <see cref="ProviderConfig"/> 之间的双向映射、验证和变更通知。
/// 插件可通过实现此接口获得强类型设置支持，同时保持与现有 <see cref="ProviderConfig"/> 的兼容。
/// </para>
/// </summary>
public interface IPluginSettings : INotifyPropertyChanged
{
    /// <summary>
    /// 从 <see cref="ProviderConfig"/> 加载设置值。
    /// </summary>
    /// <param name="config">插件配置（包含用户已保存的键值对）</param>
    void LoadFromConfig(ProviderConfig config);

    /// <summary>
    /// 将当前设置值保存到 <see cref="ProviderConfig"/>。
    /// </summary>
    /// <param name="config">目标配置对象</param>
    void SaveToConfig(ProviderConfig config);

    /// <summary>
    /// 验证当前设置是否有效。
    /// </summary>
    /// <param name="error">验证失败时的错误信息；成功时为 null</param>
    /// <returns>验证是否通过</returns>
    bool Validate(out string? error);

    /// <summary>
    /// 重置为默认值。
    /// </summary>
    void ResetToDefaults();
}
