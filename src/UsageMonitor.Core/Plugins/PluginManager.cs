using System.Reflection;
using System.Security.Cryptography;
using UsageMonitor.Core.Security;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 插件管理器 - 负责扫描、加载和管理所有插件。
/// <para>Stage E（完全声明式插件架构）：主通道为“纯声明包扫描”——plugins/&lt;包名&gt;/ 目录下含
/// plugin.json / defaults.json 等清单文件即以通用 <see cref="DeclarativeProvider"/> 实例化注册（零 DLL，
/// JSON 不可执行代码，无白名单问题）；DLL 通道（<see cref="AllowExternalPlugins"/> + SHA256 白名单）
/// 保留给未来可能的二进制插件，默认关闭。</para>
/// </summary>
public class PluginManager
{
    /// <summary>req-057：插件列表读写保护锁，避免 ReloadPlugins 与 GetEnabledPlugins/GetPlugin 并发竞态。</summary>
    private readonly object _pluginsLock = new();
    private readonly List<LoadedPlugin> _plugins = new();
    private readonly string _pluginDirectory;

    /// <summary>已加载的插件列表（线程安全快照）</summary>
    public IReadOnlyList<LoadedPlugin> Plugins
    {
        get { lock (_pluginsLock) { return _plugins.ToList().AsReadOnly(); } }
    }

    /// <summary>req-111/114：插件扫描根目录（供目录监视器与安装器定位 plugins/ 位置）。</summary>
    public string PluginDirectory => _pluginDirectory;

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
    /// 扫描并加载plugins目录下的所有插件。
    /// <para>Stage E：先扫描声明包（plugins/*/ 含清单文件即以 <see cref="DeclarativeProvider"/> 注册）；
    /// DLL 通道默认禁用，需显式 <see cref="AllowExternalPlugins"/> = true 且命中 SHA256 白名单。
    /// req-057：加锁保护避免与并发枚举竞态。</para>
    /// </summary>
    public void LoadPlugins()
    {
        lock (_pluginsLock)
        {
            _plugins.Clear();
        }

        if (!Directory.Exists(_pluginDirectory))
        {
            Directory.CreateDirectory(_pluginDirectory);
        }

        // Stage E：声明包扫描（主通道，零 DLL）。
        LoadDeclarativePackages();

        if (!AllowExternalPlugins)
        {
            FileLogger.Info("PluginManager", "外部插件DLL扫描已禁用（AllowExternalPlugins=false），仅使用声明包插件");
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

    /// <summary>Stage A 声明包清单文件名（任一存在即视为声明包目录）。</summary>
    private static readonly string[] DeclarativeManifestFiles = { "plugin.json", "fetch.json", "display.json", "defaults.json" };

    /// <summary>
    /// Stage E：扫描 plugins/&lt;包名&gt;/ 声明包目录，合并校验清单后以 <see cref="DeclarativeProvider"/> 注册。
    /// <para>校验失败或缺 providerId 的包跳过（PluginDefaultsLoader 已记日志）；目录隔离天然避免
    /// 根目录 defaults.json 多包覆盖冲突。</para>
    /// </summary>
    private void LoadDeclarativePackages()
    {
        string[] packageDirs;
        try
        {
            packageDirs = Directory.GetDirectories(_pluginDirectory);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("PluginManager", $"声明包目录枚举失败: {ex.Message}");
            return;
        }

        var loaded = 0;
        // req-116：重扫前清除插件命名空间旧词条，避免已卸载插件的文案残留；
        // 随后逐包先注册语言包再解析清单，保证 manifest 里的 i18n: 键能解析到译文。
        I18n.UnregisterByPrefix("plugin.");
        foreach (var dir in packageDirs)
        {
            try
            {
                if (!DeclarativeManifestFiles.Any(f => File.Exists(Path.Combine(dir, f)))) continue;

                // req-116：先注册声明包自带语言包（i18n/<lang>.json）
                PluginLanguagePackLoader.LoadAndRegister(dir);

                var manifest = PluginDefaultsLoader.LoadFromDirectory(dir);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.ProviderId))
                {
                    FileLogger.Warn("PluginManager", $"声明包校验失败或缺 providerId，跳过: {dir}");
                    continue;
                }

                var provider = new DeclarativeProvider(manifest, dir);
                RegisterSensitiveConfigKeys(provider);
                lock (_pluginsLock)
                {
                    if (_plugins.Any(p => p.Provider.ProviderId == provider.ProviderId))
                    {
                        FileLogger.Warn("PluginManager", $"跳过重复声明包: {provider.ProviderId} ({dir})");
                        continue;
                    }
                    _plugins.Add(new LoadedPlugin(provider, typeof(DeclarativeProvider).Assembly, dir));
                }
                loaded++;
                FileLogger.Info("PluginManager", $"已加载声明包插件: {provider.DisplayName} v{provider.Version} from {Path.GetFileName(dir)}");
            }
            catch (Exception ex)
            {
                FileLogger.Error("PluginManager", $"声明包加载失败: {dir} - {ex.Message}", ex);
            }
        }
        FileLogger.Info("PluginManager", $"声明包扫描完成：共加载 {loaded} 个纯声明插件");
    }

