using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Modules;

/// <summary>
/// req-099 B2：数据模块契约。把"用量数据的刷新保存 / 去重 / 历史读取 / 持久化加载 / 刷新聚合"
/// 集中在一个固定接口后，使 <c>RefreshService</c> 与 <c>MainViewModel</c> 通过接口访问数据层，
/// 而不再直接操作 <c>UsageHistoryStore</c> / <c>UsageHistoryRepository</c>。
/// <para>
/// 去重逻辑内聚在实现内部（<see cref="SaveUsage"/> → Store 的字段级指纹比对，见 req-015/092），
/// 插件只提供原始 <see cref="UsageInfo"/>，不感知存储细节。
/// </para>
/// </summary>
public interface IDataModule
{
    /// <summary>每个 Provider 在内存中保留的最大历史点数（同步到底层 Store）。</summary>
    int MaxPoints { get; set; }

    /// <summary>历史数据变更事件（添加点或修改 MaxPoints 时触发）。</summary>
    event EventHandler? HistoryChanged;

    /// <summary>指定 Provider 的历史数据变更事件（payload 为 providerId）。</summary>
    event EventHandler<string>? ProviderHistoryChanged;

    /// <summary>
    /// 保存一次成功刷新的完整用量数据（内部走字段级指纹去重 + 持久化）。
    /// <para>req-092 B3：若传入 <paramref name="standardFields"/>（插件 <c>MapToStandardFields</c> 的映射结果），
    /// 则据此做字段级差异检测并仅增量保存变化字段；为 null 时回退到 <c>UsageDataDiffService.ExtractStandardFields</c>。</para>
    /// </summary>
    /// <param name="usage">插件返回的完整用量信息。</param>
    /// <param name="standardFields">req-092：插件映射后的标准字段字典（可空，缺省自动提取）。</param>
    void SaveUsage(UsageInfo usage, IReadOnlyDictionary<string, object>? standardFields = null);

    /// <summary>记录一个"失败/数据缺失"历史点（避免折线断裂）。</summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    void AddErrorPoint(string providerId);

    /// <summary>获取指定 Provider 的历史百分比值列表（仅数值，便于图表绑定）。</summary>
    IReadOnlyList<double> GetHistoryValues(string providerId);

    /// <summary>获取指定 Provider 的历史数据点快照。</summary>
    IReadOnlyList<HistoryPoint> GetHistory(string providerId);

    /// <summary>启动时从持久化仓库回填最近 N 个点，避免重启后折线图从头画起。</summary>
    /// <param name="providerIds">需要回填的 Provider 集合。</param>
    /// <param name="pointsPerProvider">每个 Provider 回填的点数。</param>
    Task LoadPersistedHistoryAsync(IEnumerable<string> providerIds, int pointsPerProvider);

    /// <summary>
    /// 启动恢复（问题1/9）：读取指定 (Provider, Account) 在 <c>usage_field_versions</c> 表中的最新字段快照。
    /// <para>供 UI 在首次刷新前回填卡片显示（数据概览 / 进度条 / 5h 倒计时等）；
    /// 纯内存模式（无仓库）返回空字典。</para>
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="accountId">账号 ID（空时按 "default" 处理）。</param>
    Task<Dictionary<string, object>> GetLatestFieldsAsync(string providerId, string accountId);

    /// <summary>
    /// req-013：在一次成功刷新后，把当前内存快照区间的 最大/最小/末尾/平均 用量百分比
    /// 写入刷新聚合表（供历史窗口 DataGrid 展示）。fire-and-forget，失败仅日志。
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="triggerKind">触发类型（"manual" / "auto"）。</param>
    void RecordRefreshAggregate(string providerId, string triggerKind);

    /// <summary>
    /// req-110 P1-3：把历史表中按旧 account_id（网页身份哈希）落库的行迁移到新 account_id（配置账号 ID）。
    /// <para>账号首次绑定网页身份哈希时由 RefreshService 调用；幂等，失败仅日志；纯内存模式下无操作。</para>
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="fromAccountId">旧 account_id（网页身份哈希）。</param>
    /// <param name="toAccountId">新 account_id（配置账号 ID）。</param>
    Task MigrateAccountIdAsync(string providerId, string fromAccountId, string toAccountId);

    /// <summary>
    /// 暴露底层历史存储（供仅需只读订阅/查询的既有消费方过渡使用）。
    /// 新代码应优先使用本接口的方法而非直接访问底层存储。
    /// </summary>
    IUsageHistoryStore HistoryStore { get; }
}
