using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Auth;

/// <summary>
/// Cookie 鉴权提供者 - 封装现有 BrowserLoginService，管理 Cookie 的获取、验证、刷新
/// <para>req-096：统一鉴权管理模块的 Cookie 鉴权实现。</para>
/// </summary>
public class CookieAuthProvider : IAuthProvider
{
    private readonly BrowserLoginService _browserLoginService;
    private readonly ConfigService _configService;

    /// <summary>
    /// 创建 CookieAuthProvider 实例
    /// </summary>
    /// <param name="browserLoginService">浏览器登录服务，用于获取和刷新 Cookie</param>
    /// <param name="configService">配置服务，用于读取 ProviderConfig 中的 Cookie</param>
    public CookieAuthProvider(BrowserLoginService browserLoginService, ConfigService configService)
    {
        _browserLoginService = browserLoginService ?? throw new ArgumentNullException(nameof(browserLoginService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    /// <inheritdoc/>
    public AuthKind Kind => AuthKind.Cookie;

    /// <inheritdoc/>
    public Task<string?> GetAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // Cookie 存储在两个位置：
        // 1. ProviderConfig.Values["Cookie"]（主配置，DPAPI 加密）
        // 2. cookies/{providerId}.json（BrowserLoginService 保存的完整 Cookie 数据）
        // 优先从主配置读取（与 RefreshService 保持一致）
        var config = _configService.GetProviderConfig(providerId);
        var cookie = config.GetValue("Cookie");
        
        if (!string.IsNullOrWhiteSpace(cookie))
            return Task.FromResult<string?>(cookie);

        // 回退到 BrowserLoginService 的 Cookie 文件
        cookie = BrowserLoginService.GetCookieString(providerId);
        return Task.FromResult(cookie);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // Cookie 验证：调用 BrowserLoginService.CheckCookieValidAsync
        // 需要从 ProviderConfig 读取 BrowserLoginConfig
        var config = _configService.GetProviderConfig(providerId);
        var loginConfigJson = config.GetValue("_loginConfig");
        
        if (string.IsNullOrWhiteSpace(loginConfigJson))
        {
            // 没有登录配置，无法验证
            return false;
        }

        try
        {
            var loginConfig = System.Text.Json.JsonSerializer.Deserialize<BrowserLoginConfig>(loginConfigJson);
            if (loginConfig == null)
                return false;

            loginConfig.ProviderId = providerId;
            return await BrowserLoginService.CheckCookieValidAsync(loginConfig, ct);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // Cookie 刷新：调用 BrowserLoginService.GetOrRefreshCookieAsync
        var config = _configService.GetProviderConfig(providerId);
        var loginConfigJson = config.GetValue("_loginConfig");
        
        if (string.IsNullOrWhiteSpace(loginConfigJson))
        {
            // 没有登录配置，无法刷新
            return false;
        }

        try
        {
            var loginConfig = System.Text.Json.JsonSerializer.Deserialize<BrowserLoginConfig>(loginConfigJson);
            if (loginConfig == null)
                return false;

            loginConfig.ProviderId = providerId;
            var cookieData = await _browserLoginService.GetOrRefreshCookieAsync(loginConfig, ct);
            return cookieData != null;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task SaveAuthDataAsync(string providerId, string accountId, string authData, CancellationToken ct = default)
    {
        // 保存 Cookie 到 ProviderConfig
        var config = _configService.GetProviderConfig(providerId);
        config.SetValue("Cookie", authData);
        _configService.UpdateProviderConfig(providerId, config);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default)
    {
        // 删除 Cookie（同时删除主配置和 Cookie 文件）
        var config = _configService.GetProviderConfig(providerId);
        config.RemoveValue("Cookie");
        config.RemoveValue("_userAgent");
        _configService.UpdateProviderConfig(providerId, config);
        
        BrowserLoginService.DeleteCookieData(providerId);
        return Task.CompletedTask;
    }
}
