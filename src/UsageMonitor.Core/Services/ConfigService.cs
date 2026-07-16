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

    /// <summary>托盘悬浮窗触发区域宽度（像素，屏幕右下角向左延伸），默认 200</summary>
    public int TrayTriggerWidth { get; set; } = 200;

    /// <summary>托盘悬浮窗触发区域高度（像素，工作区底部向下延伸），默认 40</summary>
    public int TrayTriggerHeight { get; set; } = 40;

    /// <summary>各 Provider 在任务栏的显示模式（key=ProviderId，缺省时为 Text）</summary>
    public Dictionary<string, TaskbarDisplayMode> ProviderTaskbarModes { get; set; } = new();

    /// <summary>圆环图警告阈值（百分比，达到后切到琥珀色，默认 60）</summary>
    public int RingChartWarningThreshold { get; set; } = 60;

    /// <summary>圆环图危险阈值（百分比，达到后切到红色，默认 85）</summary>
    public int RingChartDangerThreshold { get; set; } = 85;

    /// <summary>应用外观主题（深色 / 浅色）。启动时由 ThemeManager 应用，默认深色。</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>各 Provider 在主窗口卡片中展示的图表类型（key=ProviderId，缺省为 None 仅进度条）</summary>
    public Dictionary<string, CardChartKind> ProviderCardCharts { get; set; } = new();

    /// <summary>
    /// 托盘悬浮窗位置（屏幕坐标系，单位：像素）。
    /// <list type="bullet">
    /// <item><description>null = 从未拖拽过，使用默认行为（弹出于托盘图标/光标附近）</description></item>
    /// <item><description>非 null = 用户拖拽后保存的坐标，下次弹出悬浮窗直接用此位置</description></item>
    /// </list>
    /// 屏幕坐标系遵循 WPF SystemParameters.PrimaryScreen*，原点在左上角，正方向向右/向下。
    /// </summary>
    public TrayTooltipPosition? TrayTooltipPosition { get; set; }

    /// <summary>
    /// 任务栏窗口在父任务栏坐标系内的水平相对位置（0~1 浮点）。
    /// <list type="bullet">
    /// <item><description>0 = 任务栏最左</description></item>
    /// <item><description>1 = 任务栏最右（贴近通知区域）</description></item>
    /// <item><description>null = 从未拖拽过，使用默认位置（任务栏右端留通知区域）</description></item>
    /// </list>
    /// 任务栏宽度变化或 DPI 变更后自动适配，与绝对像素相比不会越界。
    /// </summary>
    public double? TaskbarRelativeX { get; set; }

    /// <summary>
    /// 任务栏窗口用户手动拖拽两侧边缘调整并持久化的宽度（像素）。
    /// <list type="bullet">
    /// <item><description>null = 从未手动调整，使用内容自适应默认宽度（文字模式按内容测量）</description></item>
    /// <item><description>非 null = 用户拖拽保存的固定宽度，优先于自适应</description></item>
    /// </list>
    /// </summary>
    public double? TaskbarWidth { get; set; }
}

/// <summary>
/// 托盘悬浮窗拖拽后保存的位置（屏幕坐标系 X/Y，设备无关单位 DIP）。
/// 独立于窗口尺寸：只记住位置，宽高仍按 WPF 实际渲染值计算。
/// </summary>
public class TrayTooltipPosition
{
    /// <summary>悬浮窗左上角的 X 坐标（屏幕坐标系）</summary>
    public double X { get; set; }

    /// <summary>悬浮窗左上角的 Y 坐标（屏幕坐标系）</summary>
    public double Y { get; set; }
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

    /// <summary>配置文件读写互斥锁：保证多线程下 Save/Load/UpdateProviderConfig/ReloadProviderConfigsFromDisk 的原子性，避免相互覆盖或写坏文件。</summary>
    private readonly object _ioLock = new();

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
        // 读写主体加锁，避免与并发 Save 相互干扰。lock 可重入：文件不存在时内部调用的 Save() 会再次进入同一把锁。
        lock (_ioLock)
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
                LastLoadError = $"{ex.GetType().Name}: {ex.Message}";
                FileLogger.Error("ConfigService", $"加载配置失败: {ex.Message}", ex);
                _settings = new AppSettings();
            }
        }
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void Save()
    {
        LastSaveError = null;
        bool changed = false;
        // 写文件主体加锁，保证原子写入；事件在锁外触发，避免订阅方回调在持锁期间引发长时间持锁/重入。
        lock (_ioLock)
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
                changed = true;
            }
            catch (Exception ex)
            {
                // 重要：记录真实错误信息，让 UI 能提示用户（磁盘满/权限不足/文件被占用）
                LastSaveError = $"{ex.GetType().Name}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
            }
        }

        if (changed)
            ConfigChanged?.Invoke(this, EventArgs.Empty);
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
        bool changed = false;
        // 读文件 + 替换字典加锁；事件锁外触发。
        lock (_ioLock)
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
                changed = true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("ConfigService",
                    $"ReloadProviderConfigsFromDisk failed: {ex.Message}", ex);
            }
        }

        if (changed)
            ConfigChanged?.Invoke(this, EventArgs.Empty);
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
        // 先在锁内更新内存字典，再调用 Save()（Save 自身加锁并在锁外触发 ConfigChanged）。
        lock (_ioLock)
        {
            _settings.ProviderConfigs[providerId] = config;
        }
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
                    catch (Exception ex)
                    {
                        // 解密失败则保留原值（可能是未加密的旧配置）；记录告警便于诊断，绝不记明文值。
                        FileLogger.Warn("ConfigService",
                            $"解密字段失败，已保留原值。key={key}, 原因={ex.GetType().Name}: {ex.Message}");
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
