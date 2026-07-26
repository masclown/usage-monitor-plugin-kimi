using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.Core.Models;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
//   明确选择 WPF 版本，避免与 System.Drawing 同名类型冲突。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 单个用量档位的编辑 VM（设置页"用量色阶" Tab 行项）。
/// <para>
/// 负责：
/// <list type="bullet">
///   <item><description>双向绑定 <see cref="MinPercent"/>、<see cref="IsEnabled"/>、<see cref="ColorArgb"/>。</description></item>
///   <item><description>对外暴露 <see cref="Color"/>（WPF Brush）以便 XAML 预览色块。</description></item>
///   <item><description>提供 <see cref="PickColorCommand"/>：用 WinForms ColorDialog 选色，更新 ColorArgb。</description></item>
///   <item><description>提供 <see cref="RemoveCommand"/>：通知父 VM 从集合中移除。</description></item>
/// </list>
/// </para>
/// </summary>
public class UsageTierConfigViewModel : INotifyPropertyChanged
{
    private readonly UsageTierConfig _model;

    /// <summary>父 VM 引用，用于 <see cref="RemoveCommand"/> 通知移除本行。</summary>
    public TierListEditorViewModel? Parent { get; set; }

    /// <summary>构造时传入数据模型（同一引用；编辑直接落到 model 上）。</summary>
    public UsageTierConfigViewModel(UsageTierConfig model, TierListEditorViewModel? parent = null)
    {
        _model = model;
        Parent = parent;
        RemoveCommand = new RelayCommand(() => Parent?.RemoveTier(this));
        PickColorCommand = new RelayCommand(PickColor);
        PickScreenColorCommand = new RelayCommand(PickScreenColor);
    }

    /// <summary>下界（含，0-99）。</summary>
    public double MinPercent
    {
        get => _model.MinPercent;
        set
        {
            // 限幅到 [0, 99]：100 在语义上"取不到"，避免与"≥85"档重合造成歧义。
            var v = value;
            if (v < 0) v = 0;
            if (v > 99) v = 99;
            if (System.Math.Abs(_model.MinPercent - v) < 0.0001) return;
            _model.MinPercent = v;
            OnPropertyChanged();
        }
    }

