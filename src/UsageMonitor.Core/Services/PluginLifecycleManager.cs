namespace UsageMonitor.Core.Services;

/// <summary>
/// req-086：插件生命周期管理器。
/// 管理所有插件的 Initialize → ValidateConfig → Start → Stop → Dispose 流程。
/// </summary>
public class PluginLifecycleManager
{
    private readonly List<Plugins.IPluginLifecycle> _plugins = new();
    private Plugins.PluginContext? _context;

    /// <summary>注册插件到生命周期管理</summary>
    public void Register(Plugins.IPluginLifecycle plugin)
    {
        _plugins.Add(plugin);
    }

    /// <summary>初始化所有插件（启动时调用）</summary>
    public async Task InitializeAllAsync(Plugins.PluginContext context)
    {
        _context = context;
        foreach (var plugin in _plugins)
        {
            try
            {
                await plugin.InitializeAsync(context);
                var error = await plugin.ValidateConfigAsync();
                if (error != null)
                {
                    FileLogger.Warn("PluginLifecycle", $"插件配置验证失败: {error}");
                }
                await plugin.StartAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Error("PluginLifecycle", $"插件初始化失败: {plugin.GetType().Name}", ex);
            }
        }
    }

    /// <summary>停止并释放所有插件（关闭时调用）</summary>
    public async Task ShutdownAllAsync()
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                await plugin.StopAsync();
                await plugin.DisposeAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Error("PluginLifecycle", $"插件关闭失败: {plugin.GetType().Name}", ex);
            }
        }
        _plugins.Clear();
    }
}
