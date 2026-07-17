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

    /// <summary>各 Provider 在主窗口卡片中展示的图表类型（key=ProviderId，缺省为 None 仅进度条）。
    /// <para>遗留的「单选」字段：仅用于向 <see cref="ProviderCardChartKinds"/> 迁移旧配置，新逻辑一律读写多选集合。</para></summary>
    public Dictionary<string, CardChartKind> ProviderCardCharts { get; set; } = new();

    /// <summary>
    /// 各 Provider 在主窗口卡片中展示的图表类型「集合」（多选，key=ProviderId）。
    /// <para>
    /// 取代原先的单选 <see cref="ProviderCardCharts"/>：一个插件可同时勾选多个图表（如 MiniMax 的折线图 + 热力图），
    /// 卡片会按此集合叠加展示。首次从旧配置迁移时，会把 <see cref="ProviderCardCharts"/> 中的单值包装成单元素列表。
    /// 空列表或缺省表示不显示任何卡片图表（仅保留进度条）。
    /// </para>
    /// </summary>
    public Dictionary<string, List<CardChartKind>> ProviderCardChartKinds { get; set; } = new();

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

    /// <summary>
    /// 用量色阶配置（按已用百分比换色的阈值 + 颜色 + 是否启用）。
    /// <para>
    /// 为空时由 <see cref="GetEffectiveUsageTierConfig"/> 回退到出厂默认 4 档（低/注意/中/高）。
    /// 详情见 <see cref="UsageTierConfig.Defaults"/>。
    /// </para>
    /// </summary>
    public List<UsageMonitor.Core.Models.UsageTierConfig> UsageTierConfig { get; set; } = new();
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
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidDataException("配置文件为空（可能上次写入被中断）。");
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                // 解密敏感字段
                DecryptSensitiveFields();
            }
            catch (Exception ex)
            {
                LastLoadError = $"{ex.GetType().Name}: {ex.Message}";
                FileLogger.Error("ConfigService", $"加载配置失败: {ex.Message}", ex);
                // 保护用户配置：优先从上次成功保存的 .bak 恢复；恢复失败再备份损坏文件并回退到默认配置。
                // 避免「config.json 被写坏 → 静默重置为空 → 插件启用状态 / Cookie 等全部丢失」。
                if (!TryRecoverFromBackup())
                {
                    BackupCorruptedConfig();
                    _settings = new AppSettings();
                }
            }
        }
    }

    /// <summary>
    /// 尝试从上次成功保存留下的 <c>config.json.bak</c>（由原子写入 File.Replace 生成）恢复配置。
    /// 恢复成功后解密敏感字段并原子写回正式文件，返回 true。
    /// </summary>
    private bool TryRecoverFromBackup()
    {
        var bakPath = _configFilePath + ".bak";
        if (!File.Exists(bakPath)) return false;
        try
        {
            var json = File.ReadAllText(bakPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return false;
            var recovered = JsonSerializer.Deserialize<AppSettings>(json);
            if (recovered == null) return false;
            _settings = recovered;
            DecryptSensitiveFields();
            FileLogger.Warn("ConfigService", "config.json 损坏，已从 config.json.bak 成功恢复配置。");
            Save(); // 原子写回，修复损坏的正式文件（_ioLock 可重入）
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("ConfigService", $"从 config.json.bak 恢复失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>把损坏的 config.json 复制一份备份（.corrupted-时间戳），便于事后排查/手工恢复，避免被后续 Save 覆盖丢失。</summary>
    private void BackupCorruptedConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath)) return;
            var dst = _configFilePath + $".corrupted-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_configFilePath, dst, overwrite: true);
            FileLogger.Warn("ConfigService", $"已备份损坏的配置到 {Path.GetFileName(dst)}");
        }
        catch { /* 备份失败不阻断启动 */ }
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
                // 原子写入：先写临时文件，校验非空后用 File.Replace 原子替换，并保留 .bak 备份。
                // 直接 File.WriteAllText 若在写入中途被中断（进程退出/断电），会留下空或半截的 config.json，
                // 下次启动反序列化失败即导致配置被重置（插件启用状态、Cookie 等全部丢失）。
                var tmpPath = _configFilePath + ".tmp";
                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                if (new FileInfo(tmpPath).Length <= 0)
                    throw new IOException("写入临时配置文件后大小为 0，放弃替换以保护原配置。");
                if (File.Exists(_configFilePath))
                    File.Replace(tmpPath, _configFilePath, _configFilePath + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(tmpPath, _configFilePath);
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
    /// 获取当前生效的用量色阶配置（<see cref="AppSettings.UsageTierConfig"/> 为空时返回出厂默认 4 档）。
    /// <para>
    /// 返回一个新 List（不返回内部引用），避免调用方直接修改内部集合引发不一致；序列化时由 <c>JsonSerializer</c> 负责写回。
    /// </para>
    /// </summary>
    public List<UsageMonitor.Core.Models.UsageTierConfig> GetEffectiveUsageTierConfig()
    {
        lock (_ioLock)
        {
            if (_settings.UsageTierConfig != null && _settings.UsageTierConfig.Count > 0)
                return new List<UsageMonitor.Core.Models.UsageTierConfig>(_settings.UsageTierConfig);
            return UsageMonitor.Core.Models.UsageTierConfig.Defaults();
        }
    }

    /// <summary>
    /// 写入用量色阶配置（仅更新内存，不自动 Save；调用方控制持久化时机以实现"先预览后保存"语义）。
    /// </summary>
    /// <param name="tiers">新的档位集合（按调用方意愿的顺序传入；运行时会再按 MinPercent 升序排序）。</param>
    public void SetUsageTierConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig> tiers)
    {
        lock (_ioLock)
        {
            _settings.UsageTierConfig = tiers != null
                ? new List<UsageMonitor.Core.Models.UsageTierConfig>(tiers)
                : new List<UsageMonitor.Core.Models.UsageTierConfig>();
        }
    }

    /// <summary>
    /// 获取指定 Provider 当前的「卡片图表类型集合」（多选）。
    /// <para>
    /// 兼容旧配置：若多选字典 <see cref="AppSettings.ProviderCardChartKinds"/> 尚无该 Provider，
    /// 但旧单选 <see cref="AppSettings.ProviderCardCharts"/> 中存在且非 None，则把单值迁移为单元素列表并写回内存（下次 Save 一并持久化）。
    /// 返回列表为副本，避免调用方直接改动内部集合。
    /// </para>
    /// </summary>
    public List<CardChartKind> GetProviderCardChartKinds(string providerId)
    {
        lock (_ioLock)
        {
            if (_settings.ProviderCardChartKinds.TryGetValue(providerId, out var list) && list != null)
                return new List<CardChartKind>(list);

            // 迁移旧单选值（非 None 时包装为单元素列表）
            if (_settings.ProviderCardCharts.TryGetValue(providerId, out var single)
                && single != CardChartKind.None)
            {
                var migrated = new List<CardChartKind> { single };
                _settings.ProviderCardChartKinds[providerId] = migrated;
                return new List<CardChartKind>(migrated);
            }
            return new List<CardChartKind>();
        }
    }

    /// <summary>
    /// 设置指定 Provider 的「卡片图表类型集合」（多选）并持久化。
    /// 同步回写旧单选字段（取首元素，无则 None），避免两套配置漂移。
    /// </summary>
    public void SetProviderCardChartKinds(string providerId, IReadOnlyList<CardChartKind> kinds)
    {
        lock (_ioLock)
        {
            var list = kinds != null ? new List<CardChartKind>(kinds) : new List<CardChartKind>();
            _settings.ProviderCardChartKinds[providerId] = list;
            _settings.ProviderCardCharts[providerId] = list.Count > 0 ? list[0] : CardChartKind.None;
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
