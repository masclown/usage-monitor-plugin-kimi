using System;

namespace UsageMonitor.Core.Services.Security;

/// <summary>
/// 凭据存储工厂：根据运行环境自动选择 Windows Credential Manager 或 AES-256-GCM 降级方案。
/// <para>
/// 选择策略：
/// <list type="number">
/// <item><description>尝试 <see cref="WindowsCredentialManagerStore"/>：写入并读取一条探测凭据验证可用性</description></item>
/// <item><description>若探测失败（Headless / 无 user profile / DPAPI 不可用），记录警告并降级到 <see cref="AesGcmFileSecretStore"/></description></item>
/// <item><description>降级方案要求环境变量 <c>USAGEMONITOR_MASTER_KEY</c> 可用；不可用则抛 <see cref="MasterKeyMissingException"/></description></item>
/// </list>
/// </para>
/// <para>
/// 推荐业务方直接使用单例 <see cref="Current"/>，避免重复探测开销。
/// </para>
/// </summary>
public static class SecretStoreFactory
{
    private static readonly object _gate = new();
    private static ISecretStore? _current;

    /// <summary>探测时使用的临时 serviceName（避免与业务凭据冲突）。</summary>
    private const string ProbeService = "UsageMonitor.Probe";

    /// <summary>探测时使用的临时 accountName。</summary>
    private const string ProbeAccount = "__probe__";

    /// <summary>
    /// 当前进程单例的 <see cref="ISecretStore"/>。首次访问时探测一次后缓存。
    /// <para>线程安全：双重检查锁定 + lock。</para>
    /// </summary>
    public static ISecretStore Current
    {
        get
        {
            // 快速路径：避免常见情况下的锁开销
            if (_current != null) return _current;
            lock (_gate)
            {
                _current ??= CreateInternal();
                return _current;
            }
        }
    }

    /// <summary>
    /// 强制重建单例（主要用于测试 / 配置变更后重试）。
    /// </summary>
    public static void Reset()
    {
        lock (_gate) { _current = null; }
    }

    /// <summary>
    /// 选择当前进程应使用的凭据后端。
    /// </summary>
    private static ISecretStore CreateInternal()
    {
        // 1) 先尝试 Windows Credential Manager
        try
        {
            var probe = new WindowsCredentialManagerStore();
            probe.Set(ProbeService, ProbeAccount, ProbeAccount);
            var v = probe.Get(ProbeService, ProbeAccount);
            probe.Delete(ProbeService, ProbeAccount);
            if (v == ProbeAccount)
            {
                FileLogger.Info("SecretStoreFactory",
                    "凭据后端已选定：WindowsCredentialManager（Windows Credential Manager 可用）");
                return probe;
            }
            FileLogger.Warn("SecretStoreFactory",
                "Windows Credential Manager 探测读写不一致，降级到 AES-GCM 文件方案。");
        }
        catch (Exception ex)
        {
            // Headless / 无 user profile / 权限不足都进这里
            FileLogger.Warn("SecretStoreFactory",
                $"Windows Credential Manager 不可用，降级到 AES-GCM 文件方案。原因: {ex.GetType().Name}: {ex.Message}");
        }

        // 2) 降级到 AES-256-GCM 加密文件
        try
        {
            var fallback = new AesGcmFileSecretStore();
            FileLogger.Info("SecretStoreFactory",
                $"凭据后端已选定：{fallback.BackendName}（AES-256-GCM 加密文件）");
            return fallback;
        }
        catch (MasterKeyMissingException ex)
        {
            // 主方案 + 降级方案都不可用，错误信息已明确指出环境变量名
            FileLogger.Error("SecretStoreFactory", ex.Message, ex);
            throw;
        }
        catch (Exception ex)
        {
            FileLogger.Error("SecretStoreFactory",
                $"AES-256-GCM 文件后端初始化失败: {ex.Message}", ex);
            throw;
        }
    }
}