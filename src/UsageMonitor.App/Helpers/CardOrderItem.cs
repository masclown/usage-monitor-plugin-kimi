namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-103：卡片排序设置页的列表项。
/// </summary>
public class CardOrderItem
{
    /// <summary>Provider ID（唯一标识）。</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>显示名称（如「MiniMax」「OpenAI」）。</summary>
    public string DisplayName { get; set; } = string.Empty;
}
