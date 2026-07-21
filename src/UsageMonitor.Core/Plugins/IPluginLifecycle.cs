namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-086：插件生命周期管理接口。
/// 定义插件从初始化到销毁的完整生命周期钩子。
/// </summary>
public interface IPluginLifecycle
{
    /// <summary>初始化插件（加载配置、准备资源）</summary>
    Task InitializeAsync(PluginContext context);

    /// <summary>验证配置有效性，返回错误信息（null 表示有效）</summary>
    Task<string?> ValidateConfigAsync();

    /// <summary>启动插件（开始定时任务、注册事件等）</summary>
    Task StartAsync();

    /// <summary>停止插件（取消定时任务、注销事件等）</summary>
    Task StopAsync();

    /// <summary>释放插件资源</summary>
    Task DisposeAsync();
}

/// <summary>
/// 插件上下文：提供共享资源访问。
/// </summary>
public class PluginContext
{
    /// <summary>配置服务</summary>
    public required Services.ConfigService ConfigService { get; init; }

    /// <summary>日志服务（FileLogger 为静态类，直接通过类型调用）</summary>
    public Type LoggerType => typeof(Services.FileLogger);

    /// <summary>共享 HttpClient（插件可复用）</summary>
    public HttpClient SharedHttpClient { get; } = new();

    /// <summary>插件专属数据目录（%AppData%\UsageMonitor\plugins\{ProviderId}\）</summary>
    public string PluginDataDir { get; }

    /// <summary>
    /// req-088 B2：Taskbar 迷你图注册中心（可选）。
    /// 插件在 OnLoad / InitializeAsync 中调用 MiniChartRegistry.Register() 注册自己的迷你图描述符，
    /// TaskbarWindow 渲染时会自动拉取并按 Kind 选模板渲染。
    /// <para>App 启动时注入；单元测试场景下可能为 null，调用方需做 null 检查。</para>
    /// </summary>
    public MiniChart.ITaskbarMiniChartRegistry? MiniChartRegistry { get; init; }

    public PluginContext()
    {
        PluginDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsageMonitor", "plugins");
    }
}
