namespace UsageMonitor.Core.Models;

/// <summary>
/// req-093：环形图（半圆环 / 全圆环）单个 metric 的完整数据包。
/// <para>
/// 解决环形图色阶与具体数据语义不一致的问题：5h 用量（已用百分比）应当
/// "越高越红"，而周限额剩余、积分余额（剩余量）应当 "越低越红"。
/// 把 "是否反转 / 警告阈值 / 危险阈值" 等元信息从全局配置下沉到数据本身，
/// 让 <c>RingChartControl.SelectBrush</c> 直接根据数据语义选择画笔，
/// 不依赖外部判断。
/// </para>
/// <para>
/// 设计参考：req-009 热力图色阶按 Provider 独立配置、req-074 控件默认值主题化。
/// 本类作为 SDK 契约的一部分，Core 项目纯 DTO，无 WPF 依赖，插件 SDK 可自由构造。
/// </para>
/// </summary>
public class RingMetricData
{
    /// <summary>数据名称（如 "5h 用量"、"周限额剩余"、"积分余额"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>当前百分比（0-100）。由插件 / ProviderUsageViewModel 提供。</summary>
    public double Percent { get; set; }

    /// <summary>
    /// 色阶方向。false（默认）= 高百分比危险（已用量语义）；
    /// true = 低百分比危险（剩余量语义）。
    /// </summary>
    public bool IsInverted { get; set; }

    /// <summary>警告阈值（百分比）。默认 60，对应 "正常模式" 的高%语义。</summary>
    public double WarningThreshold { get; set; } = 60;

    /// <summary>危险阈值（百分比）。默认 85，对应 "正常模式" 的高%语义。</summary>
    public double DangerThreshold { get; set; } = 85;

    /// <summary>显示文本（可选，如 "剩余 80%"）。控件 CenterText 缺省时使用。</summary>
    public string? DisplayText { get; set; }
}