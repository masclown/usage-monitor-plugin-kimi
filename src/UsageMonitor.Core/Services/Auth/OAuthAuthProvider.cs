using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Auth;

/// <summary>
/// OAuth 鉴权提供者 - 预留 OAuth 流程框架，未来支持授权码模式
/// <para>req-096：统一鉴权管理模块的 OAuth 鉴权框架（暂未实现）。</para>
/// </summary>
public class OAuthAuthProvider : IAuthProvider
{
    private readonly ConfigService _configService;

    /// <summary>
    /// 创建 OAuthAuthProvider 实例
    /// </summary>
    /// <param name="configService">配置服务，用于读取 ProviderConfig 中的 OAuth Token</param>
    public OAuthAuthProvider(ConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    /// <inheritdoc/>
    public AuthKind Kind => AuthKind.OAuth;

    /// <inheritdoc/>
    public Task<string?> GetAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // OAuth Token 存储在 ProviderConfig.Values["OAuthToken"] 中
        var config = _configService.GetProviderConfig(providerId);
        var token = config.GetValue("OAuthToken");
        return Task.FromResult(token);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // OAuth Token 验证：检查是否存在且未过期
        var token = await GetAuthDataAsync(providerId, accountId, ct);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        // TODO: 未来实现 Token 过期检查
        // var expiresAt = config.GetValue("OAuthTokenExpiresAt");
        // if (DateTime.TryParse(expiresAt, out var expiry) && expiry < DateTime.Now)
        //     return false;

        return true;
    }

    /// <inheritdoc/>
    public Task<bool> RefreshAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // TODO: 未来实现 OAuth Token 刷新流程
        // 1. 读取 RefreshToken
        // 2. 调用 OAuth 端点刷新 AccessToken
        // 3. 保存新 Token
        throw new NotImplementedException("OAuth Token 刷新功能暂未实现，等待后续版本支持");
    }

    /// <inheritdoc/>
    public Task SaveAuthDataAsync(string providerId, string accountId, string authData, CancellationToken ct = default)
    {
        // 保存 OAuth Token 到 ProviderConfig
        var config = _configService.GetProviderConfig(providerId);
        config.SetValue("OAuthToken", authData);
        _configService.UpdateProviderConfig(providerId, config);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // 删除 OAuth Token
        var config = _configService.GetProviderConfig(providerId);
        config.RemoveValue("OAuthToken");
        config.RemoveValue("OAuthRefreshToken");
        config.RemoveValue("OAuthTokenExpiresAt");
        _configService.UpdateProviderConfig(providerId, config);
        return Task.CompletedTask;
    }
}
