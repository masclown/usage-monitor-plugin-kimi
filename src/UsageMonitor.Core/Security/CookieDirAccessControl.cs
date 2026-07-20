using System.Security.AccessControl;
using System.Security.Principal;

namespace UsageMonitor.Core.Security;

/// <summary>
/// req-089：Cookie 目录 NTFS ACL 收紧工具。
/// 对 %AppData%\UsageMonitor\cookies\ 做 ACL 收紧，仅当前 Windows 用户有 FullControl。
/// 非 NTFS 文件系统或权限不足时降级为 WARN 日志，不抛异常。
/// </summary>
public static class CookieDirAccessControl
{
    /// <summary>
    /// 对指定目录应用 ACL 收紧：阻断继承、去除 Users/Authenticated Users、添加当前用户 FullControl。
    /// 幂等：已收紧的目录跳过。非 NTFS 或权限不足时写 WARN 日志不抛异常。
    /// </summary>
    /// <param name="dirPath">目标目录路径</param>
    /// <returns>是否成功应用（true=已收紧或本次收紧成功，false=降级跳过）</returns>
    public static bool ApplyTightening(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            return false;

        try
        {
            // 幂等检测：已阻断继承则认为已收紧
            if (IsTightened(dirPath))
            {
                Services.FileLogger.Info("CookieACL", $"目录已收紧，跳过: {dirPath}");
                return true;
            }

            var dirInfo = new DirectoryInfo(dirPath);
            var acl = dirInfo.GetAccessControl();

            // 阻断继承，不保留继承的权限
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // 移除显式授予 Users / Authenticated Users 的规则
            var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                var sid = rule.IdentityReference as SecurityIdentifier;
                if (sid != null && (sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ||
                                    sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)))
                {
                    acl.RemoveAccessRule(rule);
                }
            }

            // 添加当前用户 FullControl
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                acl.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            dirInfo.SetAccessControl(acl);
            Services.FileLogger.Info("CookieACL", $"ACL 收紧成功: {dirPath} (用户={currentUser?.Value})");
            return true;
        }
        catch (PrivilegeNotHeldException ex)
        {
            Services.FileLogger.Warn("CookieACL", $"权限不足，跳过 ACL 收紧: {dirPath} - {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Services.FileLogger.Warn("CookieACL", $"访问被拒绝，跳过 ACL 收紧: {dirPath} - {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Services.FileLogger.Error("CookieACL", $"ACL 收紧异常: {dirPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// 检测目录是否已收紧（阻断继承即认为已收紧）。
    /// </summary>
    /// <param name="dirPath">目标目录路径</param>
    /// <returns>true=已阻断继承</returns>
    public static bool IsTightened(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            return false;

        try
        {
            var acl = new DirectoryInfo(dirPath).GetAccessControl();
            return acl.AreAccessRulesProtected;
        }
        catch
        {
            return false;
        }
    }
}
