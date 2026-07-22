using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Auth;

/// <summary>
/// API Key 鉴权提供者 - 封装现有 API Key 逻辑，从 ProviderConfig 读取
/// <para>req-096：统一鉴权管理模块的 API Key 鉴权实现。</para>
/// </summary>
public class ApiKeyAuthProvider : IAuthProvider
{
    private readonly ConfigService _configService;

    /// <summary>
    /// 创建 ApiKeyAuthProvider 实例
    /// </summary>
    /// <param name="configService">配置服务，用于读取 ProviderConfig 中的 ApiKey</param>
    public ApiKeyAuthProvider(ConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    /// <inheritdoc/>
    public AuthKind Kind => AuthKind.ApiKey;

    /// <inheritdoc/>
    public Task<string?> GetAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // API Key 存储在 ProviderConfig.Values["ApiKey"] 中
        var config = _configService.GetProviderConfig(providerId);
        var apiKey = config.GetValue("ApiKey");
        return Task.FromResult(apiKey);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // API Key 验证：检查是否存在且非空
        var apiKey = await GetAuthDataAsync(providerId, accountId, ct);
        return !string.IsNullOrWhiteSpace(apiKey);
    }

    /// <inheritdoc/>
    public Task<bool> RefreshAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // API Key 通常不需要刷新（长期有效），直接返回验证结果
        return ValidateAsync(providerId, accountId, ct);
    }

    /// <inheritdoc/>
    public Task SaveAuthDataAsync(string providerId, string accountId, string authData, CancellationToken ct = default)
    {
        // 保存 API Key 到 ProviderConfig
        var config = _configService.GetProviderConfig(providerId);
        config.SetValue("ApiKey", authData);
        _configService.UpdateProviderConfig(providerId, config);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // 删除 API Key
        var config = _configService.GetProviderConfig(providerId);
        config.RemoveValue("ApiKey");
        _configService.UpdateProviderConfig(providerId, config);
        return Task.CompletedTask;
    }
}
