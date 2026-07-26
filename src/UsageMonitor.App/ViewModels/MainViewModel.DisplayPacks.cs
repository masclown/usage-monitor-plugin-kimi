using System;
using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services.Display;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// req-115：显示资源包下拉选项 DTO（Id 为空串 = 内置默认）。
/// </summary>
public sealed record DisplayPackOption(string Id, string DisplayName);

/// <summary>
/// MainViewModel 显示资源包分部（req-115）：主题 / 图表样式包 / mini 图表样式包 / 悬浮窗模板包
/// 的下拉选项与选中项持久化，供设置窗口绑定。
/// </summary>
public partial class MainViewModel
{
    /// <summary>req-115：显示资源包注册表（经宿主 App 获取；未注入时为 null，下拉仅剩内置项）。</summary>
    private DisplayPackRegistry? PackRegistry => _hostAppRef?.DisplayPacks;

    // ==================== 主题 ====================

    /// <summary>req-115：可选主题列表（内置 dark/light + 外部主题包，来自 ThemeModule 注册顺序）。</summary>
    public IReadOnlyList<DisplayPackOption> AvailableThemeOptions =>
        UsageMonitor.App.Services.Theme.ThemeModule.Default.AvailableThemes
            .Select(t => new DisplayPackOption(t.Id, t.DisplayName))
            .ToList();

