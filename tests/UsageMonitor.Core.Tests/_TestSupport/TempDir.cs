using System.IO;

namespace UsageMonitor.Core.Tests._TestSupport;

/// <summary>
/// req-059-001: 测试用临时目录工具。
/// 每个测试用例构造一个独立目录，测试结束自动删除，确保互不污染。
/// <para>
/// 实现为 <see cref="IDisposable"/>，配合 xUnit 的 <c>using</c> 模式：
/// 构造函数创建目录，<see cref="Dispose"/> 删除目录（含子文件）。
/// </para>
/// </summary>
public sealed class TempDir : IDisposable
{
    private bool _disposed;

    /// <summary>临时目录的完整路径</summary>
    public string Path { get; }

    /// <summary>
    /// 在系统临时目录下创建形如 <c>UsageMonitor-Tests-{guid}</c> 的目录。
    /// </summary>
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"UsageMonitor-Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// 拼接子路径并返回完整路径（不创建子目录）。
    /// </summary>
    public string Combine(params string[] parts)
    {
        return System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    }

    /// <summary>回收：递归删除整个临时目录及其内容。允许失败（测试中可能已被删除）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            /* 清理失败不阻断测试结束 */
        }
    }
}
