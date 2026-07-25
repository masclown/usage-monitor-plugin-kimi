using System.IO;
using System.Reflection;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using Xunit;

namespace UsageMonitor.App.Tests._TestSupport;

/// <summary>
/// app-layer-zero-test: 测试用临时目录工具（与 Core.Tests 同模式）。
/// 每个测试用例构造独立目录，Dispose 时递归删除，确保互不污染。
/// </summary>
public sealed class TempDir : IDisposable
{
    private bool _disposed;

    /// <summary>临时目录的完整路径</summary>
    public string Path { get; }

    /// <summary>在系统临时目录下创建形如 UsageMonitor-AppTests-{guid} 的目录。</summary>
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"UsageMonitor-AppTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>拼接子路径并返回完整路径（不创建子目录）。</summary>
    public string Combine(params string[] parts)
    {
        return System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    }

    /// <summary>回收：递归删除整个临时目录及其内容。允许失败。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            /* 清理失败不阻断测试结果 */
        }
    }
}

/// <summary>
/// app-layer-zero-test: 反射辅助（与 Core.Tests 同模式），
/// 用于把 ConfigService 的私有配置路径字段指向测试临时目录。
/// </summary>
public static class ReflectionHelpers
{
    /// <summary>设置实例的私有/非公开字段值。</summary>
    public static void SetField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }
}

/// <summary>
/// app-layer-zero-test: MainViewModel 测试装配工厂。
/// <para>
/// 组合「临时目录 ConfigService + 注册 Fake Provider 的 PluginManager + FakeRefreshService」
/// 构造可断言的 <see cref="MainViewModel"/>，供刷新 / Provider 过滤 / 配置联动测试复用。
/// </para>
/// </summary>
public sealed class MainViewModelHarness : IDisposable
{
    private readonly TempDir _tempDir = new();

    /// <summary>被测 MainViewModel。</summary>
    public MainViewModel ViewModel { get; }

    /// <summary>注入的配置服务（配置文件指向临时目录）。</summary>
    public ConfigService ConfigService { get; }

    /// <summary>注入的插件管理器（含通过 providerIds 注册的 Fake Provider）。</summary>
    public PluginManager PluginManager { get; }

    /// <summary>注入的刷新服务桩。</summary>
    public FakeRefreshService RefreshService { get; }

    /// <summary>注入的数据模块桩（Moq 代理，跟踪 MaxPoints）。</summary>
    public Moq.Mock<UsageMonitor.Core.Modules.IDataModule> DataModule { get; }

    /// <summary>
    /// 创建测试装配：为每个 providerId 注册一个 FakeUsageProvider 并构造 MainViewModel。
    /// </summary>
    /// <param name="providerIds">要注册的 Fake Provider Id 列表（可为空）。</param>
    public MainViewModelHarness(params string[] providerIds)
    {
        ConfigService = new ConfigService();
        ReflectionHelpers.SetField(ConfigService, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(ConfigService, "_configFilePath", _tempDir.Combine("config.json"));

        PluginManager = new PluginManager();
        foreach (var id in providerIds)
            PluginManager.RegisterPlugin(new FakeUsageProvider(id, $"Fake-{id}"));

        RefreshService = new FakeRefreshService();

        DataModule = new Moq.Mock<UsageMonitor.Core.Modules.IDataModule>();
        DataModule.SetupProperty(m => m.MaxPoints);
        DataModule.Setup(m => m.GetHistoryValues(Moq.It.IsAny<string>()))
            .Returns(Array.Empty<double>());

        ViewModel = new MainViewModel(
            PluginManager, ConfigService, RefreshService, DataModule.Object);
    }

    /// <summary>回收：停计时器 + 删除临时目录。</summary>
    public void Dispose()
    {
        ViewModel.StopResetCountdownTimer();
        RefreshService.Dispose();
        _tempDir.Dispose();
    }
}
