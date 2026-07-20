using System.Security.Cryptography;
using System.Text;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-090-001：Cookie 文件 HMAC-SHA256 完整性签名。
/// 文件格式：magic(4) + version(1) + hmac(32) + dpapi_ciphertext。
/// 签名密钥随机生成 32 字节，经 DPAPI 加密后存 %AppData%\UsageMonitor\secret.key。
/// </summary>
public static class CookieProtection
{
    private static readonly byte[] Magic = "UMCK"u8.ToArray(); // 4 bytes
    private const byte CurrentVersion = 0x01;
    private const int HmacLength = 32; // HMAC-SHA256 output size

    private static readonly string KeyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "secret.key");

    private static byte[]? _cachedKey;
    private static readonly object _keyLock = new();

    /// <summary>
    /// 加载或创建签名密钥。密钥经 DPAPI 加密后持久化到 secret.key。
    /// </summary>
    private static byte[] LoadOrCreateSigningKey()
    {
        lock (_keyLock)
        {
            if (_cachedKey != null) return _cachedKey;

            try
            {
                if (File.Exists(KeyFilePath))
                {
                    var encrypted = File.ReadAllBytes(KeyFilePath);
                    _cachedKey = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    return _cachedKey;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warn("CookieProtection", $"读取签名密钥失败，将重新生成: {ex.Message}");
            }

            // 生成新密钥
            _cachedKey = RandomNumberGenerator.GetBytes(32);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);
                var encrypted = ProtectedData.Protect(_cachedKey, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(KeyFilePath, encrypted);
                FileLogger.Info("CookieProtection", "已生成新的 Cookie 签名密钥");
            }
            catch (Exception ex)
            {
                FileLogger.Error("CookieProtection", "保存签名密钥失败", ex);
            }
            return _cachedKey;
        }
    }

    /// <summary>
    /// 对 DPAPI 密文计算 HMAC-SHA256 签名，并按新格式拼接：magic + version + hmac + ciphertext。
    /// </summary>
    /// <param name="dpapiCiphertext">DPAPI 加密后的密文</param>
    /// <returns>带签名的新格式字节数组</returns>
    public static byte[] Protect(byte[] dpapiCiphertext)
    {
        var key = LoadOrCreateSigningKey();
        using var hmac = new HMACSHA256(key);
        var signature = hmac.ComputeHash(dpapiCiphertext);

        // magic(4) + version(1) + hmac(32) + ciphertext
        var result = new byte[Magic.Length + 1 + HmacLength + dpapiCiphertext.Length];
        Magic.CopyTo(result, 0);
        result[Magic.Length] = CurrentVersion;
        signature.CopyTo(result, Magic.Length + 1);
        dpapiCiphertext.CopyTo(result, Magic.Length + 1 + HmacLength);
        return result;
    }

    /// <summary>
    /// 验证新格式文件的 HMAC 签名，提取 DPAPI 密文。
    /// </summary>
    /// <param name="data">文件完整字节</param>
    /// <returns>DPAPI 密文；验签失败返回 null</returns>
    public static byte[]? Unprotect(byte[] data)
    {
        if (data == null || data.Length < Magic.Length + 1 + HmacLength)
            return null;

        // 校验 magic
        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i]) return null;
        }

        // 校验 version
        if (data[Magic.Length] != CurrentVersion)
        {
            FileLogger.Warn("CookieProtection", $"不支持的 Cookie 文件版本: 0x{data[Magic.Length]:X2}");
            return null;
        }

        // 提取签名和密文
        var storedHmac = new byte[HmacLength];
        Array.Copy(data, Magic.Length + 1, storedHmac, 0, HmacLength);
        var ciphertext = new byte[data.Length - Magic.Length - 1 - HmacLength];
        Array.Copy(data, Magic.Length + 1 + HmacLength, ciphertext, 0, ciphertext.Length);

        // 验签
        var key = LoadOrCreateSigningKey();
        using var hmac = new HMACSHA256(key);
        var computedHmac = hmac.ComputeHash(ciphertext);

        if (!CryptographicOperations.FixedTimeEquals(storedHmac, computedHmac))
        {
            FileLogger.Warn("CookieProtection", "Cookie 文件 HMAC 验签失败（可能被篡改）");
            return null;
        }

        return ciphertext;
    }

    /// <summary>
    /// 检测数据是否为旧格式（纯 Base64 DPAPI 文本）。
    /// </summary>
    public static bool IsLegacyFormat(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        // 旧格式是 Base64 文本，新格式是二进制（magic 开头不可打印）
        return content.Length > 0 && content[0] != (char)Magic[0];
    }
}
