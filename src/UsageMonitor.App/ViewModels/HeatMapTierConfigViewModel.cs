using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.Core.Models;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
//   明确选择 WPF 版本，避免与 System.Drawing / System.Windows.Forms 同名类型冲突。
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 单个热力图色阶档位的编辑 VM（设置页"热力图色阶" Tab 行项，req-011）。
/// <para>
/// 负责：
/// <list type="bullet">
///   <item><description>双向绑定 <see cref="MinTokens"/>、<see cref="IsEnabled"/>、<see cref="ColorHex"/>。</description></item>
///   <item><description>对外暴露 <see cref="Color"/>（WPF Brush）以便 XAML 预览色块。</description></item>
///   <item><description>提供 <see cref="PickColorCommand"/>：用 WinForms ColorDialog 选色，更新 ColorHex。</description></item>
///   <item><description>提供 <see cref="RemoveCommand"/>：通知父 VM 从集合中移除。</description></item>
/// </list>
/// </para>
/// <para>
/// 与 <see cref="UsageTierConfigViewModel"/> 几乎一样，只是数据模型是
/// <see cref="HeatMapTierConfig"/>（按 token 绝对值分档而非百分比），颜色用 hex 字符串而非 ARGB uint。
/// </para>
/// </summary>
public class HeatMapTierConfigViewModel : INotifyPropertyChanged
{
    private readonly HeatMapTierConfig _model;

    /// <summary>父 VM 引用，用于 <see cref="RemoveCommand"/> 通知移除本行。</summary>
    public HeatMapTierListEditorViewModel? Parent { get; set; }

    /// <summary>构造时传入数据模型（同一引用；编辑直接落到 model 上）。</summary>
    public HeatMapTierConfigViewModel(HeatMapTierConfig model, HeatMapTierListEditorViewModel? parent = null)
    {
        _model = model;
        Parent = parent;
        RemoveCommand = new RelayCommand(() => Parent?.RemoveTier(this));
        PickColorCommand = new RelayCommand(PickColor);
        PickScreenColorCommand = new RelayCommand(PickScreenColor);
    }

    /// <summary>下界（含，单位 tokens）。负数会被限幅到 0。</summary>
    public long MinTokens
    {
        get => _model.MinTokens;
        set
        {
            var v = value < 0 ? 0 : value;
            if (_model.MinTokens == v) return;
            _model.MinTokens = v;
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

    /// <summary>HEX 颜色字符串（带 `#` 前缀，如 <c>#ffa595</c>）。UI 双向绑定 + 拾色器写入。</summary>
    public string ColorHex
    {
        get => _model.ColorHex;
        set
        {
            // 规范化：去空白 / 自动补 `#` 前缀（用户手输可能不带）
            var v = (value ?? string.Empty).Trim();
            if (v.Length > 0 && v[0] != '#') v = "#" + v;
            if (string.Equals(_model.ColorHex, v, System.StringComparison.OrdinalIgnoreCase)) return;
            // 即使解析失败也保留原值（不写非法值）
            if (v.Length == 7 || v.Length == 9)
            {
                _model.ColorHex = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Color));
            }
        }
    }

    /// <summary>WPF Brush（用于 XAML 色块绑定）。每次 ColorHex 变更会触发 Brush 重建。</summary>
    public Brush Color
    {
        get
        {
            try
            {
                var c = UsageMonitor.App.Helpers.ColorStringHelper.Parse(_model.ColorHex);
                var b = new SolidColorBrush(c);
                if (b.CanFreeze) b.Freeze();
                return b;
            }
            catch
            {
                return Brushes.Gray;
            }
        }
    }

    /// <summary>取色按钮命令：弹出 WinForms ColorDialog，关闭后更新 ColorHex。</summary>
    public IRelayCommand PickColorCommand { get; }

    /// <summary>修复6：屏幕取色器命令——全屏覆盖层点击取色，更新 ColorHex。</summary>
    public IRelayCommand PickScreenColorCommand { get; }

