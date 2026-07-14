namespace UsageMonitor.Core.Models;

/// <summary>
/// 任务栏窗口中各 Provider 的显示模式
/// - Text: 仅显示文字（DisplayName + 剩余额度）
/// - MiniLineChart: 上方文字 + 下方迷你折线图（展示已用百分比历史趋势）
/// - RingChart: 圆环进度图 + 名称（中心显示纯数字百分比）
/// </summary>
public enum TaskbarDisplayMode
{
    /// <summary>文字模式（默认）</summary>
    Text = 0,

    /// <summary>迷你折线图模式</summary>
    MiniLineChart = 1,

    /// <summary>圆环进度图模式</summary>
    RingChart = 2
}
