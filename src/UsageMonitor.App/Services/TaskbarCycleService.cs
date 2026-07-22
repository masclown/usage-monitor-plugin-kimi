using System.Windows.Threading;
using UsageMonitor.Core.Plugins.MiniChart;

namespace UsageMonitor.App.Services;

/// <summary>
/// req-088 B8：Taskbar 多 Provider 迷你图循环切换服务。
/// <para>
/// 监听 <see cref="ITaskbarMiniChartRegistry"/>：当存在至少一个
/// <see cref="MiniChartDescriptor.CycleConfig"/>.<c>Enabled == true</c> 的描述符时，
/// 启动 DispatcherTimer 按 <c>IntervalSeconds</c> 滚动切换
/// <see cref="CurrentVisibleProviderId"/>，让 Taskbar 渲染层只显示当前 Provider 的迷你图。
/// </para>
/// <para>
/// 当前默认所有内置 Provider 的 descriptor 都不启用 CycleConfig（保持并排显示）；
/// 未来插件可通过 <c>descriptor.CycleConfig = MiniChartCycleConfig.Default5s</c> 启用滚动。
/// </para>
/// <para>
/// 设计参考：req-003 sticky timer 模式 —— 单一 DispatcherTimer 复用，避免每 Provider 一个 timer。
/// </para>
/// </summary>
public sealed class TaskbarCycleService
{
    private readonly ITaskbarMiniChartRegistry _registry;
    private readonly DispatcherTimer _timer;
    private int _currentIndex;
    private string? _currentVisibleProviderId;

    /// <summary>
    /// 当前可见的 ProviderId（cycle 切换的目标）。
    /// 渲染层可绑定此属性，过滤 <see cref="ITaskbarMiniChartRegistry.GetAll"/> 的可见子集。
    /// </summary>
    public string? CurrentVisibleProviderId
    {
        get => _currentVisibleProviderId;
        private set
        {
            if (_currentVisibleProviderId == value) return;
            _currentVisibleProviderId = value;
            VisibleProviderChanged?.Invoke(this, value);
        }
    }

    /// <summary>当前可见 Provider 变化事件，供渲染层订阅。</summary>
    public event EventHandler<string?>? VisibleProviderChanged;

    /// <summary>
    /// 创建循环服务实例。DispatcherTimer 在调用 <see cref="Start"/> 时启动。
    /// </summary>
    /// <param name="registry">迷你图注册中心（已注册的所有 descriptor）</param>
    public TaskbarCycleService(ITaskbarMiniChartRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 启动滚动：从 descriptor 集合中找出第一个 <c>CycleConfig.Enabled == true</c> 的配置，
    /// 按其 IntervalSeconds 设置 timer 间隔；若无任何 descriptor 启用，则 timer 不启动。
    /// </summary>
    public void Start()
    {
        var descriptors = _registry.GetAll().ToList();
        var firstCycleDescriptor = descriptors.FirstOrDefault(d => d.CycleConfig?.Enabled == true);
        if (firstCycleDescriptor == null)
        {
            // 没有启用循环的 descriptor → 不启动 timer；CurrentVisibleProviderId 保持 null，
            // 渲染层应回退到「全部并排显示」语义。
            return;
        }

        var intervalSeconds = Math.Max(1, firstCycleDescriptor.CycleConfig!.IntervalSeconds);
        _timer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _currentIndex = 0;
        ApplyCurrentIndex();
        _timer.Start();
        UsageMonitor.Core.Services.FileLogger.Info("TaskbarCycleService",
            $"Started cycling: interval={intervalSeconds}s, total={descriptors.Count}");
    }

    /// <summary>停止滚动 timer。</summary>
    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Timer 回调：滚动到下一个 descriptor；到达末尾时回到 0。
    /// </summary>
    private void OnTick(object? sender, EventArgs e)
    {
        var descriptors = _registry.GetAll().ToList();
        if (descriptors.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % descriptors.Count;
        ApplyCurrentIndex();
    }

    private void ApplyCurrentIndex()
    {
        var descriptors = _registry.GetAll().ToList();
        if (descriptors.Count == 0)
        {
            CurrentVisibleProviderId = null;
            return;
        }
        if (_currentIndex < 0 || _currentIndex >= descriptors.Count) _currentIndex = 0;
        CurrentVisibleProviderId = descriptors[_currentIndex].ProviderId;
    }
}
