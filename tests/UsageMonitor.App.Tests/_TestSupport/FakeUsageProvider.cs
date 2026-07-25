using System.Threading;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.App.Tests._TestSupport;

/// <summary>
/// app-layer-zero-test: 测试用 <see cref="IUsageProvider"/> 桩实现（App 层测试专用精简版）。
/// <para>
/// 与 Core.Tests 的同名类保持相同构造签名，用于向 <c>PluginManager</c> 注册可控 Provider，
/// 避免依赖真实插件的 HTTP 调用与浏览器自动化。
/// </para>
/// </summary>
public sealed class FakeUsageProvider : IUsageProvider
{
    private readonly Func<ProviderConfig, CancellationToken, Task<UsageInfo>>? _getUsageHandler;

    /// <summary>创建一个 FakeUsageProvider。</summary>
    /// <param name="providerId">ProviderId</param>
    /// <param name="displayName">显示名</param>
    /// <param name="getUsageHandler">可选自定义 GetUsage 行为；null 时返回默认成功 UsageInfo。</param>
    public FakeUsageProvider(
        string providerId,
        string displayName = "Fake",
        Func<ProviderConfig, CancellationToken, Task<UsageInfo>>? getUsageHandler = null)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        _getUsageHandler = getUsageHandler;
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string? IconPath => null;
    public string Version => "1.0.0-test";
    public string Author => "test";
    public string Description => "Fake provider for App layer unit tests";

    public IReadOnlyList<ConfigField> ConfigFields => Array.Empty<ConfigField>();

    /// <summary>返回可控的用量结果（默认成功、10/100）。</summary>
    public Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        if (_getUsageHandler != null)
            return _getUsageHandler(config, ct);

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

    /// <summary>配置校验固定通过。</summary>
    public Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
        => Task.FromResult(true);
}
