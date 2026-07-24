using FluentAssertions;
using Moq;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-069-004：mock IConfigService 单元测试——验证配置加载失败/异常时消费方的兜底行为。
/// <para>
/// 取舍说明：MainViewModel（IConfigService 的主要消费方）位于 App 项目，Core.Tests 仅引用 Core，
/// 无法直接测试 VM 层。因此本文件分两组覆盖等价路径：
/// <list type="bullet">
///   <item><description>A 组（Moq）：mock IConfigService 模拟加载失败/保存异常，验证 App 层消费方
///     依赖的契约模式（LastLoadError/LastSaveError 检测 + Settings 默认值兜底）可正确工作。</description></item>
///   <item><description>B 组（真实 ConfigService）：验证 Core 层实现确实兑现上述契约——损坏文件兜底、
///     保存失败不抛出、缺失 Provider 返回空配置、默认值回填、ConfigChanged 通知。</description></item>
/// </list>
/// </para>
/// </summary>
public class ConfigServiceConsumerFallbackTests : IDisposable
{
    private readonly TempDir _tempDir;
    private readonly string _configFilePath;

    /// <summary>初始化测试夹具：创建临时目录与配置文件路径。</summary>
    public ConfigServiceConsumerFallbackTests()
    {
        _tempDir = new TempDir();
        _configFilePath = _tempDir.Combine("config.json");
    }

    /// <summary>释放临时目录。</summary>
    public void Dispose() => _tempDir.Dispose();

