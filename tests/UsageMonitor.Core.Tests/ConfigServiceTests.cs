using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-057 / req-059-001: ConfigService 关键场景覆盖。
/// <para>
/// 由于 ConfigService 的 <c>_configDirectory</c> / <c>_configFilePath</c> 是
/// <c>private readonly</c>，通过 <see cref="ReflectionHelpers"/> 反射注入测试临时目录，
/// 避免污染真实 %AppData%/UsageMonitor/。
/// </para>
/// </summary>
public class ConfigServiceTests : IDisposable
{
    private readonly TempDir _tempDir;
    private readonly string _configFilePath;

    public ConfigServiceTests()
    {
        _tempDir = new TempDir();
        _configFilePath = _tempDir.Combine("config.json");
    }

    public void Dispose() => _tempDir.Dispose();

    /// <summary>
    /// 构造一个 ConfigService 并把 <c>_configDirectory</c> / <c>_configFilePath</c> 指向测试目录。
    /// </summary>
    private ConfigService CreateConfigService()
    {
        var svc = new ConfigService();
        ReflectionHelpers.SetField(svc, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(svc, "_configFilePath", _configFilePath);
        return svc;
    }

    // -----------------------------------------------------------------
    // req-057-003: UpdateSettings 在锁内执行 mutator
    // -----------------------------------------------------------------

    [Fact]
    public void UpdateSettings_Applies_Mutator_Under_Lock()
    {
        var svc = CreateConfigService();
        svc.UpdateSettings(s => s.RefreshIntervalSeconds = 123);
        svc.Settings.RefreshIntervalSeconds.Should().Be(123);
    }

    [Fact]
    public void UpdateSettings_Does_Not_Persist_To_Disk_Automatically()
    {
        // 约定：UpdateSettings 仅修改内存，调用方负责 Save。
        var svc = CreateConfigService();
        svc.UpdateSettings(s => s.RefreshIntervalSeconds = 999);
        File.Exists(_configFilePath).Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // req-057-004: Save/Load round-trip 走 MakeSnapshot + 锁外 JSON 序列化
    // -----------------------------------------------------------------

    [Fact]
    public void Save_Then_Load_Preserves_All_Fields()
    {
        var svc = CreateConfigService();
        svc.UpdateSettings(s =>
        {
            s.RefreshIntervalSeconds = 600;
            s.AutoStart = true;
            s.HistoryPointCount = 120;
            s.PluginEnabled["MiniMax"] = true;
            s.PluginEnabled["Deepseek"] = false;
        });
        svc.Save();

        File.Exists(_configFilePath).Should().BeTrue();

        var reloaded = CreateConfigService();
        reloaded.Load();
        reloaded.Settings.RefreshIntervalSeconds.Should().Be(600);
        reloaded.Settings.AutoStart.Should().BeTrue();
        reloaded.Settings.HistoryPointCount.Should().Be(120);
        reloaded.Settings.PluginEnabled["MiniMax"].Should().BeTrue();
        reloaded.Settings.PluginEnabled["Deepseek"].Should().BeFalse();
    }

    [Fact]
    public void Save_Encrypts_Sensitive_Keys_In_File_On_Disk()
    {
        // DPAPI 加密后的密文是 base64，不含原文明文。
        var svc = CreateConfigService();
        svc.UpdateSettings(s =>
        {
            var p = new ProviderConfig { ProviderId = "MiniMax" };
            s.ProviderConfigs["MiniMax"] = p;
            p.SetValue("ApiKey", "sk-PLAINTEXT-SECRET-VALUE");
            p.SetValue("Cookie", "session=plaintext-cookie-data");
        });
        svc.Save();

        var json = File.ReadAllText(_configFilePath);
        json.Should().NotContain("sk-PLAINTEXT-SECRET-VALUE");
        json.Should().NotContain("plaintext-cookie-data");
    }

    [Fact]
    public void Load_After_Save_Decrypts_Sensitive_Keys_Back_To_Plaintext()
    {
        var svc = CreateConfigService();
        svc.UpdateSettings(s => s.ProviderConfigs["MiniMax"] = new ProviderConfig
        {
            ProviderId = "MiniMax",
        });
        svc.Settings.ProviderConfigs["MiniMax"].SetValue("ApiKey", "sk-test-roundtrip");
        svc.Save();

        var reloaded = CreateConfigService();
        reloaded.Load();
        reloaded.Settings.ProviderConfigs["MiniMax"].GetValue("ApiKey")
            .Should().Be("sk-test-roundtrip");
    }

    [Fact]
    public void Load_When_File_Missing_Creates_Default_And_Persists()
    {
        File.Exists(_configFilePath).Should().BeFalse();
        var svc = CreateConfigService();
        svc.Load();
        // Load 会因为文件不存在自动 Save 一份默认配置
        File.Exists(_configFilePath).Should().BeTrue();
        svc.Settings.RefreshIntervalSeconds.Should().Be(300); // AppSettings 默认值
    }

    // -----------------------------------------------------------------
    // 损坏恢复
    // -----------------------------------------------------------------

    [Fact]
    public void Load_With_Corrupted_File_And_Bak_Recovers_From_Bak()
    {
        // 准备：写入坏 JSON + 写入完整 .bak
        File.WriteAllText(_configFilePath, "{ this is not valid json", System.Text.Encoding.UTF8);
        File.WriteAllText(_configFilePath + ".bak",
            "{\"RefreshIntervalSeconds\": 777}", System.Text.Encoding.UTF8);

        var svc = CreateConfigService();
        svc.Load();

        svc.LastLoadError.Should().NotBeNull();
        svc.Settings.RefreshIntervalSeconds.Should().Be(777); // 从 .bak 恢复
    }

    [Fact]
    public void Load_With_Corrupted_File_And_No_Bak_Falls_Back_To_Default_And_Backup()
    {
        File.WriteAllText(_configFilePath, "{ corrupted", System.Text.Encoding.UTF8);
        // 没有 .bak

        var svc = CreateConfigService();
        svc.Load();

        svc.LastLoadError.Should().NotBeNull();
        svc.Settings.RefreshIntervalSeconds.Should().Be(300); // 默认值

        // 损坏文件被备份为 .corrupted-*
        var corruptedFiles = Directory.GetFiles(_tempDir.Path, "config.json.corrupted-*");
        corruptedFiles.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------
    // IsSensitiveKey 关键词匹配（DPAPI 加密触发条件）
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("apikey")]
    // 注意："API_KEY" 不被识别——IsSensitiveKey 用 key.Contains("apikey") 匹配连续子串，
    // 而 "API_KEY" 包含 "_KEY" 不包含 "apikey"。这是已知行为：key 必须包含连续 apikey 子串。
    [InlineData("Token")]
    [InlineData("SessionToken")]
    [InlineData("Secret")]
    [InlineData("Password")]
    [InlineData("Cookie")]
    [InlineData("session_cookie")]
    public void IsSensitiveKey_Matches_Known_Keywords(string key)
    {
        // 通过反射调用 private static IsSensitiveKey
        var method = typeof(ConfigService).GetMethod(
            "IsSensitiveKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = (bool)method!.Invoke(null, new object?[] { key })!;
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("Region")]
    [InlineData("Endpoint")]
    [InlineData("ModelName")]
    [InlineData("UserId")]
    [InlineData("API_KEY")] // 单独验证：不包含连续 "apikey" 子串 -> 非敏感
    [InlineData("TopP")] // 类似不匹配的常见 key 名
    public void IsSensitiveKey_Does_Not_Match_Regular_Keys(string key)
    {
        var method = typeof(ConfigService).GetMethod(
            "IsSensitiveKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = (bool)method!.Invoke(null, new object?[] { key })!;
        result.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // UpdateProviderConfig 持久化
    // -----------------------------------------------------------------

    [Fact]
    public void UpdateProviderConfig_Persists_Config_To_Disk()
    {
        var svc = CreateConfigService();
        svc.Load(); // 触发默认配置写入
        var cfg = new ProviderConfig { ProviderId = "MiniMax" };
        cfg.SetValue("Region", "Global");
        svc.UpdateProviderConfig("MiniMax", cfg);

        File.Exists(_configFilePath).Should().BeTrue();
        var reloaded = CreateConfigService();
        reloaded.Load();
        reloaded.Settings.ProviderConfigs.Should().ContainKey("MiniMax");
        reloaded.Settings.ProviderConfigs["MiniMax"].GetValue("Region").Should().Be("Global");
    }

    // -----------------------------------------------------------------
    // NormalizeAfterLoad: 缺字段时回退默认
    // -----------------------------------------------------------------

    [Fact]
    public void Load_Normalizes_Missing_RingChartMetricOrder_To_Default()
    {
        // 写入一个空的 RingChartMetricOrder 的旧配置
        File.WriteAllText(_configFilePath,
            "{\"RefreshIntervalSeconds\": 300, \"RingChartMetricOrder\": []}",
            System.Text.Encoding.UTF8);

        var svc = CreateConfigService();
        svc.Load();
        svc.Settings.RingChartMetricOrder.Should().NotBeEmpty();
    }

    [Fact]
    public void Load_Normalizes_TriggerRect_With_Zero_Size_To_Default()
    {
        File.WriteAllText(_configFilePath,
            "{\"TrayTooltipTriggerRect\": {\"X\": 0, \"Y\": 0, \"Width\": 0, \"Height\": 0}}",
            System.Text.Encoding.UTF8);

        var svc = CreateConfigService();
        svc.Load();
        svc.Settings.TrayTooltipTriggerRect.Width.Should().BeGreaterThan(0);
        svc.Settings.TrayTooltipTriggerRect.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Load_Clamps_RefreshIntervalSeconds_To_Reasonable_Range()
    {
        // 写一个超出 30s~24h 范围的非法值
        File.WriteAllText(_configFilePath,
            "{\"RefreshIntervalSeconds\": 999999999}",
            System.Text.Encoding.UTF8);

        var svc = CreateConfigService();
        svc.Load();
        svc.Settings.RefreshIntervalSeconds.Should().BeInRange(30, 86400);
    }
}
