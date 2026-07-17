using System;

namespace UsageMonitor.Core.Services.Security;

/// <summary>
/// AES-256-GCM 降级方案所需的 Master Key 在环境变量中缺失或非法时抛出。
/// <para>
/// 错误信息会明确指出环境变量名（如 <c>USAGEMONITOR_MASTER_KEY</c>）以及期望的格式，
/// 便于运维 / 用户立即定位问题。
/// </para>
/// </summary>
public sealed class MasterKeyMissingException : Exception
{
    /// <summary>缺失/非法的环境变量名（用于 UI 提示时高亮）</summary>
    public string EnvironmentVariableName { get; }

    /// <summary>构造一个 <see cref="MasterKeyMissingException"/>。</summary>
    /// <param name="environmentVariableName">环境变量名</param>
    /// <param name="message">给用户/运维的清晰错误说明</param>
    public MasterKeyMissingException(string environmentVariableName, string message)
        : base(message)
    {
        EnvironmentVariableName = environmentVariableName;
    }

    /// <summary>包装底层异常的构造函数。</summary>
    public MasterKeyMissingException(string environmentVariableName, string message, Exception inner)
        : base(message, inner)
    {
        EnvironmentVariableName = environmentVariableName;
    }
}