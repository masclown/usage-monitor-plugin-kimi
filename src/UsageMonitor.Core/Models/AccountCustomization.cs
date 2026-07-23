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

    /// <summary>可见的进度条字段名列表（req-107 #10：合并旧 AppSettings.SelectedProgressFields[providerId]）。
    /// <para>null = 沿用插件 defaults.json 默认；空集合 = 不显示任何进度条字段。</para>
    /// </summary>
    public List<string>? VisibleProgressFields { get; set; }

    /// <summary>可见的数字网格字段名列表（req-107 #10：合并旧 AppSettings.SelectedMetricFields[providerId]）。
    /// <para>null = 沿用插件 defaults.json 默认；空集合 = 不显示任何数字字段。</para>
    /// </summary>
    public List<string>? VisibleMetricFields { get; set; }

    /// <summary>
    /// 生成账号定制的复合键：<c>ProviderId:AccountId</c>（AccountId 缺省为 "default"）。
    /// </summary>
    /// <param name="providerId">插件 ID。</param>
    /// <param name="accountId">账号 ID（缺省 "default"）。</param>
    public static string MakeKey(string providerId, string accountId = "default")
        => $"{providerId}:{(string.IsNullOrEmpty(accountId) ? "default" : accountId)}";
}
