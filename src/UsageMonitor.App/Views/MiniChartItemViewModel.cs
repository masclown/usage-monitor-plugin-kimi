using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using UsageMonitor.Core.Plugins.MiniChart;

namespace UsageMonitor.App.Views;

/// <summary>
/// req-088 B5：Taskbar 迷你图列表项的轻量包装 ViewModel。
/// <para>
/// 把 Core 层 <see cref="MiniChartDescriptor"/>（只读 DTO）与 App 层 UI 状态绑定起来：
/// <list type="bullet">
///   <item><description>descriptor：注册中心传入的描述符（Kind/Style/ColorTier/DataSource/Tooltip）。</description></item>
///   <item><description>UsageVm：关联的 <see cref="ViewModels.ProviderUsageViewModel"/>（用于订阅数据变化、刷新通知）。</description></item>
///   <item><description>DataSource 派生属性：把 <c>object?</c> 解包为 <c>double?</c>（百分比）便于 XAML 绑定。</description></item>
///   <item><description>TooltipText/TooltipTitle：把 <see cref="MiniChartTooltip"/> 模板渲染成具体字符串（req-088 B9）。</description></item>
/// </list>
/// </para>
/// <para>所有派生属性（UsagePercentage/TooltipText 等）通过 INPC 通知 XAML 刷新。
/// 注意：本类不做线程同步——仅在 UI 线程调用 <see cref="RefreshFromUsageVm"/>。</para>
/// </summary>
public class MiniChartItemViewModel : INotifyPropertyChanged
{
    /// <summary>底层描述符（注册中心传入，运行时只读）。</summary>
    public MiniChartDescriptor Descriptor { get; }

    /// <summary>关联的 ProviderUsageViewModel（用于同步实时数据；null 时显示空状态）。</summary>
    public ViewModels.ProviderUsageViewModel? UsageVm { get; }

    /// <summary>唯一 ProviderId（来自 Descriptor，转发给 XAML 便于调试）。</summary>
    public string ProviderId => Descriptor.ProviderId;

    /// <summary>图类型枚举（用于 DataTemplateSelector 选择模板）。</summary>
    public MiniChartKind Kind => Descriptor.Kind;

    /// <summary>视觉样式枚举。</summary>
    public MiniChartStyle Style => Descriptor.Style;

    /// <summary>
    /// 派生：当前用量百分比（0-100）。优先取 UsageVm 的实时值；缺失时回退到 descriptor.DataSource 中的 double?。
    /// <para>MiniRingChart / MiniText 必须；其它类型（MiniLineChart / MiniBarChart / MiniHeatMap）忽略此值。</para>
    /// </summary>
    public double? UsagePercent
    {
        get
        {
            if (UsageVm != null) return UsageVm.UsagePercentage;
            return Descriptor.DataSource as double?;
        }
    }

    /// <summary>
    /// 派生：环图模式是否处于错误态（用于 XAML 显隐「错误」覆盖层）。
    /// </summary>
    public bool IsError => UsageVm?.IsError ?? false;

    /// <summary>
    /// req-088 B9：渲染后的 Tooltip 标题文本（来自 MiniChartTooltip.TitleTemplate）。
    /// <para>支持占位符：<c>{ProviderName}</c> / <c>{Percent}</c> / <c>{Value}</c> / <c>{Timestamp}</c>。</para>
    /// </summary>
    public string TooltipTitle
    {
        get
        {
            var template = Descriptor.Tooltip?.TitleTemplate;
            if (string.IsNullOrEmpty(template)) return UsageVm?.DisplayName ?? ProviderId;
            return ResolveTooltipTemplate(template);
        }
    }

    /// <summary>
    /// req-088 B9：渲染后的 Tooltip 正文文本（来自 MiniChartTooltip.BodyTemplate）。
    /// <para>空字符串表示不显示 Body 行。</para>
    /// </summary>
    public string TooltipBody
    {
        get
        {
            var template = Descriptor.Tooltip?.BodyTemplate;
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return ResolveTooltipTemplate(template);
        }
    }

    /// <summary>
    /// req-088 B9：当前 descriptor 是否启用 Tooltip（ShowDelayMs ≥ 0 时显示）。
    /// </summary>
    public bool HasTooltip => (Descriptor.Tooltip?.ShowDelayMs ?? 0) >= 0 && !string.IsNullOrEmpty(TooltipTitle);

    /// <summary>req-088 B9：Tooltip 显示延迟（毫秒），负数表示禁用。</summary>
    public int TooltipShowDelayMs => Descriptor.Tooltip?.ShowDelayMs ?? 0;

    // req-105：本地 DispatcherTimer 推动 RefreshCountdownText 每秒刷新。
    // 默认不启动，宿主（TaskbarWindow）可调 <see cref="StartCountdownTimer"/> 启动。
    // 设计上与 MainViewModel 全局 timer 并存：全局 timer 每秒更新 <c>FiveHourCountdownText</c>
    // （依赖 Provider 数据推送），本地 timer 负责本实例的 INPC 触发（与数据推送解耦）。
    private DispatcherTimer? _countdownTimer;