    /// <summary>是否参与选色（禁用档仍占配置项位置，UI 上以低饱和灰显示）。</summary>
    public bool IsEnabled
    {
        get => _model.IsEnabled;
        set
        {
            if (_model.IsEnabled == value) return;
            _model.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>ARGB 32 位整数（0xAARRGGBB）。</summary>
    public uint ColorArgb
    {
        get => _model.ColorArgb;
        set
        {
            if (_model.ColorArgb == value) return;
            _model.ColorArgb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(ColorHex));
        }
    }

    /// <summary>WPF Brush（用于 XAML 色块绑定）。每次 ColorArgb 变更会触发 Brush 重建。</summary>
    public Brush Color
        => new SolidColorBrush(ColorFromArgb(_model.ColorArgb));

    /// <summary>HEX 文本（#AARRGGBB），便于用户直接看到当前值。</summary>
    public string ColorHex
        => $"#{_model.ColorArgb:X8}";

    /// <summary>取色按钮命令：弹出 WinForms ColorDialog，关闭后更新 ColorArgb。</summary>
    public IRelayCommand PickColorCommand { get; }

    /// <summary>修复6：屏幕取色器命令——全屏覆盖层点击取色，更新 ColorArgb。</summary>
    public IRelayCommand PickScreenColorCommand { get; }

    /// <summary>删除本行命令：通知父 VM 移除。</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>
    /// 弹出 WinForms ColorDialog 选色。ColorDialog 不支持透明通道，新选的 Alpha 强制为 0xFF。
    /// </summary>
    private void PickColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(
                (int)((_model.ColorArgb >> 24) & 0xFF),
                (int)((_model.ColorArgb >> 16) & 0xFF),
                (int)((_model.ColorArgb >> 8) & 0xFF),
                (int)(_model.ColorArgb & 0xFF))
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dlg.Color;
            ColorArgb = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        }
    }

    /// <summary>修复6：屏幕取色器——弹出全屏覆盖层，点击屏幕任意位置拾取像素颜色。</summary>
    private void PickScreenColor()
    {
        var c = UsageMonitor.App.Helpers.ScreenColorPicker.PickColor();
        if (c.HasValue)
        {
            ColorArgb = (0xFFu << 24) | ((uint)c.Value.R << 16) | ((uint)c.Value.G << 8) | c.Value.B;
        }
    }

    /// <summary>从 ARGB 还原 WPF Color（用完全限定名，避免与本类的 <see cref="Color"/> 属性重名歧义）。</summary>
    private static System.Windows.Media.Color ColorFromArgb(uint argb)
    {
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return System.Windows.Media.Color.FromArgb(a, r, g, b);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 用量色阶集合的编辑 VM（设置页"用量色阶" Tab 整体绑定上下文）。
/// <para>
/// 暴露 <see cref="TierItems"/> 集合 + 添加/恢复默认/应用预览/保存 等命令。
/// 与 <see cref="MainViewModel"/> 解耦：只通过构造函数注入的回调与父 VM 交互，
/// 便于在设置窗口独立测试。
/// </para>
/// </summary>
public class TierListEditorViewModel : INotifyPropertyChanged
{
    /// <summary>父 VM，用于"应用预览"时同步到全局色阶 /"保存"时落盘。</summary>
    private readonly MainViewModel _owner;

    /// <summary>设置项绑定集合（UI 双向绑定到这上面）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<UsageTierConfigViewModel> TierItems { get; }
        = new();

    /// <summary>"添加档位"按钮命令。</summary>
    public IRelayCommand AddTierCommand { get; }

    /// <summary>"恢复默认"按钮命令：清空并恢复出厂 4 档。</summary>
    public IRelayCommand ResetTierCommand { get; }

    /// <summary>"应用预览"命令：把当前 TierItems 推到全局 <c>UsageTierScale</c>，UI 即时刷新但不写盘。</summary>
    public IRelayCommand ApplyPreviewCommand { get; }

    /// <summary>"保存"命令：写入内存并落盘到 config.json。</summary>
    public IRelayCommand SaveTierCommand { get; }

    public TierListEditorViewModel(MainViewModel owner)
    {
        _owner = owner;
        AddTierCommand = new RelayCommand(AddTier);
        ResetTierCommand = new RelayCommand(ResetToDefaults);
        ApplyPreviewCommand = new RelayCommand(ApplyPreview);
        SaveTierCommand = new RelayCommand(SaveToConfig);

        // 从磁盘加载当前生效配置（含未持久化的临时编辑）
        ReloadFromConfig();
    }

    /// <summary>
    /// 从 <c>ConfigService</c> 拉取当前生效档位填充 <see cref="TierItems"/>。
    /// 设置窗口每次打开都会调一次，保证回显最新磁盘值。
    /// </summary>
    public void ReloadFromConfig()
    {
        TierItems.Clear();
        foreach (var t in _owner.GetCurrentTierConfigForEditor())
            TierItems.Add(new UsageTierConfigViewModel(t, this));
        OnPropertyChanged(nameof(TierItems));
    }

    /// <summary>添加一档：阈值取当前最大阈值 + 10（封顶 99），颜色给个中性灰。</summary>
    private void AddTier()
    {
        double nextMin = 50;
        foreach (var t in TierItems)
        {
            if (t.MinPercent >= nextMin) nextMin = t.MinPercent + 10;
        }
        if (nextMin > 99) nextMin = 99;
        var model = new UsageMonitor.Core.Models.UsageTierConfig
        {
            MinPercent = nextMin,
            ColorArgb = 0xFF808080, // 中性灰（用户后续选色）
            IsEnabled = true,
        };
        TierItems.Add(new UsageTierConfigViewModel(model, this));
        OnPropertyChanged(nameof(TierItems));
    }

    /// <summary>从集合移除指定档。</summary>
    public void RemoveTier(UsageTierConfigViewModel item)
    {
        if (item == null) return;
        TierItems.Remove(item);
        OnPropertyChanged(nameof(TierItems));
    }

    /// <summary>恢复为出厂默认 4 档（仅 UI 集合，未自动保存/应用）。</summary>
    private void ResetToDefaults()
    {
        TierItems.Clear();
        foreach (var t in UsageMonitor.Core.Models.UsageTierConfig.Defaults())
            TierItems.Add(new UsageTierConfigViewModel(t, this));
        OnPropertyChanged(nameof(TierItems));
    }

    /// <summary>把当前 TierItems 推到全局 UsageTierScale，UI 立即按新色阶刷新（不写盘）。</summary>
    private void ApplyPreview()
    {
        var snapshot = TierItems.Select(t => new UsageMonitor.Core.Models.UsageTierConfig
        {
            MinPercent = t.MinPercent,
            ColorArgb = t.ColorArgb,
            IsEnabled = t.IsEnabled,
        }).ToList();
        _owner.PreviewTierConfig(snapshot);
    }

    /// <summary>把当前 TierItems 写入内存配置 + 落盘 + 推送全局色阶。</summary>
    private void SaveToConfig()
    {
        var snapshot = TierItems.Select(t => new UsageMonitor.Core.Models.UsageTierConfig
        {
            MinPercent = t.MinPercent,
            ColorArgb = t.ColorArgb,
            IsEnabled = t.IsEnabled,
        }).ToList();
        _owner.SaveTierConfig(snapshot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}