    /// <summary>构造一个 ConfigService 并把内部路径指向测试目录（避免污染真实 %AppData%）。</summary>
    private ConfigService CreateConfigService()
    {
        var svc = new ConfigService();
        ReflectionHelpers.SetField(svc, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(svc, "_configFilePath", _configFilePath);
        return svc;
    }

    // =====================================================================
    // A 组：Moq mock IConfigService —— 消费方兜底契约验证
    // =====================================================================

    /// <summary>
    /// 模拟消费方（等价于 MainViewModel 的 App 层模式）：调用 Load 后检查 LastLoadError，
    /// 加载失败时回退到默认设置继续运行。
    /// </summary>
    /// <param name="configService">被测 IConfigService 实例（mock 或真实实现）。</param>
    /// <returns>消费方决策结果：(是否有加载错误, 生效的刷新间隔)</returns>
    private static (bool hasError, int refreshInterval) ConsumerLoadWithFallback(IConfigService configService)
    {
        try
        {
            configService.Load();
        }
        catch
        {
            // 消费方对 Load 异常做兜底：不向上抛出，继续用 Settings 当前值
        }

        var hasError = configService.LastLoadError != null;
        // 兜底：无论加载是否成功，Settings 必须可读（默认值兜底）
        var interval = configService.Settings?.RefreshIntervalSeconds ?? 300;
        return (hasError, interval);
    }

    /// <summary>A1：mock Load 失败（设置 LastLoadError + Settings 为默认值）→ 消费方检测到错误并使用默认刷新间隔。</summary>
    [Fact]
    public void Mock_Load_Failure_Consumer_Detects_Error_And_Uses_Defaults()
    {
        var defaultSettings = new AppSettings(); // RefreshIntervalSeconds 默认 300
        string? lastLoadError = null;
        var mock = new Mock<IConfigService>();
        mock.Setup(s => s.LastLoadError).Returns(() => lastLoadError);
        mock.Setup(s => s.Settings).Returns(defaultSettings);
        mock.Setup(s => s.Load()).Callback(() => lastLoadError = "JsonException: config.json 格式损坏");

        var (hasError, interval) = ConsumerLoadWithFallback(mock.Object);

        hasError.Should().BeTrue("消费方应能通过 LastLoadError 检测到加载失败");
        interval.Should().Be(300, "加载失败时消费方应回退到默认刷新间隔");
        mock.Verify(s => s.Load(), Times.Once);
    }

    /// <summary>A2：mock Load 抛出异常 → 消费方吞掉异常并仍能读取 Settings 默认值（不崩溃）。</summary>
    [Fact]
    public void Mock_Load_Throws_Consumer_Swallows_Exception_And_Reads_Defaults()
    {
        var defaultSettings = new AppSettings();
        var mock = new Mock<IConfigService>();
        mock.Setup(s => s.Settings).Returns(defaultSettings);
        mock.Setup(s => s.LastLoadError).Returns((string?)null);
        mock.Setup(s => s.Load()).Throws(new IOException("磁盘不可读"));

        var (hasError, interval) = ConsumerLoadWithFallback(mock.Object);

        hasError.Should().BeFalse("异常被消费方吞掉，LastLoadError 未设置时不误报");
        interval.Should().Be(300, "异常场景下消费方仍应使用默认值兜底");
    }

    /// <summary>A3：mock Save 失败（LastSaveError 非空）→ 消费方检测错误并提示用户，配置在内存中仍有效。</summary>
    [Fact]
    public void Mock_Save_Failure_Consumer_Checks_LastSaveError()
    {
        var settings = new AppSettings();
        string? lastSaveError = null;
        var mock = new Mock<IConfigService>();
        mock.Setup(s => s.LastSaveError).Returns(() => lastSaveError);
        mock.Setup(s => s.Settings).Returns(settings);
        mock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback((Action<AppSettings> m) => m(settings));
        mock.Setup(s => s.Save()).Callback(() => lastSaveError = "UnauthorizedAccessException: 权限不足");

        // 消费方模式（等价于 PluginItemViewModel.OpenConfigDialog 保存后检查）
        mock.Object.UpdateSettings(s => s.RefreshIntervalSeconds = 600);
        mock.Object.Save();

        mock.Object.LastSaveError.Should().NotBeNull("消费方应能检测到保存失败");
        mock.Object.Settings.RefreshIntervalSeconds.Should().Be(600, "保存失败时内存配置仍应有效（本次会话可用）");
    }

    /// <summary>A4：mock GetProviderConfig 对未知 Provider 返回空配置 → 消费方不抛异常、读到空值。</summary>
    [Fact]
    public void Mock_GetProviderConfig_Unknown_Provider_Consumer_Gets_Empty_Config()
    {
        var mock = new Mock<IConfigService>();
        mock.Setup(s => s.GetProviderConfig(It.IsAny<string>(), It.IsAny<IUsageProvider?>()))
            .Returns((string id, IUsageProvider? _) => new ProviderConfig { ProviderId = id });

        var config = mock.Object.GetProviderConfig("NonExistent");

        config.Should().NotBeNull("消费方不应收到 null（兜底为空配置）");
        config.GetValue("ApiKey").Should().BeNull("空配置读任何键应为 null");
    }

    // =====================================================================
    // B 组：真实 ConfigService —— 验证实现兑现消费方兜底契约
    // =====================================================================

    /// <summary>B1：配置文件损坏且无 .bak → Load 不抛异常，LastLoadError 非空，Settings 回退默认值可正常读取。</summary>
    [Fact]
    public void Real_Load_Corrupted_No_Bak_Consumer_Reads_Defaults_Without_Exception()
    {
        File.WriteAllText(_configFilePath, "{ 损坏的 JSON", System.Text.Encoding.UTF8);
        var svc = CreateConfigService();

        var (hasError, interval) = ConsumerLoadWithFallback(svc);

        hasError.Should().BeTrue("损坏文件应设置 LastLoadError");
        interval.Should().Be(300, "无 .bak 时应回退到 AppSettings 默认值");
    }

    /// <summary>B2：加载失败后消费方仍可继续 UpdateSettings + Save（恢复路径不阻塞）。</summary>
    [Fact]
    public void Real_After_Load_Failure_Consumer_Can_Still_Update_And_Save()
    {
        File.WriteAllText(_configFilePath, "{ corrupted", System.Text.Encoding.UTF8);
        var svc = CreateConfigService();
        svc.Load();
        svc.LastLoadError.Should().NotBeNull();

        // 消费方恢复路径：修改配置并保存（应覆盖损坏文件）
        svc.UpdateSettings(s => s.RefreshIntervalSeconds = 450);
        svc.Save();

        svc.LastSaveError.Should().BeNull("恢复保存不应失败");
        var reloaded = CreateConfigService();
        reloaded.Load();
        reloaded.Settings.RefreshIntervalSeconds.Should().Be(450);
    }

    /// <summary>B3：Save 写入阶段目标路径被同名目录占据 → 不抛异常，LastSaveError 非空，内存状态保持完好。</summary>
    [Fact]
    public void Real_Save_With_Blocked_Target_Path_Sets_LastSaveError_Without_Throwing()
    {
        var svc = CreateConfigService();
        svc.Load(); // 正常创建配置文件

        // 模拟磁盘异常：删除配置文件并创建同名目录，File.Move 将抛 IOException
        File.Delete(_configFilePath);
        File.Delete(_configFilePath + ".bak");
        Directory.CreateDirectory(_configFilePath);

        svc.UpdateSettings(s => s.RefreshIntervalSeconds = 999);
        var act = () => svc.Save();

        act.Should().NotThrow("Save 失败不应向消费方抛出异常");
        svc.LastSaveError.Should().NotBeNull("消费方应能通过 LastSaveError 检测写入失败");
        svc.Settings.RefreshIntervalSeconds.Should().Be(999, "保存失败时内存配置仍有效");
    }

    /// <summary>B4：GetProviderConfig 对不存在的 Provider 返回非 null 空配置（消费方兜底路径）。</summary>
    [Fact]
    public void Real_GetProviderConfig_Unknown_Provider_Returns_Empty_Config()
    {
        var svc = CreateConfigService();
        svc.Load();

        var config = svc.GetProviderConfig("UnknownProvider");

        config.Should().NotBeNull();
        config.GetValue("Anything").Should().BeNull();
    }

    /// <summary>B5：GetProviderConfig 按插件 ConfigFields 声明回填 DefaultValue（消费方无需预填配置）。</summary>
    [Fact]
    public void Real_GetProviderConfig_Backfills_Defaults_From_Plugin_Declaration()
    {
        var svc = CreateConfigService();
        svc.Load();
        var provider = new DefaultsProvidingFake();

        var config = svc.GetProviderConfig("FakeWithDefaults", provider);

        config.GetValue("Region").Should().Be("global", "缺失字段应回填插件声明的 DefaultValue");
    }

    /// <summary>B6：UpdateProviderConfig 触发 ConfigChanged 事件（消费方订阅通知路径）。</summary>
    [Fact]
    public void Real_UpdateProviderConfig_Raises_ConfigChanged()
    {
        var svc = CreateConfigService();
        svc.Load();
        var raised = false;
        svc.ConfigChanged += (_, _) => raised = true;

        var cfg = new ProviderConfig { ProviderId = "MiniMax" };
        cfg.SetValue("Region", "Global");
        svc.UpdateProviderConfig("MiniMax", cfg);

        raised.Should().BeTrue("消费方应收到配置变更通知");
    }

    /// <summary>测试用插件桩：声明带 DefaultValue 的 ConfigFields（验证默认值回填）。</summary>
    private sealed class DefaultsProvidingFake : IUsageProvider
    {
        public string ProviderId => "FakeWithDefaults";
        public string DisplayName => "Fake With Defaults";
        public string? IconPath => null;
        public string Version => "1.0.0-test";
        public string Author => "test";
        public string Description => "带默认值声明的测试插件";

        /// <summary>声明一个带 DefaultValue 的字段。</summary>
        public IReadOnlyList<ConfigField> ConfigFields => new[]
        {
            new ConfigField("Region", "区域", ConfigFieldType.Text, defaultValue: "global")
        };

        /// <summary>返回默认成功结果（本测试不使用）。</summary>
        public Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
            => Task.FromResult(new UsageInfo { ProviderId = ProviderId, IsSuccess = true });

        /// <summary>始终返回 true（本测试不使用）。</summary>
        public Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
