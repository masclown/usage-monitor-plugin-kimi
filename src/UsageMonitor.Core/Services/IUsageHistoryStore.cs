using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-069 F-10：用量历史存储接口——为 DI 容器做准备。
/// 定义历史数据点的内存存储与持久化契约。
/// </summary>
public interface IUsageHistoryStore
{
    /// <summary>每个 Provider 最多保留的历史点数</summary>
    int MaxPoints { get; set; }

    /// <summary>历史数据变更事件（添加点或修改 MaxPoints 时触发）</summary>
    event EventHandler? HistoryChanged;

    /// <summary>指定 Provider 的历史数据变更事件（payload 为 providerId）</summary>
    event EventHandler<string>? ProviderHistoryChanged;

    /// <summary>添加一个用量历史点（线程安全）</summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="usagePercent">已用百分比（0-100）</param>
    void AddPoint(string providerId, double usagePercent);

    /// <summary>添加一个包含完整 extras 的用量历史点</summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="usage">本次刷新的完整用量（含 extras）</param>
    void AddPoint(string providerId, UsageInfo usage);

    /// <summary>记录一个"失败/数据缺失"历史点（IsError=true）</summary>
    /// <param name="providerId">服务商唯一标识</param>
    void AddErrorPoint(string providerId);

    /// <summary>从持久化仓库加载并填充指定 provider 的初始内存数据</summary>
    Task LoadFromRepositoryAsync(UsageHistoryRepository repository, IEnumerable<string> providerIds, int pointsPerProvider);

    /// <summary>获取指定 Provider 的历史数据（返回快照）</summary>
    IReadOnlyList<HistoryPoint> GetHistory(string providerId);

    /// <summary>获取指定 Provider 的历史百分比值列表（仅数值，便于图表绑定）</summary>
    IReadOnlyList<double> GetHistoryValues(string providerId);

    /// <summary>清空所有历史数据</summary>
    void Clear();
}
