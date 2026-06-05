using System.Reflection;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 插件管理器 - 负责扫描、加载和管理所有插件
/// 从指定目录加载实现了 IUsageProvider 接口的插件DLL
/// </summary>
public class PluginManager
{
    private readonly List<LoadedPlugin> _plugins = new();
    private readonly string _pluginDirectory;

    /// <summary>已加载的插件列表</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins.AsReadOnly();

    /// <summary>插件加载完成事件</summary>
    public event EventHandler? PluginsLoaded;

    /// <summary>
    /// 创建插件管理器实例
    /// </summary>
    /// <param name="pluginDirectory">插件目录路径（默认为程序目录下的plugins文件夹）</param>
    public PluginManager(string? pluginDirectory = null)
    {
        _pluginDirectory = pluginDirectory ?? GetDefaultPluginDirectory();
    }

    /// <summary>
    /// 获取默认插件目录路径
    /// </summary>
    private static string GetDefaultPluginDirectory()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(appDir, "plugins");
    }

    /// <summary>
    /// 扫描并加载plugins目录下的所有插件DLL
    /// </summary>
    public void LoadPlugins()
    {
        _plugins.Clear();

        if (!Directory.Exists(_pluginDirectory))
        {
            Directory.CreateDirectory(_pluginDirectory);
            return;
        }

        var dllFiles = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.AllDirectories);

        foreach (var dllPath in dllFiles)
        {
            try
            {
                LoadPluginFromAssembly(dllPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载插件失败: {dllPath} - {ex.Message}");
            }
        }

        PluginsLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 从指定DLL文件加载插件
    /// </summary>
    private void LoadPluginFromAssembly(string dllPath)
    {
        var assembly = Assembly.LoadFrom(dllPath);
        var providerTypes = assembly.GetTypes()
            .Where(t => typeof(IUsageProvider).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in providerTypes)
        {
            if (Activator.CreateInstance(type) is IUsageProvider provider)
            {
                // 检查是否已存在相同ProviderId的插件
                if (_plugins.Any(p => p.Provider.ProviderId == provider.ProviderId))
                {
                    System.Diagnostics.Debug.WriteLine($"跳过重复插件: {provider.ProviderId}");
                    continue;
                }

                var loadedPlugin = new LoadedPlugin(provider, assembly, dllPath);
                _plugins.Add(loadedPlugin);
                System.Diagnostics.Debug.WriteLine($"已加载插件: {provider.DisplayName} v{provider.Version}");
            }
        }
    }

    /// <summary>
    /// 注册一个内置插件（不通过DLL加载）
    /// </summary>
    public void RegisterPlugin(IUsageProvider provider)
    {
        if (_plugins.Any(p => p.Provider.ProviderId == provider.ProviderId))
            return;

        var loadedPlugin = new LoadedPlugin(provider, provider.GetType().Assembly, string.Empty);
        _plugins.Add(loadedPlugin);
    }

    /// <summary>
    /// 根据ProviderId获取已加载的插件
    /// </summary>
    public LoadedPlugin? GetPlugin(string providerId)
    {
        return _plugins.FirstOrDefault(p => p.Provider.ProviderId == providerId);
    }

    /// <summary>
    /// 获取所有已启用的插件
    /// </summary>
    public IEnumerable<LoadedPlugin> GetEnabledPlugins()
    {
        return _plugins.Where(p => p.IsEnabled);
    }

    /// <summary>
    /// 卸载指定插件
    /// </summary>
    public bool UnloadPlugin(string providerId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Provider.ProviderId == providerId);
        if (plugin == null) return false;

        _plugins.Remove(plugin);
        return true;
    }

    /// <summary>
    /// 重新加载所有插件
    /// </summary>
    public void ReloadPlugins()
    {
        LoadPlugins();
    }
}
