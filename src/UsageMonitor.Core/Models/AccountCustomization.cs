using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 账号级用户定制（req-107 B7）：用户对单个账号（<c>(ProviderId, AccountId)</c> 二维 key）的显示覆盖。
/// <para>主程序最终视图 = 插件 defaults.json 默认声明 + 本账号级覆盖。同一 Provider 下不同账号互不影响
/// （账号 A 显示折线+热力、账号 B 只显示进度条）。供 req-109 多账号 UI 消费。</para>
/// </summary>
public sealed class AccountCustomization
{
    /// <summary>可见图表 ID 列表（覆盖 defaults.json 默认可见性；null 表示沿用默认）。</summary>
    public List<string>? VisibleCharts { get; set; }

    /// <summary>图表排序（chartId → 序号，越小越靠前）。</summary>
    public Dictionary<string, int> ChartOrders { get; set; } = new();

    /// <summary>各图表当前选中的数据组 ID（chartId → dataGroup id，对应 DataGroup 切片器状态）。</summary>
    public Dictionary<string, string> CurrentDataGroupIds { get; set; } = new();

    /// <summary>各图表色阶来源（chartId → source 键，如 "global:usage-tier-default" 或用户自定义）。</summary>
    public Dictionary<string, string> ChartColorTierSources { get; set; } = new();

    /// <summary>账号昵称（用户自定义，同一 Provider 内唯一）。</summary>
    public string? Nickname { get; set; }

    /// <summary>是否用昵称替代账号显示名。</summary>
    public bool UseNickname { get; set; }

    /// <summary>各图表标题覆盖（chartId → 标题；图表标题不在声明里，由用户设置界面定义）。</summary>
    public Dictionary<string, string> ChartTitles { get; set; } = new();

    /// <summary>各图表的数据组可见性（chartId → 数据组 ID 列表；null = 沿用 defaults.json 全部可见）。
    /// <para>用户设置：增删数据组；列表顺序即卡片内显示顺序。</para>
    /// </summary>
    public Dictionary<string, List<string>?> VisibleDataGroups { get; set; } = new();

    /// <summary>各图表的数据组排序（chartId → dataGroupId → 序号；序号越小越靠前）。
    /// <para>与 <see cref="VisibleDataGroups"/> 互斥：未设 <see cref="VisibleDataGroups"/> 时，按 <see cref="DataGroupOrders"/> 排序 + defaults.json 全部可见；均未设则沿用 defaults.json 声明顺序。</para>
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> DataGroupOrders { get; set; } = new();

    /// <summary>可见的进度条字段名列表（req-107 #10：合并旧 AppSettings.SelectedProgressFields[providerId]）。
    /// <para>null = 沿用插件 defaults.json 默认；空集合 = 不显示任何进度条字段。</para>
    /// </summary>
    public List<string>? VisibleProgressFields { get; set; }

    /// <summary>可见的数字网格字段名列表（req-107 #10：合并旧 AppSettings.SelectedMetricFields[providerId]）。
    /// <para>null = 沿用插件 defaults.json 默认；空集合 = 不显示任何数字字段。</para>
    /// </summary>
    public List<string>? VisibleMetricFields { get; set; }

    /// <summary>req-109：一账号多卡片（本账号下多张卡片各自的配置）。
    /// <para>Key 维度为 <c>(ProviderId, AccountId, CardId)</c>，其中 CardId 仅在显示/配置层使用；
    /// 第一个卡片 <c>CardId = "default-card"</c>。当 <see cref="Cards"/> 为空时，
    /// 顶层扁平字段（VisibleCharts 等）继续生效——迁移期兼容。</para>
    /// </summary>
    public List<CardConfig> Cards { get; set; } = new();

    /// <summary>req-105：每张图表的 Tooltip 显示字段（chartId → SDK 字段名列表）。
    /// <para>null = 沿用 defaults.json 声明的 <c>tooltip.fields</c>；空集合 = 不显示 tooltip；非空 = 仅显示列表内字段。
    /// SDK 字段元数据决定实际可选项（白名单校验由 <c>PluginValidator</c> 完成）。</para>
    /// </summary>
    public Dictionary<string, List<string>?> VisibleTooltipFields { get; set; } = new();

