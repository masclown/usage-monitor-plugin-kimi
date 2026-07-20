namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-086：插件基类，提供默认生命周期实现和统一错误处理。
/// 插件异常隔离：所有生命周期方法捕获异常并写 FileLogger（带插件前缀）。
/// </summary>
public abstract class PluginBase : IPluginLifecycle
{
    private PluginContext? _context;

    /// <summary>插件上下文（InitializeAsync 后可用）</summary>
    protected PluginContext Context => _context
        ?? throw new InvalidOperationException("Plugin not initialized. Call InitializeAsync first.");

    /// <summary>插件 ProviderId（子类必须实现）</summary>
    public abstract string ProviderId { get; }

    /// <summary>插件显示名称（子类必须实现）</summary>
    public abstract string DisplayName { get; }

    /// <summary>日志前缀</summary>
    protected string LogPrefix => $"[{ProviderId}]";

    /// <inheritdoc />
    public virtual async Task InitializeAsync(PluginContext context)
    {
        _context = context;
        LogInfo("插件初始化");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task<string?> ValidateConfigAsync()
    {
        await Task.CompletedTask;
        return null; // 默认无验证错误
    }

    /// <inheritdoc />
    public virtual async Task StartAsync()
    {
        LogInfo("插件启动");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task StopAsync()
    {
        LogInfo("插件停止");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task DisposeAsync()
    {
        LogInfo("插件释放");
        await Task.CompletedTask;
    }

    /// <summary>写信息日志（带插件前缀）</summary>
    protected void LogInfo(string message)
        => Services.FileLogger.Info(LogPrefix, message);

    /// <summary>写警告日志（带插件前缀）</summary>
    protected void LogWarn(string message, Exception? ex = null)
        => Services.FileLogger.Warn(LogPrefix, message, ex);

    /// <summary>写错误日志（带插件前缀）</summary>
    protected void LogError(string message, Exception? ex = null)
        => Services.FileLogger.Error(LogPrefix, message, ex);
}
