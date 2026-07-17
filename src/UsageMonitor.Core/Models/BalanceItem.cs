using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 余额快照中的单个数据项（req-008）。
/// <para>
/// 由 <c>UpdateBalanceFromExtra</c> 组装默认 4 项（累计 / 峰值 / 活跃 / 积分余额），
/// 插件可通过 <see cref="IUsageProvider.BalanceItems"/> 覆盖、追加或隐藏默认项。
/// 每项在主窗口卡片中按"标签 / 主数字 / 辅助行"三行结构展示，列与列之间用 1px 实线分隔。
/// </para>
/// <para>
/// 主数字的颜色由 XAML 端根据 <see cref="Label"/> 在 DataTemplate 中绑定到主题资源（不
/// 在模型中持有 WPF Brush，遵守 Core 项目"数据契约不依赖 UI 类型"的设计原则）。
/// </para>
/// </summary>
public sealed class BalanceItem : INotifyPropertyChanged
{
    private string _label = string.Empty;
    private string _value = "--";
    private string? _detail;
    private bool _isVisible = true;
    private bool _isLast;

    /// <summary>标签（12px 次级字色），如"累计"/"峰值"/"活跃"/"积分余额"。
    /// 同时作为主数字颜色的键，XAML 端按 Label 在 DataTemplate 中映射到主题资源（AccentBrush/WarningBrush 等）。</summary>
    public string Label
    {
        get => _label;
        set { if (_label == value) return; _label = value; OnPropertyChanged(); }
    }

    /// <summary>主数字（26px Bold），如"4.35B"/"552.49M"/"5/30天"/"暂无积分"。</summary>
    public string Value
    {
        get => _value;
        set { if (_value == value) return; _value = value; OnPropertyChanged(); }
    }

    /// <summary>辅助行（10px 三级字色，可空），如"2026-07-01"或"续期至 2026-08-01"。</summary>
    public string? Detail
    {
        get => _detail;
        set { if (_detail == value) return; _detail = value; OnPropertyChanged(); }
    }

    /// <summary>是否在主窗口卡片中显示；false 时整列折叠。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); }
    }

    /// <summary>是否为集合末项（由 <c>ProviderUsageViewModel</c> 在组装时设置）。
    /// XAML 端用此属性隐藏末项右侧的 1px 竖向分隔线，避免最右一列后还多一条线。</summary>
    public bool IsLast
    {
        get => _isLast;
        set { if (_isLast == value) return; _isLast = value; OnPropertyChanged(); }
    }

    /// <summary>INPC：值变更通知（供 XAML 数据绑定与 ItemsControl 刷新）。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发 PropertyChanged 事件（带 [CallerMemberName] 自动取属性名）。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