    /// <summary>req-109：可见的 Mini 图表 ID 列表（null = 沿用 defaults.json 全部；空集合 = 不显示任何 Mini 图表）。
    /// <para>任务栏的迷你图（半圆环 / 文字）按 Provider 的 <c>taskbar.miniCharts</c> 声明 → 用户个性化裁剪。</para>
    /// </summary>
    public List<string>? VisibleMiniCharts { get; set; }

    /// <summary>req-109：各 Mini 图表的数据组可见性（miniChartId → dataGroupId 列表；null = 沿用默认全部可见）。</summary>
    public Dictionary<string, List<string>?> VisibleMiniDataGroups { get; set; } = new();

    /// <summary>req-109：各 Mini 图表的数据组排序（miniChartId → dataGroupId → 序号；序号越小越靠前）。</summary>
    public Dictionary<string, Dictionary<string, int>> MiniDataGroupOrders { get; set; } = new();

    /// <summary>
    /// 生成账号定制的复合键：<c>ProviderId:AccountId:CardId</c>（3 段）。
    /// </summary>
    /// <param name="providerId">插件 ID。</param>
    /// <param name="accountId">账号 ID（缺省 "default"）。</param>
    /// <param name="cardId">卡片 ID（缺省 "default-card"）。</param>
    public static string MakeKey(string providerId, string accountId = "default", string cardId = "default-card")
        => $"{providerId}:{(string.IsNullOrEmpty(accountId) ? "default" : accountId)}:{(string.IsNullOrEmpty(cardId) ? "default-card" : cardId)}";

    /// <summary>
    /// 深拷贝当前实例（所有集合字段逐一新建独立副本），供 <c>ConfigService.MakeSnapshot</c> 使用，
    /// 避免快照与 <c>_settings</c> 共享引用导致并发修改或数据丢失。
    /// </summary>
    public AccountCustomization Clone()
    {
        var clone = new AccountCustomization
        {
            VisibleCharts = VisibleCharts == null ? null : new List<string>(VisibleCharts),
            ChartOrders = new Dictionary<string, int>(ChartOrders),
            CurrentDataGroupIds = new Dictionary<string, string>(CurrentDataGroupIds),
            ChartColorTierSources = new Dictionary<string, string>(ChartColorTierSources),
            Nickname = Nickname,
            UseNickname = UseNickname,
            ChartTitles = new Dictionary<string, string>(ChartTitles),
            VisibleProgressFields = VisibleProgressFields == null ? null : new List<string>(VisibleProgressFields),
            VisibleMetricFields = VisibleMetricFields == null ? null : new List<string>(VisibleMetricFields),
            VisibleMiniCharts = VisibleMiniCharts == null ? null : new List<string>(VisibleMiniCharts),
        };

        // VisibleDataGroups：Dictionary<string, List<string>?>
        foreach (var kvp in VisibleDataGroups)
            clone.VisibleDataGroups[kvp.Key] = kvp.Value == null ? null : new List<string>(kvp.Value);

        // DataGroupOrders：Dictionary<string, Dictionary<string, int>>
        foreach (var kvp in DataGroupOrders)
            clone.DataGroupOrders[kvp.Key] = new Dictionary<string, int>(kvp.Value);

        // VisibleTooltipFields：Dictionary<string, List<string>?>
        foreach (var kvp in VisibleTooltipFields)
            clone.VisibleTooltipFields[kvp.Key] = kvp.Value == null ? null : new List<string>(kvp.Value);

        // VisibleMiniDataGroups：Dictionary<string, List<string>?>
        foreach (var kvp in VisibleMiniDataGroups)
            clone.VisibleMiniDataGroups[kvp.Key] = kvp.Value == null ? null : new List<string>(kvp.Value);

        // MiniDataGroupOrders：Dictionary<string, Dictionary<string, int>>
        foreach (var kvp in MiniDataGroupOrders)
            clone.MiniDataGroupOrders[kvp.Key] = new Dictionary<string, int>(kvp.Value);

        // Cards：List<CardConfig>（逐项深拷贝）
        foreach (var card in Cards)
        {
            clone.Cards.Add(new CardConfig
            {
                CardId = card.CardId,
                Title = card.Title,
                DisplayOrder = card.DisplayOrder,
                Customization = card.Customization.Clone()
            });
        }

        return clone;
    }
}
