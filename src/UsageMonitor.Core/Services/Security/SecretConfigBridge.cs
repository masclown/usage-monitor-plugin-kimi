using System;

namespace UsageMonitor.Core.Services.Security;

/// <summary>
/// 业务方使用 SecretStore 的可选桥接类。
/// <para>
/// 与 <see cref="ConfigService"/> 中基于 DPAPI + config.json 的加密方案并存，
/// 调用方按场景选择：
/// <list type="bullet">
/// <item><description><b>走 SecretStore</b>：Windows Credential Manager / AES-256-GCM 文件，与系统工具同源，运维友好</description></item>
/// <item><description><b>走 ConfigService</b>：DPAPI 加密后 base64 存 config.json，与现有插件配置深度集成</description></item>
/// </list>
/// </para>
/// <para>
/// 推荐用法：把 Cookie / Token / API Key 等"长寿命 + 跨进程"凭据走 SecretStore；
/// 把 Provider 普通配置（BaseUrl、刷新间隔等）走 ConfigService。
/// </para>
/// </summary>
public static class SecretConfigBridge
{
    /// <summary>默认 serviceName 前缀（便于在 Credential Manager 中按前缀筛选）</summary>
    public const string DefaultServicePrefix = "UsageMonitor.Provider.";

    /// <summary>
    /// 当前进程选定的凭据后端名称（用于 UI 提示）。
    /// <para>可能返回 <c>WindowsCredentialManager</c> 或 <c>AesGcmFile</c>。</para>
    /// </summary>
    public static string CurrentBackendName => SecretStoreFactory.Current.BackendName;

    /// <summary>
    /// 保存敏感凭据。
    /// </summary>
    /// <param name="providerId">Provider ID（如 "minimax"），会自动拼上 <see cref="DefaultServicePrefix"/> 作为 serviceName</param>
    /// <param name="accountName">凭据键名（如 "cookie"、"api_key"、"token"）</param>
    /// <param name="secretData">明文凭据</param>
    public static void SaveProviderSecret(string providerId, string accountName, string secretData)
    {
        ValidateArgs(providerId, accountName);
        if (secretData == null) throw new ArgumentNullException(nameof(secretData));
        SecretStoreFactory.Current.Set(ResolveServiceName(providerId), accountName, secretData);
    }

    /// <summary>
    /// 读取敏感凭据（null 表示不存在）。
    /// </summary>
    public static string? LoadProviderSecret(string providerId, string accountName)
    {
        ValidateArgs(providerId, accountName);
        return SecretStoreFactory.Current.Get(ResolveServiceName(providerId), accountName);
    }

    /// <summary>
    /// 删除敏感凭据。
    /// </summary>
    /// <returns>true 表示有凭据被删除，false 表示本来就不存在</returns>
    public static bool DeleteProviderSecret(string providerId, string accountName)
    {
        ValidateArgs(providerId, accountName);
        return SecretStoreFactory.Current.Delete(ResolveServiceName(providerId), accountName);
    }

    /// <summary>把 providerId 转换成 serviceName（自动拼接默认前缀）。</summary>
    private static string ResolveServiceName(string providerId) => DefaultServicePrefix + providerId;

    /// <summary>公共参数校验。</summary>
    private static void ValidateArgs(string providerId, string accountName)
    {
        if (string.IsNullOrEmpty(providerId))
            throw new ArgumentException("providerId 不能为空", nameof(providerId));
        if (string.IsNullOrEmpty(accountName))
            throw new ArgumentException("accountName 不能为空", nameof(accountName));
    }
}