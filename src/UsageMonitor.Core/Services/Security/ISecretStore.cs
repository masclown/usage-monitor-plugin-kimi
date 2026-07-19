using System.Collections.Generic;

namespace UsageMonitor.Core.Services.Security;

/// <summary>
/// 敏感凭据（API Key / Token / Cookie / Password 等）安全存储抽象。
/// <para>
/// ℹ️ req-061 标注：本 Security 模块当前未启用。ConfigService 仍直接使用 DPAPI（ProtectedData.Protect）。
/// 待后续版本统一迁移到 ISecretStore 体系。
/// </para>
/// <para>
/// 业务调用方只需关心 <see cref="Set"/> / <see cref="Get"/> 两个接口，
/// 无需感知底层是 Windows Credential Manager 还是 AES-256-GCM 加密文件。
/// </para>
/// <para>
/// 设计目标：
/// <list type="bullet">
/// <item><description>优先对接操作系统级凭据保险箱（Windows Credential Manager / DPAPI）</description></item>
/// <item><description>Headless 环境（无桌面 / Docker / CI）自动降级到 AES-256-GCM 加密文件</description></item>
/// <item><description>降级方案 Master Key 必须从环境变量读取，禁止硬编码</description></item>
/// </list>
/// </para>
/// </summary>
public interface ISecretStore
{
    /// <summary>当前凭据后端名称（如 <c>WindowsCredentialManager</c> / <c>AesGcmFile</c>），便于日志诊断</summary>
    string BackendName { get; }

    /// <summary>
    /// 安全写入凭据。同名 <c>(serviceName, accountName)</c> 会被覆盖。
    /// </summary>
    /// <param name="serviceName">服务/应用名（用作分组前缀）</param>
    /// <param name="accountName">账号/键名</param>
    /// <param name="secretData">明文凭据内容（Cookie 字符串、API Key、Token 等）</param>
    void Set(string serviceName, string accountName, string secretData);

    /// <summary>
    /// 读取凭据。返回 <c>null</c> 表示凭据不存在。
    /// </summary>
    /// <param name="serviceName">服务/应用名</param>
    /// <param name="accountName">账号/键名</param>
    string? Get(string serviceName, string accountName);

    /// <summary>
    /// 删除凭据。不存在不抛异常。
    /// </summary>
    /// <returns>true 表示有凭据被删除，false 表示本来就不存在</returns>
    bool Delete(string serviceName, string accountName);

    /// <summary>
    /// 枚举指定服务名下所有账号名。
    /// </summary>
    IReadOnlyList<string> ListAccounts(string serviceName);
}