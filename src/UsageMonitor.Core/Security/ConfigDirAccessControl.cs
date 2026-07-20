namespace UsageMonitor.Core.Security;

/// <summary>
/// req-089-002：config.json 所在目录 ACL 收紧。
/// 复用 <see cref="CookieDirAccessControl.ApplyTightening"/>，应用到 %AppData%\UsageMonitor\。
/// </summary>
public static class ConfigDirAccessControl
{
    /// <summary>
    /// 对 config.json 所在目录应用 ACL 收紧。
    /// </summary>
    /// <param name="configFilePath">config.json 完整路径</param>
    /// <returns>是否成功应用</returns>
    public static bool ApplyTightening(string configFilePath)
    {
        var dir = Path.GetDirectoryName(configFilePath);
        if (string.IsNullOrEmpty(dir))
            return false;

        return CookieDirAccessControl.ApplyTightening(dir);
    }

    /// <summary>
    /// 检测 config.json 所在目录是否已收紧。
    /// </summary>
    public static bool IsTightened(string configFilePath)
    {
        var dir = Path.GetDirectoryName(configFilePath);
        if (string.IsNullOrEmpty(dir))
            return false;

        return CookieDirAccessControl.IsTightened(dir);
    }
}