    /// <summary>
    /// req-115：当前选中主题 Id（ThemeId 缺省时映射旧 Theme 枚举）。
    /// 设置时持久化 ThemeId + 同步 Theme 枚举（向后兼容托盘图标等明暗消费方）并即时换肤。
    /// </summary>
    public string SelectedThemeId
    {
        get
        {
            var s = _configService.Settings;
            return string.IsNullOrWhiteSpace(s.ThemeId)
                ? (s.Theme == ThemeMode.Light ? "light" : "dark")
                : s.ThemeId;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (string.Equals(SelectedThemeId, value, StringComparison.OrdinalIgnoreCase)) return;

            var descriptor = UsageMonitor.App.Services.Theme.ThemeModule.Default.AvailableThemes
                .FirstOrDefault(t => string.Equals(t.Id, value, StringComparison.OrdinalIgnoreCase));
            _configService.Settings.ThemeId = value;
            if (descriptor != null)
                _configService.Settings.Theme = descriptor.IsDark ? ThemeMode.Dark : ThemeMode.Light;
            _configService.Save();
            UsageMonitor.App.Helpers.ThemeManager.ApplyById(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeMode));
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
        }
    }

    // ==================== 图表 / mini 图表样式包 ====================

    /// <summary>req-115：可选图表样式包（首项为内置默认）。</summary>
    public IReadOnlyList<DisplayPackOption> AvailableChartStylePacks
        => BuildPackOptions(PackRegistry?.ChartStylePacks);

    /// <summary>req-115：当前选中的图表样式包 Id（空 = 内置）。保存后经 ConfigChanged 触发全局色阶重应用。</summary>
    public string SelectedChartStylePackId
    {
        get => _configService.Settings.ChartStylePackId;
        set
        {
            var v = value ?? "";
            if (string.Equals(_configService.Settings.ChartStylePackId, v, StringComparison.OrdinalIgnoreCase)) return;
            _configService.Settings.ChartStylePackId = v;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>req-115：可选 mini 图表样式包（首项为内置默认）。</summary>
    public IReadOnlyList<DisplayPackOption> AvailableMiniChartStylePacks
        => BuildPackOptions(PackRegistry?.MiniChartStylePacks);

    /// <summary>req-115：当前选中的 mini 图表样式包 Id（空 = 内置）。保存后由宿主重建 mini 注册表与任务栏窗口。</summary>
    public string SelectedMiniChartStylePackId
    {
        get => _configService.Settings.MiniChartStylePackId;
        set
        {
            var v = value ?? "";
            if (string.Equals(_configService.Settings.MiniChartStylePackId, v, StringComparison.OrdinalIgnoreCase)) return;
            _configService.Settings.MiniChartStylePackId = v;
            _configService.Save();
            _hostAppRef?.ReapplyMiniChartStyles();
            OnPropertyChanged();
        }
    }

    // ==================== 悬浮窗模板包 ====================

    /// <summary>req-115：可选悬浮窗模板包（首项为内置默认布局）。</summary>
    public IReadOnlyList<DisplayPackOption> AvailableTrayTooltipPacks
        => BuildPackOptions(PackRegistry?.TrayTooltipPacks);

    /// <summary>req-115：当前选中的悬浮窗模板包 Id（空 = 内置布局）。切换后通知全部卡片 VM 重算模板行。</summary>
    public string SelectedTrayTooltipPackId
    {
        get => _configService.Settings.TrayTooltipPackId;
        set
        {
            var v = value ?? "";
            if (string.Equals(_configService.Settings.TrayTooltipPackId, v, StringComparison.OrdinalIgnoreCase)) return;
            _configService.Settings.TrayTooltipPackId = v;
            _configService.Save();
            NotifyTrayTooltipPackToCards();
            OnPropertyChanged();
        }
    }

    // ==================== 通知与工具 ====================

    /// <summary>req-116：可选界面语言（影响插件文案与语言包选择；App 自身硬编码文案不在范围）。</summary>
    public IReadOnlyList<DisplayPackOption> LanguageOptions { get; } = new List<DisplayPackOption>
    {
        new("zh-CN", "简体中文"),
        new("en-US", "English")
    };

    /// <summary>
    /// req-116：当前界面语言。切换时持久化 + I18n.SetLanguage + 触发插件重载管线
    /// （manifest 里的 i18n: 键在重载时按新语言重新解析）。
    /// </summary>
    public string SelectedLanguage
    {
        get => string.IsNullOrWhiteSpace(_configService.Settings.Language) ? "zh-CN" : _configService.Settings.Language;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (string.Equals(SelectedLanguage, value, StringComparison.OrdinalIgnoreCase)) return;
            _configService.Settings.Language = value;
            _configService.Save();
            UsageMonitor.Core.Services.I18n.SetLanguage(value);
            _hostAppRef?.ReloadPluginsAndRebuild();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// req-115：显示资源包热重载后由宿主调用——刷新四个下拉的选项与选中项绑定，并让卡片 VM 重算悬浮窗模板行。
    /// </summary>
    public void NotifyDisplayPacksChanged()
    {
        OnPropertyChanged(nameof(AvailableThemeOptions));
        OnPropertyChanged(nameof(SelectedThemeId));
        OnPropertyChanged(nameof(AvailableChartStylePacks));
        OnPropertyChanged(nameof(SelectedChartStylePackId));
        OnPropertyChanged(nameof(AvailableMiniChartStylePacks));
        OnPropertyChanged(nameof(SelectedMiniChartStylePackId));
        OnPropertyChanged(nameof(AvailableTrayTooltipPacks));
        OnPropertyChanged(nameof(SelectedTrayTooltipPackId));
        NotifyTrayTooltipPackToCards();
    }

    /// <summary>req-115：让全部卡片 VM 重算悬浮窗模板行（模板包切换 / 热重载后调用）。</summary>
    private void NotifyTrayTooltipPackToCards()
    {
        foreach (var vm in Usages)
            vm.NotifyTrayTooltipPackChanged();
    }

    /// <summary>构建"内置默认 + 已装包"下拉选项列表。</summary>
    /// <param name="packs">已加载包集合（可为 null）。</param>
    private static List<DisplayPackOption> BuildPackOptions<T>(IReadOnlyList<T>? packs) where T : DisplayPackBase
    {
        var options = new List<DisplayPackOption> { new("", "内置默认") };
        if (packs != null)
            options.AddRange(packs.Select(p => new DisplayPackOption(p.Id ?? "", p.EffectiveDisplayName)));
        return options;
    }
}
