using System.Threading;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Tests._TestSupport;

/// <summary>
/// req-059-001: 测试用 <see cref="IUsageProvider"/> 桩实现。
/// <para>
/// 用于在 RefreshService / PluginManager / ConfigService 等场景下注入可控行为，
/// 避免依赖真实插件（Deepseek / MiMo / MiniMax 等）的 HTTP 调用与浏览器自动化。
/// </para>
/// </summary>
public sealed class FakeUsageProvider : IUsageProvider
{
    private readonly Func<ProviderConfig, CancellationToken, Task<UsageInfo>>? _getUsageHandler;
    private readonly Func<ProviderConfig, CancellationToken, Task<bool>>? _validateHandler;

    /// <summary>由测试设置的最近一次 RefreshService 写入的 ProviderConfig（供断言）。</summary>
    public ProviderConfig? LastReceivedConfig { get; private set; }

    /// <summary>由测试累计的 GetUsageAsync 调用次数。</summary>
    public int GetUsageCallCount { get; private set; }

    /// <summary>创建一个 FakeUsageProvider。</summary>
    /// <param name="providerId">ProviderId</param>
    /// <param name="displayName">显示名</param>
    /// <param name="getUsageHandler">可选自定义 GetUsage 行为；null 时返回默认成功 UsageInfo。</param>
    /// <param name="validateHandler">可选自定义 Validate 行为；null 时返回 true。</param>
    public FakeUsageProvider(
        string providerId,
        string displayName = "Fake",
        Func<ProviderConfig, CancellationToken, Task<UsageInfo>>? getUsageHandler = null,
        Func<ProviderConfig, CancellationToken, Task<bool>>? validateHandler = null)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        _getUsageHandler = getUsageHandler;
        _validateHandler = validateHandler;
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string? IconPath => null;
    public string Version => "1.0.0-test";
    public string Author => "test";
    public string Description => "Fake provider for unit tests";

    public IReadOnlyList<ConfigField> ConfigFields => Array.Empty<ConfigField>();

    public Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        GetUsageCallCount++;
        LastReceivedConfig = config;
        if (_getUsageHandler != null)
            return _getUsageHandler(config, ct);

        // 默认返回成功的 UsageInfo：占位百分比。
        return Task.FromResult(new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            UsedAmount = 10,
            TotalAmount = 100,
            Unit = "USD",
            IsSuccess = true,
            LastUpdated = DateTime.Now,
        });
    }

    public Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        if (_validateHandler != null)
            return _validateHandler(config, ct);
        return Task.FromResult(true);
    }
}
