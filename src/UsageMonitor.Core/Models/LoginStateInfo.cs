using System.Text.Json.Serialization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 登录态信息模型 - 记录登录态获取时间、加密数据和账号标识
/// <para>req-096：统一鉴权管理模块的登录态计时与多账号支持。</para>
/// </summary>
public class LoginStateInfo
{
    /// <summary>登录态获取时间</summary>
    public DateTime AcquiredAt { get; set; } = DateTime.Now;

    /// <summary>登录态数据（加密的 Cookie 或 Token）。
    /// req-096 安全：标 [JsonIgnore] 绝不持久化到 config.json，避免明文凭据落盘；
    /// 实际鉴权数据由各 AuthProvider 单独安全持久化，此字段仅在内存态有效。</summary>
    [JsonIgnore]
    public string EncryptedData { get; set; } = string.Empty;

    /// <summary>账号标识（用于多账号支持，默认 "default"）</summary>
    public string AccountId { get; set; } = "default";

    /// <summary>服务商唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>登录态持续时间（从获取时间到现在）</summary>
    [JsonIgnore]
    public TimeSpan Duration => DateTime.Now - AcquiredAt;

    /// <summary>登录态持续天数</summary>
    [JsonIgnore]
    public int DurationDays => (int)Duration.TotalDays;
}
