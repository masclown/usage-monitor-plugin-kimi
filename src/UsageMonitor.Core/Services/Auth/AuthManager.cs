using System.Collections.Concurrent;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services.Auth;

/// <summary>
/// 统一鉴权管理器 - 根据插件声明的 SupportedAuthKinds 自动选择鉴权方式
/// <para>req-096：统一管理所有插件的鉴权数据获取、验证、刷新。</para>
/// </summary>
public class AuthManager
{
    private readonly Dictionary<AuthKind, IAuthProvider> _providers;
    private readonly ConcurrentDictionary<string, LoginStateInfo> _loginStates = new();
    private readonly ConfigService _configService;

    /// <summary>
    /// 创建 AuthManager 实例
    /// </summary>
    /// <param name="configService">配置服务</param>
    /// <param name="browserLoginService">浏览器登录服务（Cookie 鉴权需要）</param>
    public AuthManager(ConfigService configService, BrowserLoginService? browserLoginService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        // 注册所有鉴权提供者
        _providers = new Dictionary<AuthKind, IAuthProvider>
        {
            [AuthKind.ApiKey] = new ApiKeyAuthProvider(configService),
            [AuthKind.Cookie] = new CookieAuthProvider(
                browserLoginService ?? new BrowserLoginService(configService),
                configService),
            [AuthKind.OAuth] = new OAuthAuthProvider(configService)
        };
    }

    /// <summary>
    /// 获取插件支持的鉴权方式列表
    /// </summary>
    /// <param name="provider">插件实例</param>
    /// <returns>支持的鉴权方式列表</returns>
    public IReadOnlyList<AuthKind> GetSupportedAuthKinds(IUsageProvider provider)
    {
        // 从插件的 SupportedAuthKinds 属性读取（req-096 新增）
        // 如果插件未实现该属性，根据 LoginConfig 推断：
        // - LoginConfig != null → Cookie
        // - LoginConfig == null → ApiKey
        if (provider is IUsageProviderWithAuth providerWithAuth)
        {
            return providerWithAuth.SupportedAuthKinds;
        }

        // 向后兼容：根据 LoginConfig 推断
        return provider.LoginConfig != null
            ? new[] { AuthKind.Cookie }
            : new[] { AuthKind.ApiKey };
    }

    /// <summary>
    /// 获取鉴权数据（自动选择鉴权方式）
    /// </summary>
    /// <param name="provider">插件实例</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>鉴权数据字符串</returns>
    public async Task<string?> GetAuthDataAsync(
        IUsageProvider provider,
        string accountId = "default",
        CancellationToken ct = default)
    {
        var supportedKinds = GetSupportedAuthKinds(provider);
        if (supportedKinds.Count == 0)
            return null;

        // 优先使用第一种支持的鉴权方式
        var kind = supportedKinds[0];
        if (!_providers.TryGetValue(kind, out var authProvider))
            return null;

        return await authProvider.GetAuthDataAsync(provider.ProviderId, accountId, ct);
    }

    /// <summary>
    /// 验证鉴权数据是否有效
    /// </summary>
    /// <param name="provider">插件实例</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>验证是否通过</returns>
    public async Task<bool> ValidateAsync(
        IUsageProvider provider,
        string accountId = "default",
        CancellationToken ct = default)
    {
        var supportedKinds = GetSupportedAuthKinds(provider);
        if (supportedKinds.Count == 0)
            return false;

        var kind = supportedKinds[0];
        if (!_providers.TryGetValue(kind, out var authProvider))
            return false;

        return await authProvider.ValidateAsync(provider.ProviderId, accountId, ct);
    }

    /// <summary>
    /// 刷新鉴权数据
    /// </summary>
    /// <param name="provider">插件实例</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>刷新是否成功</returns>
    public async Task<bool> RefreshAsync(
        IUsageProvider provider,
        string accountId = "default",
        CancellationToken ct = default)
    {
        var supportedKinds = GetSupportedAuthKinds(provider);
        if (supportedKinds.Count == 0)
            return false;

        var kind = supportedKinds[0];
        if (!_providers.TryGetValue(kind, out var authProvider))
            return false;

        var success = await authProvider.RefreshAsync(provider.ProviderId, accountId, ct);
        
        // 刷新成功后更新登录态信息
        if (success)
        {
            await UpdateLoginStateAsync(provider.ProviderId, accountId, ct);
        }

        return success;
    }

    /// <summary>
    /// 保存鉴权数据
    /// </summary>
    /// <param name="provider">插件实例</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="authData">鉴权数据</param>
    /// <param name="ct">取消令牌</param>
    public async Task SaveAuthDataAsync(
        IUsageProvider provider,
        string accountId,
        string authData,
        CancellationToken ct = default)
    {
        var supportedKinds = GetSupportedAuthKinds(provider);
        if (supportedKinds.Count == 0)
            return;

        var kind = supportedKinds[0];
        if (!_providers.TryGetValue(kind, out var authProvider))
            return;

        await authProvider.SaveAuthDataAsync(provider.ProviderId, accountId, authData, ct);
        
        // 保存后更新登录态信息
        await UpdateLoginStateAsync(provider.ProviderId, accountId, ct);
    }

