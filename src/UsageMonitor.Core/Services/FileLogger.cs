using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace UsageMonitor.Core.Services;

/// <summary>
/// Unified file logger for UsageMonitor. Writes structured logs to:
///   <c>&lt;projectRoot&gt;\logs\UsageMonitor-YYYY-MM-DD.log</c>
/// <para>
/// Format per line: <c>[HH:mm:ss.fff] [LEVEL] [Source] Message</c>
/// </para>
/// <para>
/// Used by plugins (e.g. MiniMax) to capture runtime flow when something goes wrong.
/// Logs persist across process runs; auto-trim keeps the most recent 7 files.
/// </para>
/// </summary>
public static class FileLogger
{
    /// <summary>Project root (resolved by walking up to UsageMonitor.sln).</summary>
    public static readonly string ProjectRoot = ResolveProjectRoot();

    /// <summary>Logs directory: &lt;projectRoot&gt;/logs/</summary>
    public static readonly string LogDir = Path.Combine(ProjectRoot, "logs");

    /// <summary>Concurrent write queue so multiple threads (UI + worker) can log safely.</summary>
    private static readonly BlockingCollection<LogEntry> Queue = new();

    /// <summary>Writer background thread.</summary>
    private static readonly Thread WriterThread;

    /// <summary>Lock for file rotation.</summary>
    private static readonly object RotationLock = new();

    /// <summary>Keep last N log files (rolling delete older files).</summary>
    private const int MaxFiles = 7;

    /// <summary>Stop signal.</summary>
    private static volatile bool _stop;

    static FileLogger()
    {
        Directory.CreateDirectory(LogDir);
        WriterThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "UsageMonitor.FileLogger"
        };
        WriterThread.Start();
    }

    /// <summary>Severity levels.</summary>
    public enum Level { Debug, Info, Warn, Error }

    /// <summary>One log line waiting to be flushed.</summary>
    private readonly struct LogEntry
    {
        public readonly DateTime TimeUtc;
        public readonly Level Lvl;
        public readonly string Source;
        public readonly string Message;
        public readonly Exception? Ex;

        public LogEntry(Level lvl, string source, string message, Exception? ex)
        {
            TimeUtc = DateTime.UtcNow;
            Lvl = lvl;
            Source = source ?? "Unknown";
            Message = message ?? string.Empty;
            Ex = ex;
        }
    }

    /// <summary>Quick info log with default source name.</summary>
    public static void Info(string source, string message) => Enqueue(Level.Info, source, message, null);

    /// <summary>Quick warn log with default source name.</summary>
    public static void Warn(string source, string message, Exception? ex = null)
        => Enqueue(Level.Warn, source, message, ex);

    /// <summary>Quick error log with default source name.</summary>
    public static void Error(string source, string message, Exception? ex = null)
        => Enqueue(Level.Error, source, message, ex);

    /// <summary>Quick debug log with default source name.</summary>
    public static void Debug(string source, string message)
        => Enqueue(Level.Debug, source, message, null);

    /// <summary>Enqueue a log entry for asynchronous write.</summary>
    public static void Enqueue(Level lvl, string source, string message, Exception? ex)
    {
        if (_stop) return;
        try { Queue.Add(new LogEntry(lvl, source, message, ex)); }
        catch { /* queue closed during shutdown - silently drop */ }
    }

    /// <summary>Get the path to today's log file (for diagnostic scripts).</summary>
    public static string GetCurrentLogPath()
    {
        var now = DateTime.Now;
        return Path.Combine(LogDir, $"UsageMonitor-{now:yyyy-MM-dd}.log");
    }

    /// <summary>Get full path of all log files, newest first.</summary>
    public static string[] GetLogFiles()
    {
        try
        {
            return Directory.GetFiles(LogDir, "UsageMonitor-*.log")
                .OrderByDescending(p => p)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Sync flush (use on app shutdown).</summary>
    public static void Flush()
    {
        // Drain queue synchronously
        while (Queue.TryTake(out var entry, 100))
        {
            WriteEntry(entry);
        }
    }

    /// <summary>
    /// 优雅停止后台写线程并确保队列排空：置停止标志 → CompleteAdding（仅禁止新增，已入队项仍可取出）
    /// → 等待后台线程排空剩余项后退出 → 若 Join 超时则同步写完残留，兜底不丢日志。
    /// 本方法自洽：调用它即可保证关闭时日志不丢，无需再配合 Flush，也不依赖调用顺序。
    /// </summary>
    public static void Stop()
    {
        _stop = true;
        try { Queue.CompleteAdding(); } catch { }
        try { WriterThread.Join(2000); } catch { }
        // Join 超时兜底：CompleteAdding 后 TryTake 仍能取出队列中残留项，同步写完。
        try
        {
            while (Queue.TryTake(out var entry, 0))
                WriteEntry(entry);
        }
        catch { /* never crash on shutdown */ }
    }

    private static void WriterLoop()
    {
        foreach (var entry in Queue.GetConsumingEnumerable())
        {
            try { WriteEntry(entry); }
            catch { /* never crash on log failure */ }
        }
    }

    private static void WriteEntry(LogEntry entry)
    {
        try
        {
            var localTime = entry.TimeUtc.ToLocalTime();
            var line = $"[{localTime:HH:mm:ss.fff}] [{entry.Lvl,-5}] [{entry.Source}] {entry.Message}";
            if (entry.Ex != null)
            {
                line += Environment.NewLine + $"    Exception: {entry.Ex.GetType().Name}: {entry.Ex.Message}";
                if (!string.IsNullOrEmpty(entry.Ex.StackTrace))
                    line += Environment.NewLine + "    Stack: " + entry.Ex.StackTrace;
            }
            line += Environment.NewLine;

            lock (RotationLock)
            {
                var path = GetCurrentLogPath();
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch { /* swallow IO errors */ }
    }

    /// <summary>Periodic rotation: keep only the most recent MaxFiles.</summary>
    public static void RotateIfNeeded()
    {
        try
        {
            var files = Directory.GetFiles(LogDir, "UsageMonitor-*.log")
                .OrderByDescending(p => p)
                .ToArray();
            for (int i = MaxFiles; i < files.Length; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Walk up the directory tree looking for UsageMonitor.sln to locate the project root.
    /// Falls back to the current working directory if not found.
    /// </summary>
    private static string ResolveProjectRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var slnPath = Path.Combine(dir.FullName, "UsageMonitor.sln");
                if (File.Exists(slnPath)) return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }
        return Directory.GetCurrentDirectory();
    }
}
