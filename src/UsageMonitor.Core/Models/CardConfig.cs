namespace UsageMonitor.Core.Models;

/// <summary>
/// req-109：卡片实体（一账号多卡片基础，每张卡片拥有独立 <see cref="AccountCustomization"/>）。
/// <para>存储：<see cref="AccountCustomization.Cards"/> 列表；同 (Provider, Account) 内 CardId 唯一。
/// 第一个卡片 <c>CardId = "default-card"</c>（迁移老配置时使用）。</para>
/// <para>CardId 仅在显示/配置层；认证层（AuthManager）仍按 (Provider, Account) 二段 key 鉴权。</para>
/// </summary>
public sealed class CardConfig
{
    /// <summary>卡片 ID；(Provider, Account) 内唯一；缺省 / 空字符串规范化为 "default-card"。</summary>
    public string CardId { get; set; } = "default-card";

    /// <summary>卡片标题；为空时使用 Provider 默认显示名。</summary>
    public string? Title { get; set; }

    /// <summary>同账号内拖拽排序（0-based）。</summary>
    public int DisplayOrder { get; set; }

    /// <summary>该卡片的图表可见性、排序、数据组、tooltip、色阶等个性化配置。</summary>
    public AccountCustomization Customization { get; set; } = new();

    /// <summary>
    /// 生成卡片复合键：<c>ProviderId:AccountId:CardId</c>（与 <see cref="AccountCustomization.MakeKey"/> 三段一致）。
    /// </summary>
    public static string MakeKey(string providerId, string accountId = "default", string cardId = "default-card")
        => $"{providerId}:{(string.IsNullOrEmpty(accountId) ? "default" : accountId)}:{(string.IsNullOrEmpty(cardId) ? "default-card" : cardId)}";
}