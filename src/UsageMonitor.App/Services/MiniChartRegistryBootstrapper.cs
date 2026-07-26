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
    /// 注册已加载 Provider 的 MiniChartDescriptor（数据驱动，不硬编码具体 Provider）。
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

        // req-099 修复（Bug4）/ Stage E：数据驱动——遍历实际加载的插件，用其真实 ProviderId 注册。
        // 优先按 taskbar.miniCharts 声明注册（带 ChartId）；无声明的插件回退默认单 descriptor
        // （旧 SupportedMiniCharts 接口成员已随 Stage E 删除）。
        foreach (var plugin in pluginManager.Plugins)
        {
            var provider = plugin.Provider;

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
            // 问题20：声明的内联色阶（thresholds/colors）转换为私有色阶；ref 引用全局色阶时保持 null（渲染层回退 UsageTierScale）。
            // req-115：ref="pack:<packId>" 时按图表类型从样式包解析。
            ColorTier = ConvertDeclaredColorTiers(mini.ColorTiers, mini.Kind.ToString()),
            Tooltip = MiniChartTooltip.Default,
            ContentKind = contentKind,
            SecondaryKind = secondaryKind,
            ShowLogo = showLogo,
            // req-107 B4：透传数据组与切片器声明，供渲染端（MiniChartItemViewModel）滚轮切组。
            DataGroups = mini.DataGroups.Count > 0 ? mini.DataGroups : null,
            Slicer = mini.Slicer,
            // 迷你时序图表：透传插件声明宽度（用户设置覆盖优先级更高，见 TaskbarWindow.ResolveUserMiniChartWidth）。
            DeclaredWidth = mini.Width,
            // 问题8：透传声明的 tooltip.fields（SDK/虚拟字段名），作为用户未配置时的默认字段集。
            DeclaredTooltipFields = mini.Tooltip?.Fields is { Count: > 0 } declaredFields ? declaredFields : null
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
            UsageMonitor.Core.Models.DeclarativeChartKind.MiniLineChart => MiniChartKind.MiniLineChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.MiniBarChart => MiniChartKind.MiniBarChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.MiniAreaChart => MiniChartKind.MiniAreaChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.Line => MiniChartKind.MiniLineChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.Bar => MiniChartKind.MiniBarChart,
            UsageMonitor.Core.Models.DeclarativeChartKind.HeatMap => MiniChartKind.MiniHeatMap,
            _ => MiniChartKind.MiniText,
        };

    /// <summary>
    /// 问题20：把声明的内联色阶（<see cref="UsageMonitor.Core.Models.ColorTierSpec"/> 的 thresholds/colors）
    /// 转换为迷你图私有色阶 <see cref="MiniChartColorTier"/>。
    /// <para>ref 引用全局色阶 / 声明缺失 / 解析失败时返回 null（渲染层回退全局 UsageTierScale）。
    /// req-115：ref 支持 "pack:&lt;packId&gt;" 形态——从 minicharts/ 或 charts/ 样式包取对应图表类型（回退 "usage"）色阶。</para>
    /// </summary>
    private static MiniChartColorTier? ConvertDeclaredColorTiers(UsageMonitor.Core.Models.ColorTierSpec? spec)
        => ConvertDeclaredColorTiers(spec, null);

    /// <summary>同上，携带图表类型名供 pack: 引用按类型取样式条目。</summary>
    /// <param name="spec">色阶声明。</param>
    /// <param name="chartKindName">迷你图类型名（如 "MiniRingChart"，可空）。</param>
    private static MiniChartColorTier? ConvertDeclaredColorTiers(UsageMonitor.Core.Models.ColorTierSpec? spec, string? chartKindName)
    {
        if (spec == null) return null;
        if (!string.IsNullOrEmpty(spec.Ref))
        {
            // req-115："pack:<packId>" 引用 → 从显示资源包解析；其余 ref（如全局色阶）保持 null 交给全局
            const string packPrefix = "pack:";
            if (spec.Ref!.StartsWith(packPrefix, StringComparison.OrdinalIgnoreCase))
                return ResolvePackColorTier(spec.Ref.Substring(packPrefix.Length), chartKindName);
            return null;
        }
        if (spec.Thresholds.Count == 0 || spec.Colors.Count == 0) return null;
        try
        {
            var tiers = new List<UsageMonitor.Core.Models.UsageTierConfig>();
            var count = Math.Min(spec.Thresholds.Count, spec.Colors.Count);
            // MiniChartColorTier 限制 1-6 档，超出部分截断
            for (var i = 0; i < count && i < 6; i++)
            {
                var hex = spec.Colors[i]?.TrimStart('#');
                if (string.IsNullOrEmpty(hex)) continue;
                if (hex.Length == 6) hex = "FF" + hex; // 默认不透明
                if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var argb))
                    continue;
                tiers.Add(new UsageMonitor.Core.Models.UsageTierConfig
                {
                    MinPercent = spec.Thresholds[i],
                    ColorArgb = argb,
                    IsEnabled = true
                });
            }
            return tiers.Count > 0 ? new MiniChartColorTier(tiers) : null;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MiniChartRegistryBootstrapper", $"声明色阶转换失败，回退全局色阶：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// req-115：从显示资源包解析 pack 色阶：优先 minicharts/ mini 样式包，回退 charts/ 图表样式包；
    /// 包内条目按图表类型名取，缺失回退 "usage" 条目；均未命中 / 解析失败返回 null（回退全局色阶）。
    /// </summary>
    /// <param name="packId">样式包 Id。</param>
    /// <param name="chartKindName">图表类型名（如 "MiniRingChart"，可空）。</param>
    private static MiniChartColorTier? ResolvePackColorTier(string packId, string? chartKindName)
    {
        try
        {
            var registry = (System.Windows.Application.Current as UsageMonitor.App.App)?.DisplayPacks;
            if (registry == null || string.IsNullOrWhiteSpace(packId)) return null;

            UsageMonitor.Core.Services.Display.ChartStyleEntry? entry = null;
            var miniPack = registry.GetMiniChartStylePack(packId);
            if (miniPack != null
                && (chartKindName == null || !miniPack.ChartStyles.TryGetValue(chartKindName, out entry)))
                miniPack.ChartStyles.TryGetValue("usage", out entry);
            if (entry == null)
            {
                var chartPack = registry.GetChartStylePack(packId);
                if (chartPack != null
                    && (chartKindName == null || !chartPack.ChartStyles.TryGetValue(chartKindName, out entry)))
                    chartPack.ChartStyles.TryGetValue("usage", out entry);
            }

            var tiers = UsageMonitor.Core.Services.Display.DisplayPackConverters.ToUsageTiers(entry);
            if (tiers == null || tiers.Count == 0) return null;
            // MiniChartColorTier 限制 1-6 档，超出部分截断
            return new MiniChartColorTier(tiers.Take(6).ToList());
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MiniChartRegistryBootstrapper", $"pack 色阶解析失败（{packId}），回退全局色阶：{ex.Message}");
            return null;
        }
    }
}