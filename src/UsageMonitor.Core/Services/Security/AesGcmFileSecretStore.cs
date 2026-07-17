using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageMonitor.Core.Services.Security;

/// <summary>
/// AES-256-GCM 加密文件凭据存储后端（Windows Credential Manager 不可用时的降级方案）。
/// <para>
/// 文件布局：<c>%AppData%/UsageMonitor/secrets/{serviceName}.bin</c>
/// <list type="bullet">
/// <item><description>每条凭据（<c>accountName → secretData</c>）序列化为 JSON，整体用 AES-256-GCM 加密</description></item>
/// <item><description>密文信封格式：<c>nonce(12) || ciphertext(N) || tag(16)</c></description></item>
/// <item><description>Master Key 从环境变量 <c>USAGEMONITOR_MASTER_KEY</c> 读取（base64 编码的 32 字节）</description></item>
/// <item><description>缺失或非法时构造时立即抛 <see cref="MasterKeyMissingException"/>，错误信息明确指出环境变量名</description></item>
/// </list>
/// </para>
/// <para>
/// 适用场景：
/// <list type="bullet">
/// <item><description>Headless 服务（Docker / Windows Server Core / CI）无 user profile，Credential Manager 不可用</description></item>
/// <item><description>用户偏好把凭据放本地文件而非 Windows 凭据管理器</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AesGcmFileSecretStore : ISecretStore
{
    /// <inheritdoc />
    public string BackendName => "AesGcmFile";

    /// <summary>GCM 标准 nonce 长度（96-bit）</summary>
    private const int NonceSize = 12;

    /// <summary>GCM 标准 tag 长度（128-bit）</summary>
    private const int TagSize = 16;

    /// <summary>AES-256 密钥长度（256-bit = 32 字节）</summary>
    private const int KeySize = 32;

    /// <summary>Master Key 环境变量名（必须为 base64 编码的 32 字节）</summary>
    public const string MasterKeyEnvironmentVariable = "USAGEMONITOR_MASTER_KEY";

    private readonly string _storageDir;
    private readonly byte[] _masterKey;

    /// <summary>
    /// 构造一个 AES-GCM 文件凭据存储实例。
    /// <para>构造时会立即校验 Master Key 环境变量，不可用则抛 <see cref="MasterKeyMissingException"/>。</para>
    /// </summary>
    /// <param name="storageDir">凭据文件根目录；为 null 时使用 <c>%AppData%/UsageMonitor/secrets/</c></param>
    /// <exception cref="MasterKeyMissingException">环境变量 <c>USAGEMONITOR_MASTER_KEY</c> 未设置或不是合法的 32 字节 base64 串</exception>
    public AesGcmFileSecretStore(string? storageDir = null)
    {
        _storageDir = storageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsageMonitor", "secrets");
        _masterKey = LoadMasterKeyFromEnv();
        try
        {
            if (!Directory.Exists(_storageDir))
                Directory.CreateDirectory(_storageDir);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"无法创建凭据存储目录 '{_storageDir}'，请检查磁盘权限或环境变量配置。", ex);
        }
    }

    /// <summary>
    /// 从环境变量读取并解码 Master Key。
    /// <para>缺失时错误信息明确指出环境变量名以及期望的格式（base64 编码的 32 字节）。</para>
    /// </summary>
    private static byte[] LoadMasterKeyFromEnv()
    {
        var raw = Environment.GetEnvironmentVariable(MasterKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new MasterKeyMissingException(MasterKeyEnvironmentVariable,
                $"AES-256-GCM 降级方案需要 Master Key，但环境变量 {MasterKeyEnvironmentVariable} 未设置。" +
                $"请通过 `setx {MasterKeyEnvironmentVariable} <base64-32-bytes>` 设置后重启应用。" +
                "生成示例 (PowerShell): `[Convert]::ToBase64String((1..32|%{{Get-Random -Max 256}}) -as [byte[]])`");
        }
        try
        {
            var key = Convert.FromBase64String(raw.Trim());
            if (key.Length != KeySize)
            {
                throw new MasterKeyMissingException(MasterKeyEnvironmentVariable,
                    $"环境变量 {MasterKeyEnvironmentVariable} 解码后为 {key.Length} 字节，要求 {KeySize} 字节（AES-256）。" +
                    "请重新生成 32 字节随机密钥并 base64 编码后设置。");
            }
            return key;
        }
        catch (FormatException ex)
        {
            throw new MasterKeyMissingException(MasterKeyEnvironmentVariable,
                $"环境变量 {MasterKeyEnvironmentVariable} 不是合法的 base64 字符串。", ex);
        }
    }

    /// <inheritdoc />
    public void Set(string serviceName, string accountName, string secretData)
    {
        ValidateArgs(serviceName, accountName);
        if (secretData == null) throw new ArgumentNullException(nameof(secretData));

        var dict = LoadOrInit(serviceName);
        dict[accountName] = secretData;
        Save(serviceName, dict);
    }

    /// <inheritdoc />
    public string? Get(string serviceName, string accountName)
    {
        ValidateArgs(serviceName, accountName);
        var dict = LoadOrInit(serviceName);
        return dict.TryGetValue(accountName, out var v) ? v : null;
    }

    /// <inheritdoc />
    public bool Delete(string serviceName, string accountName)
    {
        ValidateArgs(serviceName, accountName);
        var dict = LoadOrInit(serviceName);
        if (!dict.Remove(accountName)) return false;
        Save(serviceName, dict);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListAccounts(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            throw new ArgumentException("serviceName 不能为空", nameof(serviceName));
        var dict = LoadOrInit(serviceName);
        return new List<string>(dict.Keys);
    }

    /// <summary>公共参数校验（serviceName / accountName 非空）。</summary>
    private static void ValidateArgs(string serviceName, string accountName)
    {
        if (string.IsNullOrEmpty(serviceName))
            throw new ArgumentException("serviceName 不能为空", nameof(serviceName));
        if (string.IsNullOrEmpty(accountName))
            throw new ArgumentException("accountName 不能为空", nameof(accountName));
    }

    /// <summary>根据 serviceName 派生加密文件路径（剔除文件名非法字符）。</summary>
    private string FilePath(string serviceName) =>
        Path.Combine(_storageDir, SanitizeFileName(serviceName) + ".bin");

    /// <summary>把任意字符串转换为合法文件名（替换非法字符为下划线）。</summary>
    private static string SanitizeFileName(string serviceName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(serviceName.Length);
        foreach (var c in serviceName)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>加载 serviceName 对应的凭据字典；文件不存在返回空字典。</summary>
    private Dictionary<string, string> LoadOrInit(string serviceName)
    {
        var path = FilePath(serviceName);
        if (!File.Exists(path)) return new Dictionary<string, string>();
        var cipher = File.ReadAllBytes(path);
        var plain = DecryptBytes(cipher);
        try
        {
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            throw new CryptographicException(
                $"凭据文件 '{path}' 解密成功但 JSON 解析失败，疑似已损坏或 Master Key 不匹配。", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>把 serviceName 对应的字典加密后原子写入磁盘。</summary>
    private void Save(string serviceName, Dictionary<string, string> dict)
    {
        var json = JsonSerializer.Serialize(dict);
        var plain = Encoding.UTF8.GetBytes(json);
        try
        {
            var cipher = EncryptBytes(plain);
            var finalPath = FilePath(serviceName);
            var tmpPath = finalPath + ".tmp";
            File.WriteAllBytes(tmpPath, cipher);
            if (new FileInfo(tmpPath).Length <= 0)
                throw new IOException("写入临时凭据文件后大小为 0，放弃替换以保护原文件。");
            if (File.Exists(finalPath))
                File.Replace(tmpPath, finalPath, null);
            else
                File.Move(tmpPath, finalPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>AES-256-GCM 加密，输出 <c>nonce(12) || ciphertext(N) || tag(16)</c> 信封。</summary>
    private byte[] EncryptBytes(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        var output = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, NonceSize + cipher.Length, TagSize);
        return output;
    }

    /// <summary>AES-256-GCM 解密，输入必须符合 <c>nonce(12) || ciphertext(N) || tag(16)</c> 信封。</summary>
    private byte[] DecryptBytes(byte[] envelope)
    {
        if (envelope.Length < NonceSize + TagSize)
            throw new CryptographicException("加密凭据文件长度不足（疑似被截断或损坏）。");
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherLen = envelope.Length - NonceSize - TagSize;
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(envelope, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(envelope, NonceSize, cipher, 0, cipherLen);
        Buffer.BlockCopy(envelope, NonceSize + cipherLen, tag, 0, TagSize);
        var plain = new byte[cipherLen];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}