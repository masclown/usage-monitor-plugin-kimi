namespace UsageMonitor.Core.Models;

/// <summary>
/// 刷新策略模型 - 定义插件的刷新间隔策略
/// <para>req-102：插件刷新策略声明，RefreshService 按插件策略执行刷新。</para>
/// </summary>
public class RefreshPolicy
{
    /// <summary>最小刷新间隔（秒），防止过于频繁的刷新</summary>
    public int MinIntervalSeconds { get; set; } = 60;

    /// <summary>最大刷新间隔（秒），确保数据不会过期太久</summary>
    public int MaxIntervalSeconds { get; set; } = 3600;

    /// <summary>默认刷新间隔（秒），插件推荐的刷新频率</summary>
    public int DefaultIntervalSeconds { get; set; } = 900;

    /// <summary>
    /// 验证刷新间隔是否在策略范围内
    /// </summary>
    /// <param name="intervalSeconds">待验证的刷新间隔（秒）</param>
    /// <returns>验证后的刷新间隔（超出范围时返回边界值）</returns>
    public int ClampInterval(int intervalSeconds)
    {
        if (intervalSeconds < MinIntervalSeconds)
            return MinIntervalSeconds;
        if (intervalSeconds > MaxIntervalSeconds)
            return MaxIntervalSeconds;
        return intervalSeconds;
    }
}
