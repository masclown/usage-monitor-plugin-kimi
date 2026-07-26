using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UsageMonitor.Core.Services.Display;

/// <summary>
/// 显示资源包注册表（req-115）：集中持有四类包（主题 / 图表样式 / mini 图表样式 / 悬浮窗模板），
/// 挂载与 plugins/ 平级的四个目录监视器实现热重载，任一目录变更后重扫并触发 <see cref="Changed"/>。
/// <para>Core 层只负责"扫描 + 持有 + 通知"；具体渲染（构造 WPF 资源 / 应用色阶）由 App 层消费。</para>
/// </summary>
public sealed class DisplayPackRegistry : IDisposable
{
    private readonly string _themesDir;
    private readonly string _chartsDir;
    private readonly string _miniChartsDir;
    private readonly string _trayTooltipsDir;
    private readonly object _lock = new();
    private readonly List<DebouncedDirectoryWatcher> _watchers = new();
    private bool _disposed;

    private List<ThemePack> _themePacks = new();
    private List<ChartStylePack> _chartStylePacks = new();
    private List<MiniChartStylePack> _miniChartStylePacks = new();
    private List<TrayTooltipPack> _trayTooltipPacks = new();

    /// <summary>任一显示资源包目录变更并完成重扫后触发（回调在计时器线程，订阅方自行派发到 UI 线程）。</summary>
    public event EventHandler? Changed;

    /// <summary>已加载的主题包（线程安全快照）。</summary>
    public IReadOnlyList<ThemePack> ThemePacks { get { lock (_lock) return _themePacks.ToList(); } }

    /// <summary>已加载的图表样式包（线程安全快照）。</summary>
    public IReadOnlyList<ChartStylePack> ChartStylePacks { get { lock (_lock) return _chartStylePacks.ToList(); } }

    /// <summary>已加载的 mini 图表样式包（线程安全快照）。</summary>
    public IReadOnlyList<MiniChartStylePack> MiniChartStylePacks { get { lock (_lock) return _miniChartStylePacks.ToList(); } }

    /// <summary>已加载的悬浮窗模板包（线程安全快照）。</summary>
    public IReadOnlyList<TrayTooltipPack> TrayTooltipPacks { get { lock (_lock) return _trayTooltipPacks.ToList(); } }

    /// <summary>
    /// 创建注册表（四个目录默认位于程序目录下，与 plugins/ 平级）。
    /// </summary>
    /// <param name="baseDirectory">基目录（默认 <see cref="AppDomain.BaseDirectory"/>）。</param>
    public DisplayPackRegistry(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        _themesDir = Path.Combine(root, "themes");
        _chartsDir = Path.Combine(root, "charts");
        _miniChartsDir = Path.Combine(root, "minicharts");
        _trayTooltipsDir = Path.Combine(root, "traytooltips");
    }

    /// <summary>按 Id 查主题包（未命中返回 null）。</summary>
    /// <param name="id">主题包 Id。</param>
    public ThemePack? GetThemePack(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : ThemePacks.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 Id 查图表样式包（未命中返回 null）。</summary>
    /// <param name="id">图表样式包 Id。</param>
    public ChartStylePack? GetChartStylePack(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : ChartStylePacks.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 Id 查 mini 图表样式包（未命中返回 null）。</summary>
    /// <param name="id">mini 图表样式包 Id。</param>
    public MiniChartStylePack? GetMiniChartStylePack(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : MiniChartStylePacks.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 Id 查悬浮窗模板包（未命中返回 null）。</summary>
    /// <param name="id">悬浮窗模板包 Id。</param>
    public TrayTooltipPack? GetTrayTooltipPack(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : TrayTooltipPacks.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 重新扫描四类包目录（幂等，可由启动、手动刷新或目录监视触发）。
    /// </summary>
    public void Reload()
    {
        var themes = DisplayPackLoader.LoadThemePacks(_themesDir);
        var charts = DisplayPackLoader.LoadChartStylePacks(_chartsDir);
        var minis = DisplayPackLoader.LoadMiniChartStylePacks(_miniChartsDir);
        var trays = DisplayPackLoader.LoadTrayTooltipPacks(_trayTooltipsDir);

        lock (_lock)
        {
            _themePacks = themes;
            _chartStylePacks = charts;
            _miniChartStylePacks = minis;
            _trayTooltipPacks = trays;
        }
        FileLogger.Info("DisplayPackRegistry",
            $"显示资源包扫描完成：主题 {themes.Count}、图表 {charts.Count}、mini {minis.Count}、悬浮窗 {trays.Count}");
    }

    /// <summary>
    /// 启动监视：首次扫描后为四个目录挂 <see cref="DebouncedDirectoryWatcher"/>，变更即重扫并触发 <see cref="Changed"/>。
    /// </summary>
    public void StartWatching()
    {
        Reload();
        foreach (var dir in new[] { _themesDir, _chartsDir, _miniChartsDir, _trayTooltipsDir })
        {
            var watcher = new DebouncedDirectoryWatcher(dir, OnPackDirChanged);
            watcher.Start();
            _watchers.Add(watcher);
        }
    }

    /// <summary>目录变更回调：重扫全部包并通知消费方。</summary>
    private void OnPackDirChanged()
    {
        Reload();
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            FileLogger.Error("DisplayPackRegistry", $"Changed handler threw: {ex.Message}", ex);
        }
    }

    /// <summary>释放全部目录监视器。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
