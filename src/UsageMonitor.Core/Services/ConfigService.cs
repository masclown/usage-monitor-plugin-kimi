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

    /// <summary>历史数据保留点数（默认 60，可调 30/60/120）</summary>
    public int HistoryPointCount { get; set; } = 60;

    /// <summary>是否启用托盘悬浮窗（鼠标悬停托盘图标时弹出）</summary>
    public bool ShowTrayTooltip { get; set; } = true;

    /// <summary>托盘悬浮窗关闭延迟（毫秒）</summary>
    public int TrayTooltipHideDelayMs { get; set; } = 500;

    /// <summary>各 Provider 在任务栏的显示模式（key=ProviderId，缺省时为 Text）</summary>
    public Dictionary<string, TaskbarDisplayMode> ProviderTaskbarModes { get; set; } = new();

    /// <summary>圆环图警告阈值（百分比，达到后切到琥珀色，默认 60）</summary>
    public int RingChartWarningThreshold { get; set; } = 60;

    /// <summary>圆环图危险阈值（百分比，达到后切到红色，默认 85）</summary>
    public int RingChartDangerThreshold { get; set; } = 85;
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
    /// 上次 Save() 失败的错误信息（null 表示成功）。
    /// UI 可以在保存后检查这个字段，提示用户磁盘满/权限不足等问题。
    /// </summary>
    public string? LastSaveError { get; private set; }

    /// <summary>
    /// 上次 Load() 失败的错误信息（null 表示成功）。
    /// </summary>
    public string? LastLoadError { get; private set; }

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
        LastSaveError = null;
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
            // 重要：记录真实错误信息，让 UI 能提示用户（磁盘满/权限不足/文件被占用）
            LastSaveError = $"{ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-load provider config from disk into memory. Used after external tools (e.g. the
    /// BrowserLoginService) write <c>config.json</c> directly without going through
    /// <see cref="UpdateProviderConfig"/>, so the in-memory <see cref="ProviderConfig"/>
    /// for the same provider must be refreshed.
    /// <para>
    /// Implementation: re-read the file, replace <c>_settings.ProviderConfigs</c> with the
    /// freshly-loaded provider configs (preserves any other app settings). Triggers
    /// <see cref="ConfigChanged"/> so subscribers re-read state.
    /// </para>
    /// </summary>
    public void ReloadProviderConfigsFromDisk()
    {
        try
        {
            if (!File.Exists(_configFilePath)) return;
            var json = File.ReadAllText(_configFilePath, Encoding.UTF8);
            var fresh = JsonSerializer.Deserialize<AppSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (fresh?.ProviderConfigs != null)
            {
                // Replace only the ProviderConfigs dict, keep other settings
                _settings.ProviderConfigs = fresh.ProviderConfigs;
                FileLogger.Info("ConfigService",
                    $"Reloaded ProviderConfigs from disk. Count={fresh.ProviderConfigs.Count}");
            }
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            FileLogger.Error("ConfigService",
                $"ReloadProviderConfigsFromDisk failed: {ex.Message}", ex);
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
        var sensitiveKeywords = new[] { "apikey", "token", "secret", "password", "key", "cookie" };
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
