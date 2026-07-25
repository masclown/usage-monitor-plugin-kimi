using System.Reflection;
using FluentAssertions;
using UsageMonitor.Core.Services;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// ci-no-quality-gate: 架构依赖方向检查（不引入第三方架构测试包，用程序集引用反射实现）。
/// <para>
/// 项目架构规则：依赖方向只能向 Core 收敛（App → Core ← Plugins）。
/// Core 作为契约与服务中心，绝不允许反向引用展示层（App）或任何插件实现（Plugin.*）。
/// 违反时本测试失败，CI 构建即阻塞。
/// </para>
/// </summary>
public class ArchitectureTests
{
    /// <summary>
    /// Core 程序集不得引用 App 或任何 Plugin 程序集（依赖倒置：插件依赖 Core 的 SDK 契约，而非相反）。
    /// </summary>
    [Fact]
    public void Core_Assembly_Does_Not_Reference_App_Or_Plugins()
    {
        var coreAssembly = typeof(ConfigService).Assembly;

        var forbidden = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name =>
                name.Equals("UsageMonitor.App", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("UsageMonitor.Plugin.", StringComparison.OrdinalIgnoreCase)
                || name.Equals("UsageMonitor.LoginHelper", StringComparison.OrdinalIgnoreCase))
            .ToList();

        forbidden.Should().BeEmpty(
            "Core 是契约中心，依赖方向只能向 Core 收敛；发现反向引用：{0}",
            string.Join(", ", forbidden));
    }

    /// <summary>
    /// Core 程序集内不得出现 WPF 展示层类型引用（PresentationFramework 等），
    /// 防止 UI 逻辑渗入服务层导致不可测试。
    /// </summary>
    [Fact]
    public void Core_Assembly_Does_Not_Reference_Wpf_Presentation()
    {
        var coreAssembly = typeof(ConfigService).Assembly;

        var wpfRefs = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name =>
                name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PresentationCore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        wpfRefs.Should().BeEmpty(
            "Core 不得依赖 WPF 展示层程序集；发现：{0}",
            string.Join(", ", wpfRefs));
    }
}