    /// <summary>
    /// req-105：动态刷新倒计时文案（如 "重置倒计时：2 小时 21 分钟"）。
    /// 优先从 <see cref="UsageVm"/> 提取已有的 <c>FiveHourCountdownText</c>（req-028 全局 timer 每秒刷新）作为 RefreshCountdown 占位符的取值；
    /// 无 UsageVm / 无 5h 字段时回退为 "上次更新：{LastUpdateText}" 兑底；两者都为空时返回空串。
    /// </summary>
    public string RefreshCountdownText
    {
        get
        {
            if (UsageVm == null) return string.Empty;
            var countdown = UsageVm.FiveHourCountdownText;
            if (!string.IsNullOrWhiteSpace(countdown) && countdown != "00:00:00")
                return $"重置倒计时：{countdown}";
            if (!string.IsNullOrWhiteSpace(UsageVm.LastUpdateText))
                return $"上次更新：{UsageVm.LastUpdateText}";
            return string.Empty;
        }
    }

    /// <summary>
    /// req-105：启动本 VM 的倒计时 DispatcherTimer（每秒 Tick 触发 RefreshCountdownText 通知）。
    /// <para>幂等：多次调用不会重复启动 timer。宿主在 <c>TaskbarWindow.Loaded</c> 时调用。</para>
    /// </summary>
    public void StartCountdownTimer()
    {
        if (_countdownTimer != null) return;
        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) => OnPropertyChanged(nameof(RefreshCountdownText));
        _countdownTimer.Start();
    }

    /// <summary>
    /// req-105：停止本 VM 的倒计时 DispatcherTimer。宿主在 <c>TaskbarWindow.Unloaded</c> 时调用避免内存泄漏。
    /// </summary>
    public void StopCountdownTimer()
    {
        if (_countdownTimer == null) return;
        _countdownTimer.Stop();
        _countdownTimer = null;
    }

    public MiniChartItemViewModel(MiniChartDescriptor descriptor, ViewModels.ProviderUsageViewModel? usageVm)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        UsageVm = usageVm;
    }

    /// <summary>
    /// 由外部（ProviderUsageViewModel 数据更新时）调用以触发本 VM 刷新。
    /// <para>所有派生属性会发出 PropertyChanged 让 XAML 重新取样。</para>
    /// </summary>
    public void RefreshFromUsageVm()
    {
        OnPropertyChanged(nameof(UsagePercent));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(TooltipTitle));
        OnPropertyChanged(nameof(TooltipBody));
        OnPropertyChanged(nameof(HasTooltip));
        // req-105：刷新动态倒计时。FiveHourCountdownText 由 MainViewModel 全局 timer 每秒刷新。
        OnPropertyChanged(nameof(RefreshCountdownText));
    }

    /// <summary>
    /// 替换 MiniChartTooltip 占位符为实际值。
    /// <para>占位符集合：<c>{ProviderName}</c> → DisplayName / <c>{Percent}</c> → UsagePercent /
    /// <c>{Value}</c> → 原始 double / <c>{Timestamp}</c> → HH:mm:ss /
    /// <c>{RefreshCountdown}</c> → <see cref="RefreshCountdownText"/>（req-105 新增）。</para>
    /// </summary>
    private string ResolveTooltipTemplate(string template)
    {
        // req-105：先按 descriptor.ToolTipFields 剔除未启用字段的占位符（未来扩展点落地）。
        var result = StripDisabledTooltipFields(template);
        var displayName = UsageVm?.DisplayName ?? ProviderId;
        result = result.Replace("{ProviderName}", displayName);
        if (UsagePercent.HasValue)
            result = result.Replace("{Percent}", UsagePercent.Value.ToString("0.0"));
        else
            result = result.Replace("{Percent}", "--");
        if (UsageVm?.HistoryValues is { Count: > 0 } last && double.IsFinite(last[^1]))
            result = result.Replace("{Value}", last[^1].ToString("0.0"));
        else
            result = result.Replace("{Value}", "--");
        // req-105：用动态倒计时文案替换占位符；无数据时移除占位符。
        result = result.Replace("{RefreshCountdown}", RefreshCountdownText);
        result = result.Replace("{Timestamp}", DateTime.Now.ToString("HH:mm:ss"));
        return result;
    }

    /// <summary>
    /// req-105：按 <see cref="MiniChartDescriptor.ToolTipFields"/> 剔除未启用字段的占位符。
    /// <para>字段名 → 占位符映射：ProviderName→{ProviderName}；CurrentValue→{Value}/{Percent}；
    /// RefreshCountdown→{RefreshCountdown}。{Timestamp} 为元数据始终保留。未列出的字段占位符被替换为空串。</para>
    /// </summary>
    private string StripDisabledTooltipFields(string template)
    {
        var fields = Descriptor.ToolTipFields;
        if (fields == null || fields.Count == 0) return template;
        var set = new System.Collections.Generic.HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
        var result = template;
        if (!set.Contains("ProviderName")) result = result.Replace("{ProviderName}", "");
        if (!set.Contains("CurrentValue"))
        {
            result = result.Replace("{Value}", "");
            result = result.Replace("{Percent}", "");
        }
        if (!set.Contains("RefreshCountdown")) result = result.Replace("{RefreshCountdown}", "");
        return result;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
