using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Plugins.MiniChart;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Services;

/// <summary>
/// req-088 B6：4 个现有 Provider 的 MiniChartDescriptor 集中注册入口。
/// <para>
/// 当前实现：在 App 启动时（<c>App.OnStartup</c>）由 <see cref="MiniChartRegistryBootstrapper.RegisterBuiltins"/>
/// 直接遍历已知内置 Provider 并 <see cref="ITaskbarMiniChartRegistry.Register"/>，
/// 不走 <c>PluginLifecycleManager.InitializeAsync</c> 流程（待 req-086 完整生命周期激活后可改造）。</para>
/// <para>设计动机：让 req-088 B1-B3 创建的 MiniChart SDK 真正被 4 个 Provider 使用，
/// 验证注册→渲染链路，为后续 PluginLifecycleManager 集成铺路。</para>
/// <para>
/// req-098：读取 <see cref="ConfigService"/>.<see cref="AppSettings.TaskbarMiniChartConfigs"/>，
/// 按 <c>IsVisible</c> 过滤；用户配置的 <c>ChartKind</c> / <c>ContentKind</c> / <c>ShowLogo</c>
/// 覆盖插件默认 descriptor。Bootstrapper 不再是无依赖的纯静态工具，需在 <c>App.OnStartup</c>
/// 注入 <see cref="ConfigService"/> 实例。
/// </para>
/// </summary>
public static class MiniChartRegistryBootstrapper
{
    /// <summary>
    /// 注册 4 个内置 Provider（MiniMax / DeepSeek / Kimi / Qoder）的 MiniChartDescriptor。
    /// 默认使用 RingChart 类型 + Compact 样式，与 req-051 重构后的视觉一致。
    /// <para>req-098：用户可在「设置 → 任务栏迷你图表」关闭 / 切换特定 Provider 的迷你图，
    /// 本方法读取 <see cref="AppSettings.TaskbarMiniChartConfigs"/> 应用用户偏好。</para>
    /// </summary>
    /// <param name="registry">Taskbar 迷你图注册中心（App 单例）</param>
    /// <param name="configService">配置服务（req-098 注入以读取用户偏好）</param>
    public static int RegisterBuiltins(ITaskbarMiniChartRegistry registry, PluginManager pluginManager, ConfigService? configService = null)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (pluginManager == null) throw new ArgumentNullException(nameof(pluginManager));

        var registered = 0;
        var userConfigs = configService?.Settings.TaskbarMiniChartConfigs
                          ?? new Dictionary<string, TaskbarMiniChartConfig>(StringComparer.OrdinalIgnoreCase);

        // req-099 修复（Bug4）：改为数据驱动——遍历实际加载的插件，用其真实 ProviderId 注册，
        // 只为声明了 SupportedMiniCharts（非空）的插件注册。修复原先硬编码 "deepseek_web/kimi_web/qoder_web"
        // 与实际 ProviderId（deepseek/kimi/qoder）不匹配、导致任务栏出现无数据空环的问题。
        foreach (var plugin in pluginManager.Plugins)
        {
            var provider = plugin.Provider;
            var supported = provider.SupportedMiniCharts;
            // 未声明迷你图能力的插件（如纯 API 的 OpenAI/MiMo）跳过。
            if (supported == null || supported.Count == 0) continue;

            // req-109：优先按 taskbar.miniCharts 声明逐个注册（带 ChartId，供渲染端按 chartId 精确过滤）。
            var taskbar = provider.Taskbar;
            if (taskbar != null && taskbar.MiniCharts.Count > 0)
            {
                foreach (var mini in taskbar.MiniCharts)
                {
                    if (TryBuildDescriptorForMiniChart(provider.ProviderId, mini, userConfigs, out var desc))
                    {
                        registry.Register(desc);
                        registered++;
                    }
                }
                continue;
            }

            // 回退：旧注册路径（单 descriptor，无 ChartId）。
            if (TryBuildDescriptor(provider.ProviderId, userConfigs, out var fallbackDesc))
            {
                registry.Register(fallbackDesc);
                registered++;
            }
        }

        FileLogger.Info(
            "MiniChartRegistryBootstrapper",
            $"Registered {registered} builtin MiniChart descriptors (data-driven by loaded plugins; req-098 user configs applied)");

