using System;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-028：把 <see cref="TimeSpan"/> 格式化成 <c>HH:mm:ss</c> 形式的剩余时间字符串。
/// <para>与项目里现有的"X 小时 Y 分钟后重置"（详见 <c>MainViewModel.BuildRemainText</c>）互补——
/// BuildRemainText 适合人读，本类适合机器/UI 扫码读（倒计时实时滚动）。
/// 剩余 ≤ 0 一律返回 <c>"00:00:00"</c>，避免负号污染 UI。</para>
/// <para>
/// 实例：本类只有静态方法，禁实例化。
/// </para>
/// </summary>
public static class CountdownFormatter
{
    /// <summary>把剩余时长格式化为 HH:mm:ss（不足补 0）。剩余 ≤ 0 返回 00:00:00。</summary>
    public static string Format(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "00:00:00";
        var hours = (int)Math.Floor(remaining.TotalHours);
        var minutes = remaining.Minutes;
        var seconds = remaining.Seconds;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    /// <summary>把"目标时刻 - 当前时刻"格式化为剩余时间字符串。目标 ≤ 当前返回 00:00:00。</summary>
    public static string FormatUntil(DateTime targetTime)
        => Format(targetTime - DateTime.Now);
}
