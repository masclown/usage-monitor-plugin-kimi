using System.ComponentModel;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-086：插件设置基类，提供 INotifyPropertyChanged 支持和默认值处理。
/// <para>
/// 插件可继承此基类获得强类型设置支持，同时保持与现有 <see cref="ProviderConfig"/> 的兼容。
/// 子类需实现 <see cref="LoadFromConfig"/> 和 <see cref="SaveToConfig"/> 完成双向映射。
/// </para>
/// </summary>
public abstract class PluginSettingsBase : IPluginSettings
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 触发属性变更通知。
    /// </summary>
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 设置属性值并触发变更通知。
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <inheritdoc />
    public virtual bool Validate(out string? error)
    {
        error = null;
        return true;
    }

    /// <inheritdoc />
    public abstract void LoadFromConfig(ProviderConfig config);

    /// <inheritdoc />
    public abstract void SaveToConfig(ProviderConfig config);

    /// <inheritdoc />
    public virtual void ResetToDefaults()
    {
        // 默认实现：子类可覆盖以提供自定义重置逻辑
    }

    /// <summary>
    /// 从配置读取字符串值，支持默认值。
    /// </summary>
    protected string GetString(ProviderConfig config, string key, string defaultValue = "")
    {
        return config.GetValue(key) ?? defaultValue;
    }

    /// <summary>
    /// 从配置读取布尔值，支持默认值。
    /// </summary>
    protected bool GetBool(ProviderConfig config, string key, bool defaultValue = false)
    {
        var value = config.GetValue(key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从配置读取整数值，支持默认值。
    /// </summary>
    protected int GetInt(ProviderConfig config, string key, int defaultValue = 0)
    {
        var value = config.GetValue(key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// 从配置读取双精度浮点值，支持默认值。
    /// </summary>
    protected double GetDouble(ProviderConfig config, string key, double defaultValue = 0)
    {
        var value = config.GetValue(key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return double.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// 写入字符串值到配置。
    /// </summary>
    protected void SetString(ProviderConfig config, string key, string? value)
    {
        if (value == null)
        {
            config.RemoveValue(key);
        }
        else
        {
            config.SetValue(key, value);
        }
    }

    /// <summary>
    /// 写入布尔值到配置。
    /// </summary>
    protected void SetBool(ProviderConfig config, string key, bool value)
    {
        config.SetValue(key, value ? "true" : "false");
    }

    /// <summary>
    /// 写入整数值到配置。
    /// </summary>
    protected void SetInt(ProviderConfig config, string key, int value)
    {
        config.SetValue(key, value.ToString());
    }

    /// <summary>
    /// 写入双精度浮点值到配置。
    /// </summary>
    protected void SetDouble(ProviderConfig config, string key, double value)
    {
        config.SetValue(key, value.ToString("G"));
    }
}
