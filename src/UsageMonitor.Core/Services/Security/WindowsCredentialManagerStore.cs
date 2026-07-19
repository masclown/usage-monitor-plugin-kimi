using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace UsageMonitor.Core.Services.Security;

/// <summary>
/// Windows Credential Manager 凭据存储后端。
/// <para>
/// 通过 P/Invoke 调 <c>advapi32.dll</c> 的 <c>CredWriteW</c> / <c>CredReadW</c> / <c>CredDeleteW</c> / <c>CredEnumerateW</c>，
/// 把凭据存入 Windows 自带的"凭据管理器"（控制面板 → 用户账户 → 凭据管理器 → Windows 凭据）。
/// </para>
/// <para>
/// 优势：
/// <list type="bullet">
/// <item><description>底层用 Windows DPAPI 加密，安全性等同系统级保险箱</description></item>
/// <item><description>与 Outlook、Git Credential Manager 等系统工具同源，运维工具可直接管理</description></item>
/// <item><description>不写业务文件，避免误提交到仓库</description></item>
/// </list>
/// </para>
/// <para>
/// 限制：
/// <list type="bullet">
/// <item><description>仅 Windows；Linux/macOS 需要其他实现（本项目目前以 Windows 为主平台）</description></item>
/// <item><description>Headless 服务（Docker / Server Core）若无 user profile 会失败，
/// 此时应捕获异常并由 <see cref="SecretStoreFactory"/> 降级到 <see cref="AesGcmFileSecretStore"/></description></item>
/// </list>
/// </para>
/// </summary>
public sealed class WindowsCredentialManagerStore : ISecretStore
{
    /// <inheritdoc />
    public string BackendName => "WindowsCredentialManager";

    // Win32 CRED_TYPE / CRED_PERSIST 常量
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    // Win32 错误码：ERROR_NOT_FOUND
    private const int ERROR_NOT_FOUND = 1168;

    /// <summary>
    /// Windows <c>CREDENTIAL</c> 结构（与 advapi32.dll 一一对应）。
    /// <para>CharSet=Unicode 是因为我们调用的是 CredWriteW / CredReadW 等 W 版本。</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredEnumerate(string filter, uint flag, out uint count, out IntPtr credentialsArrayPtr);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);

    /// <inheritdoc />
    public void Set(string serviceName, string accountName, string secretData)
    {
        ValidateArgs(serviceName, accountName);
        if (secretData == null) throw new ArgumentNullException(nameof(secretData));
        // req-070 F-34：空字符串 secret 会导致 CredWrite 写入 0 长度 blob，行为未文档化
        if (string.IsNullOrEmpty(secretData))
            throw new ArgumentException("secretData 不能为空字符串", nameof(secretData));

        var target = BuildTarget(serviceName, accountName);
        var blobBytes = Encoding.UTF8.GetBytes(secretData);
        var blobPtr = Marshal.AllocHGlobal(blobBytes.Length);
        var targetPtr = IntPtr.Zero;
        var userPtr = IntPtr.Zero;
        try
        {
            Marshal.Copy(blobBytes, 0, blobPtr, blobBytes.Length);
            targetPtr = Marshal.StringToCoTaskMemUni(target);
            userPtr = Marshal.StringToCoTaskMemUni(accountName);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetPtr,
                CredentialBlobSize = (uint)blobBytes.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userPtr,
            };
            if (!CredWrite(ref credential, 0))
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err,
                    $"CredWrite 失败（target={target}，Win32 err={err}）。" +
                    "Headless 环境下请调用 SecretStoreFactory 触发降级。");
            }
        }
        finally
        {
            if (targetPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(targetPtr);
            if (userPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(userPtr);
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <inheritdoc />
    public string? Get(string serviceName, string accountName)
    {
        ValidateArgs(serviceName, accountName);
        var target = BuildTarget(serviceName, accountName);
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND) return null;
            throw new Win32Exception(err, $"CredRead 失败（target={target}，Win32 err={err}）。");
        }
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return string.Empty;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    /// <inheritdoc />
    public bool Delete(string serviceName, string accountName)
    {
        ValidateArgs(serviceName, accountName);
        var target = BuildTarget(serviceName, accountName);
        if (!CredDelete(target, CRED_TYPE_GENERIC, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND) return false;
            throw new Win32Exception(err, $"CredDelete 失败（target={target}，Win32 err={err}）。");
        }
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListAccounts(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName)) throw new ArgumentException("serviceName 不能为空", nameof(serviceName));
        var prefix = serviceName + "/";
        var accounts = new List<string>();
        if (!CredEnumerate(prefix + "*", 0, out var count, out var arrayPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND) return accounts;
            throw new Win32Exception(err, $"CredEnumerate 失败（filter={prefix}*，Win32 err={err}）。");
        }
        try
        {
            var elementSize = Marshal.SizeOf<IntPtr>();
            for (int i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(arrayPtr, i * elementSize);
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.TargetName == IntPtr.Zero) continue;
                var target = Marshal.PtrToStringUni(cred.TargetName) ?? string.Empty;
                if (target.StartsWith(prefix, StringComparison.Ordinal))
                {
                    accounts.Add(target.Substring(prefix.Length));
                }
            }
            return accounts;
        }
        finally
        {
            CredFree(arrayPtr);
        }
    }

    /// <summary>
    /// 校验 serviceName / accountName 非空（公共校验逻辑）。
    /// </summary>
    private static void ValidateArgs(string serviceName, string accountName)
    {
        if (string.IsNullOrEmpty(serviceName))
            throw new ArgumentException("serviceName 不能为空", nameof(serviceName));
        if (string.IsNullOrEmpty(accountName))
            throw new ArgumentException("accountName 不能为空", nameof(accountName));
    }

    /// <summary>
    /// 组合 target 名为 <c>"{serviceName}/{accountName}"</c>。
    /// Windows Credential Manager 的 target name 是分组的唯一标识，
    /// 使用前缀+账号避免不同 Provider 之间冲突。
    /// </summary>
    private static string BuildTarget(string serviceName, string accountName)
        => serviceName + "/" + accountName;
}