using System;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-109：账号实体（多账号 + 一账号多卡片基础）。
/// <para>每个 (ProviderId, AccountId) 在 <see cref="AppSettings.Accounts"/> 列表内唯一；
/// 账号管理 UI 按用户决定放 <c>PluginConfigWindow</c> 内（用户拍板），不在设置窗口顶级导航。</para>
/// <para>认证层（AuthManager / LoginStateInfo）仍按 (Provider, Account) 二段 key 鉴权，CardId 不进认证层。</para>
/// </summary>
public sealed class Account
{
    /// <summary>所属 Provider 唯一标识。</summary>
    public string ProviderId { get; set; } = "";

    /// <summary>账号 ID；Provider 内唯一；缺省 / 空字符串规范化为 "default"。</summary>
    public string AccountId { get; set; } = "default";

    /// <summary>用户自定义昵称；Provider 内唯一；为空时回退显示 Provider 名。</summary>
    public string? Nickname { get; set; }

    /// <summary>是否用昵称替代账号显示名。</summary>
    public bool UseNickname { get; set; }

    /// <summary>账号创建时间（首次 AddAccount 时记录）。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>是否默认账号（Provider 下首个 AddAccount 自动为 true）。</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 账号是否启用（S1 插件管理页账号列表的启用开关）。
    /// <para>默认 <c>true</c>；旧配置反序列化缺失该字段时保持启用，保证向后兼容。
    /// 由 <c>ConfigService.UpdateAccount</c> 持久化，用于后续按账号过滤卡片显示。</para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// req-110 P1-3：网页身份绑定哈希（AccountIdHasher 产出，非 PII）。
    /// <para>账号由用户显式创建；刷新成功后插件返回的网页身份哈希写入本字段作为绑定元数据
    /// （首次绑定；后续不一致仅告警，提示网页侧换号），不再反向自动注册账号。
    /// 删除账号后重建同一网页账号可凭相同哈希重新关联历史数据。null = 尚未绑定。</para>
    /// </summary>
    public string? BoundStableId { get; set; }

    /// <summary>
    /// 生成账号复合键：<c>ProviderId:AccountId</c>（与 <see cref="AccountCustomization.MakeKey"/> 二段前缀一致）。
    /// </summary>
    public static string MakeKey(string providerId, string accountId = "default")
        => $"{providerId}:{(string.IsNullOrEmpty(accountId) ? "default" : accountId)}";
}