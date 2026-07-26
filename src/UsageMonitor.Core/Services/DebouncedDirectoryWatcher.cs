using System;
using System.IO;
using System.Threading;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 防抖目录监视器（req-111）：封装 <see cref="FileSystemWatcher"/>，
/// 将短时间内的多次文件变更（Created/Changed/Deleted/Renamed，含子目录）合并为一次回调。
/// <para>典型用途：监视 plugins/ 与显示资源包目录，变更后触发热重载管线。
/// 回调在计时器线程触发，调用方自行负责派发到 UI 线程。</para>
/// <para>提供 <see cref="Pause"/>/<see cref="Resume"/> 供安装器写包期间挂起监听，避免复制半途触发重载；
/// <see cref="NotifyChanged"/> 公开供单测直接注入变更事件（不依赖真实文件系统事件）。</para>
/// </summary>
public sealed class DebouncedDirectoryWatcher : IDisposable
{
    private readonly string _directory;
    private readonly int _debounceMs;
    private readonly Action _callback;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private int _pauseDepth;
    private bool _disposed;

    /// <summary>
    /// 创建防抖目录监视器。
    /// </summary>
    /// <param name="directory">要监视的目录（不存在时 <see cref="Start"/> 会自动创建）。</param>
    /// <param name="callback">防抖窗口结束后触发的回调（计时器线程）。</param>
    /// <param name="debounceMs">防抖窗口毫秒数（默认 800ms，窗口内新事件会重置计时）。</param>
    public DebouncedDirectoryWatcher(string directory, Action callback, int debounceMs = 800)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _debounceMs = debounceMs > 0 ? debounceMs : 800;
    }

    /// <summary>
    /// 启动文件系统监听：目录不存在时先创建；重复调用为幂等空操作。
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _watcher != null) return;

            try
            {
                if (!Directory.Exists(_directory))
                    Directory.CreateDirectory(_directory);

                _watcher = new FileSystemWatcher(_directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Created += OnFileSystemEvent;
                _watcher.Changed += OnFileSystemEvent;
                _watcher.Deleted += OnFileSystemEvent;
                _watcher.Renamed += OnFileSystemEvent;
                _watcher.EnableRaisingEvents = true;
                FileLogger.Info("DirectoryWatcher", $"已启动目录监视: {_directory}（防抖 {_debounceMs}ms）");
            }
            catch (Exception ex)
            {
                FileLogger.Error("DirectoryWatcher", $"启动目录监视失败: {_directory} - {ex.Message}", ex);
                _watcher?.Dispose();
                _watcher = null;
            }
        }
    }

    /// <summary>
    /// 挂起监听（可嵌套）：挂起期间的文件事件被丢弃，供安装器复制文件期间调用。
    /// </summary>
    public void Pause()
    {
        Interlocked.Increment(ref _pauseDepth);
    }

    /// <summary>
    /// 恢复监听（与 <see cref="Pause"/> 配对）：不会补发挂起期间丢弃的事件，
    /// 需要立即重载时由调用方显式触发（如安装完成后手动调用重载管线）。
    /// </summary>
    public void Resume()
    {
        // 防御：Resume 多于 Pause 时不允许降到负数
        if (Interlocked.Decrement(ref _pauseDepth) < 0)
            Interlocked.Exchange(ref _pauseDepth, 0);
    }

    /// <summary>
    /// 注入一次“目录已变更”通知并启动/重置防抖计时（文件事件与单测共用入口）。
    /// </summary>
    public void NotifyChanged()
    {
        if (_disposed || Volatile.Read(ref _pauseDepth) > 0) return;

        lock (_lock)
        {
            if (_disposed) return;
            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(OnDebounceElapsed, null, _debounceMs, Timeout.Infinite);
            }
            else
            {
                // 窗口内再次变更：重置计时，把连续变更合并为一次回调
                _debounceTimer.Change(_debounceMs, Timeout.Infinite);
            }
        }
    }

    /// <summary>
    /// FileSystemWatcher 原始事件入口：统一折算为一次防抖通知。
    /// </summary>
    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        NotifyChanged();
    }

    /// <summary>
    /// 防抖窗口结束：触发回调（异常兜底记日志，避免计时器线程异常导致进程崩溃）。
    /// </summary>
    private void OnDebounceElapsed(object? state)
    {
        if (_disposed || Volatile.Read(ref _pauseDepth) > 0) return;
        try
        {
            _callback();
        }
        catch (Exception ex)
        {
            FileLogger.Error("DirectoryWatcher", $"变更回调执行失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 释放监视器与防抖计时器。
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