    /// <summary>删除本行命令：通知父 VM 移除。</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>
    /// 弹出 WinForms ColorDialog 选色，关闭后把 ARGB 转成 hex 字符串写入 ColorHex。
    /// </summary>
    private void PickColor()
    {
        var current = UsageMonitor.App.Helpers.ColorStringHelper.Parse(_model.ColorHex);
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B)
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dlg.Color;
            ColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    /// <summary>修复6：屏幕取色器——弹出全屏覆盖层，点击屏幕任意位置拾取像素颜色。</summary>
    private void PickScreenColor()
    {
        var c = UsageMonitor.App.Helpers.ScreenColorPicker.PickColor();
        if (c.HasValue)
        {
            ColorHex = $"#{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 热力图色阶集合的编辑 VM（设置页“热力图色阶” Tab 整体绑定上下文，req-011）。
/// <para>
/// 暴露 <see cref="TierItems"/> 集合 + <see cref="ProviderOptions"/> 下拉 + 添加/恢复默认/应用预览/保存 等命令。
/// 与 <see cref="MainViewModel"/> 解耦：只通过构造函数注入的回调与父 VM 交互，便于在设置窗口独立测试。
/// </para>
/// </summary>
public class HeatMapTierListEditorViewModel : INotifyPropertyChanged
{
    /// <summary>父 VM，用于"应用预览"时同步到全局色阶 /"保存"时落盘。</summary>
    private readonly MainViewModel _owner;

    /// <summary>Provider 下拉框可选项（key = providerId 或 "" 表示"通用默认"）。</summary>
    public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string, string>> ProviderOptions { get; }

    /// <summary>设置项绑定集合（UI 双向绑定到这上面）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<HeatMapTierConfigViewModel> TierItems { get; } = new();

    // Stage E：默认选中“通用默认”，构造时若有已加载插件则切到首个（不硬编码具体 Provider）。
    private string _selectedProviderId = "";
    /// <summary>当前编辑的 ProviderId（"通用默认"为空字符串）。</summary>
    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set
        {
            var v = value ?? string.Empty;
            if (_selectedProviderId == v) return;
            _selectedProviderId = v;
            OnPropertyChanged();
            ReloadFromConfig();
        }
    }

    /// <summary>"添加档位"按钮命令。</summary>
    public IRelayCommand AddTierCommand { get; }

    /// <summary>"恢复默认"按钮命令：清空并恢复该 Provider 的默认色阶。</summary>
    public IRelayCommand ResetToDefaultsCommand { get; }

    /// <summary>"应用预览"命令：把当前 TierItems 推到全局 <c>HeatMapTierScale</c>，UI 即时刷新但不写盘。</summary>
    public IRelayCommand ApplyPreviewCommand { get; }

    /// <summary>"保存"命令：写入 <c>ConfigService.Settings.ProviderHeatMapTiers</c> 并落盘到 config.json。</summary>
    public IRelayCommand SaveCommand { get; }

    public HeatMapTierListEditorViewModel(MainViewModel owner)
    {
        _owner = owner;
        AddTierCommand = new RelayCommand(AddTier);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
        ApplyPreviewCommand = new RelayCommand(ApplyPreview);
        SaveCommand = new RelayCommand(Save);

        // 构造 ProviderOptions：已加载插件 + "通用默认"
        var opts = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
        {
            new("", "通用默认（4 档 K~M 级）"),
        };
        // 反射 PluginManager.Plugins 在 MainViewModel 构造时已初始化；通过 _owner 暴露
        foreach (var (pid, pname) in _owner.GetLoadedProviderOptions())
            opts.Add(new(pid, pname));
        ProviderOptions = opts;

        // 默认选中首个已加载插件（无插件时保持"通用默认"），避免 Provider 专名硬编码。
        if (opts.Count > 1) _selectedProviderId = opts[1].Key;

        // 从磁盘加载当前生效配置（含未持久化的临时编辑）
        ReloadFromConfig();
    }

    /// <summary>
    /// 从 <c>MainViewModel.GetCurrentHeatMapTiersForEditor</c> 拉取当前选 Provider 的色阶填充 <see cref="TierItems"/>。
    /// 切换 Provider 或设置窗口每次打开都会调一次，保证回显最新磁盘值。
    /// </summary>
    public void ReloadFromConfig()
    {
        TierItems.Clear();
        foreach (var t in _owner.GetCurrentHeatMapTiersForEditor(_selectedProviderId))
            TierItems.Add(new HeatMapTierConfigViewModel(t, this));
        OnPropertyChanged(nameof(TierItems));
    }

    /// <summary>从集合移除指定档。</summary>
    public void RemoveTier(HeatMapTierConfigViewModel item)
    {
        if (item == null) return;
        TierItems.Remove(item);
    }

    /// <summary>添加一档：阈值取当前最大阈值 + 1M（封顶 long.MaxValue），颜色给个中性灰。</summary>
    private void AddTier()
    {
        long nextMin = 0;
        foreach (var t in TierItems)
            if (t.MinTokens >= nextMin) nextMin = t.MinTokens + 1_000_000;
        if (nextMin < 0) nextMin = 0; // 防御性：long 溢出
        var model = new HeatMapTierConfig
        {
            MinTokens = nextMin,
            ColorHex = "#f3f4f6",
            IsEnabled = true
        };
        TierItems.Add(new HeatMapTierConfigViewModel(model, this));
    }

    /// <summary>恢复为该 Provider 的默认色阶（仅 UI 集合，未自动保存/应用）。</summary>
    private void ResetToDefaults()
    {
        TierItems.Clear();
        foreach (var t in _owner.GetCurrentHeatMapTiersForEditor(_selectedProviderId))
            TierItems.Add(new HeatMapTierConfigViewModel(t, this));
        OnPropertyChanged(nameof(TierItems));
    }

    /// <summary>把当前 TierItems 序列化为 HeatMapTierConfig 列表快照（用于预览 / 保存）。</summary>
    private System.Collections.Generic.List<HeatMapTierConfig> Snapshot()
        => TierItems.Select(t => new HeatMapTierConfig
        {
            MinTokens = t.MinTokens,
            ColorHex = t.ColorHex,
            IsEnabled = t.IsEnabled
        }).ToList();

    /// <summary>把当前 TierItems 推到全局 HeatMapTierScale，UI 立即按新色阶刷新（不写盘）。</summary>
    private void ApplyPreview()
    {
        _owner.PreviewHeatMapTierConfig(_selectedProviderId, Snapshot());
    }

    /// <summary>把当前 TierItems 写入 ConfigService 并落盘到 config.json。</summary>
    private void Save()
    {
        _owner.SaveHeatMapTierConfig(_selectedProviderId, Snapshot());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
