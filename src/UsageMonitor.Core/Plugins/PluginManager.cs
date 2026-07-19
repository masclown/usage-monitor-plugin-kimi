using System.Reflection;
using System.Security.Cryptography;
using UsageMonitor.Core.Services;

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
    /// 是否允许加载外部插件DLL（默认 false，仅内置插件）。
    /// 通过配置文件 Plugins:AllowExternalPlugins = true 开启。
    /// </summary>
    public static bool AllowExternalPlugins { get; set; } = false;

    /// <summary>
    /// 外部插件DLL的SHA256白名单（小写十六进制，无连字符）。
    /// 仅当 <see cref="AllowExternalPlugins"/> 为 true 时生效。
    /// 通过配置文件 Plugins:AllowedPluginHashes 设置。
    /// </summary>
    public static HashSet<string> AllowedPluginHashes { get; } = new(StringComparer.OrdinalIgnoreCase);

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
    /// 扫描并加载plugins目录下的所有插件DLL。
    /// 默认禁用外部DLL扫描（安全考虑），需显式设置 <see cref="AllowExternalPlugins"/> = true。
    /// 启用时，仅加载 <see cref="AllowedPluginHashes"/> 白名单中的DLL。
    /// </summary>
    public void LoadPlugins()
    {
        _plugins.Clear();

        if (!AllowExternalPlugins)
        {
            FileLogger.Info("PluginManager", "外部插件DLL扫描已禁用（AllowExternalPlugins=false），仅使用内置插件");
            PluginsLoaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!Directory.Exists(_pluginDirectory))
        {
            Directory.CreateDirectory(_pluginDirectory);
            PluginsLoaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        var dllFiles = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.AllDirectories);
        FileLogger.Info("PluginManager", $"扫描到 {dllFiles.Length} 个外部插件DLL");

        foreach (var dllPath in dllFiles)
        {
            try
            {
                LoadPluginFromAssembly(dllPath);
            }
            catch (Exception ex)
            {
                FileLogger.Error("PluginManager", $"加载插件失败: {dllPath} - {ex.Message}", ex);
            }
        }

        PluginsLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 从指定DLL文件加载插件。
    /// 计算DLL的SHA256哈希并与白名单比对，拒绝未授权DLL。
    /// </summary>
    private void LoadPluginFromAssembly(string dllPath)
    {
        // 计算DLL文件SHA256哈希
        var fileHash = ComputeFileSha256(dllPath);
        FileLogger.Info("PluginManager", $"插件DLL: {Path.GetFileName(dllPath)}, SHA256={fileHash}");

        // 白名单校验
        if (AllowedPluginHashes.Count > 0 && !AllowedPluginHashes.Contains(fileHash))
        {
            FileLogger.Warn("PluginManager", $"插件DLL哈希不在白名单中，拒绝加载: {dllPath}");
            return;
        }

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
                    FileLogger.Warn("PluginManager", $"跳过重复插件: {provider.ProviderId}");
                    continue;
                }

                var loadedPlugin = new LoadedPlugin(provider, assembly, dllPath);
                _plugins.Add(loadedPlugin);
                FileLogger.Info("PluginManager", $"已加载插件: {provider.DisplayName} v{provider.Version} from {Path.GetFileName(dllPath)}");
            }
        }
    }

    /// <summary>
    /// 计算文件的SHA256哈希值（小写十六进制，无连字符）
    /// </summary>
    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
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