        return registered;
    }

    /// <summary>
    /// req-098：根据 <paramref name="userConfigs"/> 决定是否注册指定 Provider。
    /// 返回 false 时表示用户关闭了此 Provider 的任务栏迷你图（不调用 Register）。
    /// </summary>
    private static bool TryBuildDescriptor(
        string providerId,
        Dictionary<string, TaskbarMiniChartConfig> userConfigs,
        out MiniChartDescriptor descriptor)
    {
        // 用户未配置或显式 IsVisible=false 时跳过
        if (userConfigs.TryGetValue(providerId, out var cfg) && !cfg.IsVisible)
        {
            FileLogger.Info(
                "MiniChartRegistryBootstrapper",
                $"Skip {providerId} MiniChart (user config IsVisible=false)");
            descriptor = null!;
            return false;
        }

        // 有用户配置则覆盖图类型 / 内容；无配置则用 RingChart + PrimaryMetric 默认值。
        var chartKind = cfg?.ChartKind ?? MiniChartKind.MiniRingChart;
        var contentKind = cfg?.ContentKind ?? MiniChartContentKind.PrimaryMetric;
        var secondaryKind = cfg?.SecondaryKind;
        var showLogo = cfg?.ShowLogo ?? true;

        descriptor = new MiniChartDescriptor
        {
            ProviderId = providerId,
            Kind = chartKind,
            Style = MiniChartStyle.Compact,
            DataSource = (double?)null,
            ColorTier = null, // 传 null 让渲染层走全局 UsageTierScale 色阶
            Tooltip = MiniChartTooltip.Default,
            ContentKind = contentKind,
            SecondaryKind = secondaryKind,
            ShowLogo = showLogo
        };
        return true;
    }

    /// <summary>
    /// req-109：按 <c>taskbar.miniCharts</c> 声明的单个 Mini 图表构建 descriptor（带 ChartId）。
    /// <para>用户配置（<see cref="AppSettings.TaskbarMiniChartConfigs"/>）仍可覆盖内容类型 / Logo；
    /// ChartId 始终来自声明，供渲染端按 chartId 精确过滤。</para>
    /// </summary>
    private static bool TryBuildDescriptorForMiniChart(
        string providerId,
        UsageMonitor.Core.Models.MiniChartDeclaration mini,
        Dictionary<string, TaskbarMiniChartConfig> userConfigs,
        out MiniChartDescriptor descriptor)
    {
        // 用户显式 IsVisible=false 时跳过整个 Provider 的迷你图（与旧路径一致）。
        if (userConfigs.TryGetValue(providerId, out var cfg) && !cfg.IsVisible)
        {
            descriptor = null!;
            return false;
        }

        var contentKind = cfg?.ContentKind ?? MiniChartContentKind.PrimaryMetric;
        var secondaryKind = cfg?.SecondaryKind;
        var showLogo = cfg?.ShowLogo ?? true;

        descriptor = new MiniChartDescriptor
        {
            ProviderId = providerId,
            ChartId = mini.ChartId,
            Kind = MapDeclarativeKindToMiniChartKind(mini.Kind),
            Style = MiniChartStyle.Compact,
            DataSource = (double?)null,
            ColorTier = null,
            Tooltip = MiniChartTooltip.Default,
            ContentKind = contentKind,
            SecondaryKind = secondaryKind,
            ShowLogo = showLogo
        };
        return true;
    }

    /// <summary>
    /// req-109：<see cref="UsageMonitor.Core.Models.DeclarativeChartKind"/> → <see cref="MiniChartKind"/> 映射。
    /// </summary>
    private static MiniChartKind MapDeclarativeKindToMiniChartKind(UsageMonitor.Core.Models.DeclarativeChartKind kind)
        => kind switch
        {
            UsageMonitor.Core.Models.DeclarativeChartKind.MiniRingChart => MiniChartKind.MiniRingChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.Ring => MiniChartKind.MiniRingChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.MiniText => MiniChartKind.MiniText,
            UsageMonitor.Core.Models.DeclarativeChartKind.Line => MiniChartKind.MiniLineChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.Bar => MiniChartKind.MiniBarChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.HeatMap => MiniChartKind.MiniHeatMap,
            _ => MiniChartKind.MiniText,
        };
}