    /// <summary>
    /// 从指定DLL文件加载插件。
    /// 计算DLL的SHA256哈希并与白名单比对，拒绝未授权DLL。
    /// req-057：加锁保护 _plugins 写入。
    /// </summary>
    private void LoadPluginFromAssembly(string dllPath)
    {
        var fileHash = ComputeFileSha256(dllPath);
        FileLogger.Info("PluginManager", $"插件DLL: {Path.GetFileName(dllPath)}, SHA256={fileHash}");

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
                RegisterSensitiveConfigKeys(provider);
                lock (_pluginsLock)
                {
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
    /// 把插件声明的敏感配置键（Sensitive=true 或 Password 类型字段）注入全局敏感键注册表，
    /// 供 ConfigService 落盘加密判定；失败不阻断插件加载（仅降级为关键词兜底）。
    /// </summary>
    /// <param name="provider">已实例化的插件。</param>
    private static void RegisterSensitiveConfigKeys(IUsageProvider provider)
    {
        try
        {
            SensitiveConfigKeyRegistry.RegisterFields(provider.ConfigFields);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("PluginManager", $"注册敏感配置键失败（{provider.ProviderId}）: {ex.Message}");
        }
    }

    /// <summary>
    /// 注册一个内置插件（不通过DLL加载）。req-057：加锁保护。
    /// </summary>
    public void RegisterPlugin(IUsageProvider provider)
    {
        RegisterSensitiveConfigKeys(provider);
        lock (_pluginsLock)
        {
            if (_plugins.Any(p => p.Provider.ProviderId == provider.ProviderId))
            {
                FileLogger.Warn("PluginManager", $"跳过重复插件: {provider.ProviderId} ({provider.DisplayName})");
                return;
            }

            var loadedPlugin = new LoadedPlugin(provider, provider.GetType().Assembly, string.Empty);
            _plugins.Add(loadedPlugin);
            FileLogger.Info("PluginManager", $"已注册内置插件: {provider.DisplayName} v{provider.Version} (ProviderId={provider.ProviderId})");
        }
    }

    /// <summary>
    /// 根据ProviderId获取已加载的插件。req-057：加锁保护。
    /// </summary>
    public LoadedPlugin? GetPlugin(string providerId)
    {
        lock (_pluginsLock)
        {
            return _plugins.FirstOrDefault(p => p.Provider.ProviderId == providerId);
        }
    }

    /// <summary>
    /// 获取所有已启用的插件。req-057：加锁保护并返回快照，避免枚举期间被修改。
    /// </summary>
    public IEnumerable<LoadedPlugin> GetEnabledPlugins()
    {
        lock (_pluginsLock)
        {
            return _plugins.Where(p => p.IsEnabled).ToList();
        }
    }

    /// <summary>
    /// 卸载指定插件。req-057：加锁保护。
    /// </summary>
    public bool UnloadPlugin(string providerId)
    {
        lock (_pluginsLock)
        {
            var plugin = _plugins.FirstOrDefault(p => p.Provider.ProviderId == providerId);
            if (plugin == null) return false;

            _plugins.Remove(plugin);
            return true;
        }
    }

    /// <summary>
    /// 重新加载所有插件
    /// </summary>
    public void ReloadPlugins()
    {
        LoadPlugins();
    }
}
