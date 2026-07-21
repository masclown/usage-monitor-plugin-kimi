using UsageMonitor.Core.Plugins.MiniChart;

namespace UsageMonitor.App.Services;

/// <summary>
/// req-088 B6：4 个现有 Provider 的 MiniChartDescriptor 集中注册入口。
/// <para>
/// 当前实现：在 App 启动时（<c>App.OnStartup</c>）由 <see cref="MiniChartRegistryBootstrapper.RegisterBuiltins"/>
/// 直接遍历已知内置 Provider 并 <see cref="ITaskbarMiniChartRegistry.Register"/>，
/// 不走 <c>PluginLifecycleManager.InitializeAsync</c> 流程（待 req-086 完整生命周期激活后可改造）。</para>
/// <para>
/// 设计动机：让 req-088 B1-B3 创建的 MiniChart SDK 真正被 4 个 Provider 使用，
/// 验证注册→渲染链路，为后续 PluginLifecycleManager 集成铺路。</para>
/// <para>
/// 迁移路径：未来 req-086 PluginLifecycleManager 激活后，本类可移除，
/// 由每个 Provider 在 override <c>InitializeAsync(PluginContext)</c> 中通过
/// <c>context.MiniChartRegistry.Register()</c> 自助注册。</para>
/// </summary>
public static class MiniChartRegistryBootstrapper
{
    /// <summary>
    /// 注册 4 个内置 Provider（MiniMax / DeepSeek / Kimi / Qoder）的 MiniChartDescriptor。
    /// 默认使用 RingChart 类型 + Compact 样式，与 req-051 重构后的视觉一致。
    /// </summary>
    /// <param name="registry">Taskbar 迷你图注册中心（App 单例）</param>
    public static int RegisterBuiltins(ITaskbarMiniChartRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        var registered = 0;
        // MiniMax：折叠时仍显示限额进度条，Taskbar 用 RingChart 显示主进度
        registry.Register(MiniChartDescriptor.ForRingChart("MiniMax"));
        registered++;

        // DeepSeek（双模式网页版本标识 deepseek_web）
        registry.Register(MiniChartDescriptor.ForRingChart("deepseek_web"));
        registered++;

        // Kimi（双模式网页版本标识 kimi_web）
        registry.Register(MiniChartDescriptor.ForRingChart("kimi_web"));
        registered++;

        // Qoder
        registry.Register(MiniChartDescriptor.ForRingChart("qoder_web"));
        registered++;

        // 纯 API Key 模式（MiniMax / DeepSeek / Kimi / MiMo / OpenAI 不注册 MiniChart，
        // 因为 Taskbar 仅对网页模式有意义。MiMo / OpenAI 不走浏览器，无需注册）。

        UsageMonitor.Core.Services.FileLogger.Info(
            "MiniChartRegistryBootstrapper",
            $"Registered {registered} builtin MiniChart descriptors");

        return registered;
    }
}