    /// <summary>
    /// 删除鉴权数据
    /// </summary>
    /// <param name="provider">插件实例</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    public async Task DeleteAuthDataAsync(
        IUsageProvider provider,
        string accountId = "default",
        CancellationToken ct = default)
    {
        var supportedKinds = GetSupportedAuthKinds(provider);
        if (supportedKinds.Count == 0)
            return;

        var kind = supportedKinds[0];
        if (!_providers.TryGetValue(kind, out var authProvider))
            return;

        await authProvider.DeleteAuthDataAsync(provider.ProviderId, accountId, ct);
        
        // 删除后清除登录态信息
        var key = GetLoginStateKey(provider.ProviderId, accountId);
        _loginStates.TryRemove(key, out _);
    }

    /// <summary>
    /// 获取登录态信息（用于登录态计时显示）
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="accountId">账号标识</param>
    /// <returns>登录态信息，未找到时返回 null</returns>
    public LoginStateInfo? GetLoginState(string providerId, string accountId = "default")
    {
        var key = GetLoginStateKey(providerId, accountId);
        return _loginStates.TryGetValue(key, out var state) ? state : null;
    }

    /// <summary>
    /// req-096 接线：幂等记录登录态获取时间（仅当尚未记录时写入 AcquiredAt=now）。
    /// <para>供 <c>RefreshService</c> 在首次成功刷新时调用，使“登录态计时”生效。
    /// 与 <see cref="UpdateLoginStateAsync"/> 不同：本方法**不**获取鉴权数据（不触发浏览器/IO），也**不**覆盖已有记录，
    /// 避免每次刷新重置 AcquiredAt；EncryptedData 留空（已标 [JsonIgnore]）。</para>
    /// </summary>
    /// <param name="providerId">服务商唯一标识。</param>
    /// <param name="accountId">账号标识（默认 "default"）。</param>
    public void EnsureLoginStateRecorded(string providerId, string accountId = "default")
    {
        if (string.IsNullOrEmpty(providerId)) return;
        var key = GetLoginStateKey(providerId, accountId);
        _loginStates.TryAdd(key, new LoginStateInfo
        {
            ProviderId = providerId,
            AccountId = accountId,
            AcquiredAt = DateTime.Now
        });
    }

    /// <summary>
    /// 更新登录态信息
    /// </summary>
    private async Task UpdateLoginStateAsync(string providerId, string accountId, CancellationToken ct)
    {
        var key = GetLoginStateKey(providerId, accountId);
        var authData = await GetAuthDataAsync(providerId, accountId, ct);
        
        if (authData != null)
        {
            _loginStates[key] = new LoginStateInfo
            {
                ProviderId = providerId,
                AccountId = accountId,
                AcquiredAt = DateTime.Now,
                EncryptedData = authData
            };
        }
    }

    /// <summary>
    /// 获取登录态信息的缓存键
    /// </summary>
    private static string GetLoginStateKey(string providerId, string accountId)
    {
        return $"{providerId}:{accountId}";
    }

    /// <summary>
    /// 从配置加载所有登录态信息（应用启动时调用）。
    /// <para>req-096：从 <see cref="ConfigService"/> 的 <c>PersistedLoginStates</c> 恢复登录态元数据
    /// （<see cref="LoginStateInfo.AcquiredAt"/> 等），使重启后“登录态计时”得以延续。
    /// 注意：<see cref="LoginStateInfo.EncryptedData"/> 标 <c>[JsonIgnore]</c> 不落盘（安全），
    /// 恢复后为空，待下次鉴权刷新时由 <see cref="UpdateLoginStateAsync"/> 重新填充。</para>
    /// </summary>
    public Task LoadLoginStatesAsync(CancellationToken ct = default)
    {
        var persisted = _configService.Settings.PersistedLoginStates;
        if (persisted != null)
        {
            foreach (var state in persisted)
            {
                if (state == null || string.IsNullOrEmpty(state.ProviderId)) continue;
                var accountId = string.IsNullOrEmpty(state.AccountId) ? "default" : state.AccountId;
                _loginStates[GetLoginStateKey(state.ProviderId, accountId)] = state;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存所有登录态信息（应用关闭时调用）。
    /// <para>req-096：把内存中的登录态元数据写回 <see cref="ConfigService"/> 并持久化到 config.json。
    /// <see cref="LoginStateInfo.EncryptedData"/> 因 <c>[JsonIgnore]</c> 不会写入，避免明文凭据落盘。</para>
    /// </summary>
    public Task SaveLoginStatesAsync(CancellationToken ct = default)
    {
        _configService.Settings.PersistedLoginStates = new List<LoginStateInfo>(_loginStates.Values);
        _configService.Save();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取鉴权数据（内部辅助方法）
    /// </summary>
    private async Task<string?> GetAuthDataAsync(string providerId, string accountId, CancellationToken ct)
    {
        // 遍历所有鉴权提供者，尝试获取鉴权数据
        foreach (var provider in _providers.Values)
        {
            var data = await provider.GetAuthDataAsync(providerId, accountId, ct);
            if (data != null)
                return data;
        }
        return null;
    }
}

/// <summary>
/// 扩展接口：插件声明支持的鉴权方式（req-096）
/// <para>插件可选择实现此接口以声明支持的鉴权方式，未实现时由 AuthManager 根据 LoginConfig 推断。</para>
/// </summary>
public interface IUsageProviderWithAuth
{
    /// <summary>插件支持的鉴权方式列表</summary>
    IReadOnlyList<AuthKind> SupportedAuthKinds { get; }
}
