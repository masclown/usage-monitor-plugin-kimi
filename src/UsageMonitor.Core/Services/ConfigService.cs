using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 应用全局配置模型
/// </summary>
public class AppSettings
{
    /// <summary>刷新间隔（秒），默认300秒（5分钟）</summary>
    public int RefreshIntervalSeconds { get; set; } = 300;

    /// <summary>是否启用任务栏显示</summary>
    public bool ShowInTaskbar { get; set; } = true;

    /// <summary>是否开机自启</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>是否最小化到托盘</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>任务栏显示的ProviderId列表（空表示全部显示）</summary>
    public List<string> TaskbarDisplayProviders { get; set; } = new();

    /// <summary>各服务商的配置列表</summary>
    public Dictionary<string, ProviderConfig> ProviderConfigs { get; set; } = new();

    /// <summary>各插件是否启用的映射</summary>
    public Dictionary<string, bool> PluginEnabled { get; set; } = new();
}

/// <summary>
/// 配置管理服务 - 负责读写应用配置，支持API Key加密存储
/// 配置文件保存在 %AppData%/UsageMonitor/config.json
/// </summary>
public class ConfigService
{
    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private AppSettings _settings;

    /// <summary>当前应用配置</summary>
    public AppSettings Settings => _settings;

    /// <summary>配置变更事件</summary>
    public event EventHandler? ConfigChanged;

    /// <summary>
    /// 创建配置服务实例
    /// </summary>
    public ConfigService()
    {
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsageMonitor");
        _configFilePath = Path.Combine(_configDirectory, "config.json");
        _settings = new AppSettings();
    }

    /// <summary>
    /// 加载配置文件
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_configFilePath))
        {
            _settings = new AppSettings();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_configFilePath, Encoding.UTF8);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            // 解密敏感字段
            DecryptSensitiveFields();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            _settings = new AppSettings();
        }
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void Save()
    {
        try
        {
            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);

            // 加密敏感字段后保存
            var settingsToSave = CloneSettings();
            EncryptSensitiveFields(settingsToSave);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(settingsToSave, options);
            File.WriteAllText(_configFilePath, json, Encoding.UTF8);

            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取指定服务商的配置（不存在则创建默认配置）
    /// </summary>
    public ProviderConfig GetProviderConfig(string providerId, IUsageProvider? provider = null)
    {
        if (!_settings.ProviderConfigs.TryGetValue(providerId, out var config))
        {
            config = new ProviderConfig { ProviderId = providerId };

            // 使用插件定义的默认值填充
            if (provider != null)
            {
                foreach (var field in provider.ConfigFields)
                {
                    if (!string.IsNullOrEmpty(field.DefaultValue))
                        config.SetValue(field.Key, field.DefaultValue);
                }
            }

            _settings.ProviderConfigs[providerId] = config;
        }
        return config;
    }

    /// <summary>
    /// 更新指定服务商的配置
    /// </summary>
    public void UpdateProviderConfig(string providerId, ProviderConfig config)
    {
        _settings.ProviderConfigs[providerId] = config;
        Save();
    }

    /// <summary>
    /// 加密敏感字段（如Password类型的配置值）
    /// </summary>
    private void EncryptSensitiveFields(AppSettings settings)
    {
        foreach (var (_, config) in settings.ProviderConfigs)
        {
            var keysToEncrypt = config.Values.Keys.ToList();
            foreach (var key in keysToEncrypt)
            {
                if (IsSensitiveKey(key) && !string.IsNullOrEmpty(config.Values[key]))
                {
                    config.Values[key] = Encrypt(config.Values[key]);
                }
            }
        }
    }

    /// <summary>
    /// 解密敏感字段
    /// </summary>
    private void DecryptSensitiveFields()
    {
        foreach (var (_, config) in _settings.ProviderConfigs)
        {
            var keysToDecrypt = config.Values.Keys.ToList();
            foreach (var key in keysToDecrypt)
            {
                if (IsSensitiveKey(key) && !string.IsNullOrEmpty(config.Values[key]))
                {
                    try
                    {
                        config.Values[key] = Decrypt(config.Values[key]);
                    }
                    catch
                    {
                        // 解密失败则保留原值（可能是未加密的旧配置）
                    }
                }
            }
        }
    }

    /// <summary>判断是否为敏感配置键</summary>
    private static bool IsSensitiveKey(string key)
    {
        var sensitiveKeywords = new[] { "apikey", "token", "secret", "password", "key" };
        return sensitiveKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>使用DPAPI加密字符串</summary>
    private static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>使用DPAPI解密字符串</summary>
    private static string Decrypt(string cipherText)
    {
        var bytes = Convert.FromBase64String(cipherText);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>深拷贝配置对象</summary>
    private static AppSettings CloneSettings(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    private AppSettings CloneSettings()
    {
        var json = JsonSerializer.Serialize(_settings);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
