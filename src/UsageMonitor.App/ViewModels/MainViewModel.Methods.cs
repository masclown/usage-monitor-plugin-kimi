using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.App.Controls;
using UsageMonitor.App.Helpers;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    public MainViewModel(PluginManager pluginManager, ConfigService configService, IRefreshService refreshService, UsageMonitor.Core.Modules.IDataModule? dataModule = null, UsageMonitor.Core.Services.Auth.AuthManager? authManager = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _refreshService = refreshService;
        _dataModule = dataModule ?? new UsageMonitor.Core.Services.Data.DataModule();
        _dataModule.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);

        // req-099 B1：创建显示模块（卡片装配 / 渲染 / 过滤 / 任务栏模式 / 图表顺序）。
        // 重新登录回调转发到 TriggerManualReLogin（内部经 HostApp 触发登录流程）。
        // S1：注入 AuthManager 供账号行 Sub 状态灯读取登录态。
        _displayModule = new UsageMonitor.App.Services.Display.DisplayModule(
            pluginManager, configService, refreshService, TriggerManualReLogin, authManager);
        // 已启用卡片集合变化时刷新空状态派生属性（IsEmpty / EmptyStateHint）与绑定。
        _displayModule.EnabledCardsChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EnabledUsages));
            OnPropertyChanged(nameof(IsEmpty));
            // req-110 P1-5：空态分层引导文案随卡片集合同步刷新
            OnPropertyChanged(nameof(EmptyStateHint));
        };

        // S1：账号增删改 → ConfigService.Save() → ConfigChanged → 增量重建卡片集合（复用既有事件链路，不新增事件）。
        // DisplayModule.SyncCardsWithAccounts 内部按账号结构签名比对，仅重建真正变化的 Provider；
        // ObservableCollection 绑定 UI，必须确保在 UI 线程执行。
        _configService.ConfigChanged += (_, _) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            if (dispatcher.CheckAccess())
            {
                _displayModule.SyncCardsWithAccounts();
                // 问题8：卡片重建后新 VM 字段缓存为空，回填持久化字段快照，避免数据概览等图表在下次刷新前长时间空白
                //（RestoreFromFieldSnapshot 内部会跳过已收到实时数据的 VM，重复调用安全）。
                _ = RestorePersistedFieldSnapshotsAsync();
            }
            else
            {
                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    _displayModule.SyncCardsWithAccounts();
                    _ = RestorePersistedFieldSnapshotsAsync();
                }));
            }
        };

                // req-072 U-18：RefreshCommand 执行时更新 LastRefreshTime / RefreshProgress / ErrorCount
                RefreshCommand = new AsyncRelayCommand(async () =>
                {
                    RefreshProgress = 0;
                    ErrorCount = 0;
                    try
                    {
                        await refreshService.RefreshAllAsync();
                        LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
                        RefreshProgress = 100;
                        // 统计错误数量
                        ErrorCount = EnabledUsages.Count(u => u.IsError);
                    }
                    catch
                    {
                        ErrorCount++;
                    }
                });
        SaveSettingsCommand = new RelayCommand(() => _configService.Save());

        // req-073：全局保存 / 取消命令。保存命令仅写盘并清除未保存标记；
        // 关闭窗口由 SettingsWindow 订阅 RequestCloseSettings 后执行（保存成功才关闭）。
        SaveAllSettingsCommand = new RelayCommand(() =>
        {
            // 问题8：全局保存前先提交色阶编辑器（用量色阶 + 热力图色阶）的未落盘修改，
            // 替代色阶设置页内分散的“保存设置”按钮（懒加载字段非 null 才提交，避免无谓初始化）。
            _tierEditor?.SaveTierCommand.Execute(null);
            _heatMapTierEditor?.SaveCommand.Execute(null);
            _configService.Save();
            if (string.IsNullOrEmpty(_configService.LastSaveError))
            {
                HasUnsavedChanges = false;
                RaiseRequestCloseSettings(saved: true);
            }
            // 保存失败：不触发关闭，由 SettingsWindow 检查 LastSaveError 弹错误提示。
        });
        CancelSettingsCommand = new RelayCommand(() =>
        {
            HasUnsavedChanges = false;
            RaiseRequestCloseSettings(saved: false);
        });


        // req-016：初始化主窗口 Logo + 订阅主题切换事件
        // req-032：单 logo 模式，加载一次即可（不再订阅 ThemeChanged 切换 logo）
        CurrentLogoSource = UsageMonitor.App.Helpers.LogoProvider.LoadLogo();

        // REQ-003：环形图 metric 顺序从设置同步到 ListBox 集合；提供上下移动 + 恢复默认三个命令
        // 使用 RelayCommand<int> 泛型版本（列表索引），CommunityToolkit.Mvvm 8.x 的非泛型 RelayCommand 仅接 Action。
        SyncRingChartMetricOrderFromConfig();
        MoveRingMetricUpCommand = new RelayCommand<string>(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            var idx = RingChartMetricOrder.IndexOf(key);
            if (idx <= 0 || idx >= RingChartMetricOrder.Count) return;
            (RingChartMetricOrder[idx - 1], RingChartMetricOrder[idx]) =
                (RingChartMetricOrder[idx], RingChartMetricOrder[idx - 1]);
            PersistRingChartMetricOrder();
        });
        MoveRingMetricDownCommand = new RelayCommand<string>(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            var idx = RingChartMetricOrder.IndexOf(key);
            if (idx < 0 || idx >= RingChartMetricOrder.Count - 1) return;
            (RingChartMetricOrder[idx + 1], RingChartMetricOrder[idx]) =
                (RingChartMetricOrder[idx], RingChartMetricOrder[idx + 1]);
            PersistRingChartMetricOrder();
        });
        ResetRingMetricOrderCommand = new RelayCommand(() =>
        {
            RingChartMetricOrder.Clear();
            foreach (var k in RingChartMetricKeys.DefaultOrder) RingChartMetricOrder.Add(k);
            PersistRingChartMetricOrder();
        });

        // REQ-004/006：触发区域默认重置命令 + 进入蒙版
        ResetTriggerAreaCommand = new RelayCommand(() =>
        {
            var def = ClampRect(RectInt.DefaultBottomRight());
            _configService.Settings.TrayTooltipTriggerRect = def;
            _configService.Save();
            OnPropertyChanged(nameof(TriggerRectX));
            OnPropertyChanged(nameof(TriggerRectY));
            OnPropertyChanged(nameof(TriggerRectWidth));
            OnPropertyChanged(nameof(TriggerRectHeight));
        });
        EditTriggerAreaCommand = new RelayCommand(() => OpenTriggerOverlayAction?.Invoke());

        // 订阅配置变更：当外部（其它入口直接改 Settings、TriggerAreaOverlayWindow 拖拽、程序其它点 Save）修改任意配置时，
        // 通知所有 Settings 派生属性刷新，让 TwoWay 绑定（TextBox、CheckBox 等）拿最新值。
        // 与 App.xaml.cs 中 _configService.ConfigChanged 订阅互不冲突（多订阅者并行接收）。
        _configService.ConfigChanged += OnConfigChangedRefreshSettings;

        // req-028：启动全局每秒 DispatcherTimer，刷新托盘/悬浮窗/卡片的重置倒计时 + 到时自动刷新。
        // 必须在 Usages / EnabledUsages / PluginItems 都装配后启动；后续调用连动。
        StartResetCountdownTimer();

        // 初始化插件列表与用量显示（req-099 B1：卡片装配 / 已启用过滤逻辑已抽离到 DisplayModule.Build）
        _displayModule.Build();

        // req-053：启动时同步全局已启用 metric 到所有 Provider，避免重启后显示所有 metric
        // 必须在 Usages 集合构建完成后调用
        SyncGlobalEnabledMetricsToAllProviders();

        // 问题1/9：启动时从 usage_field_versions 恢复各卡片的最近字段快照（fire-and-forget，
        // 首次刷新到达后 VM 会自行跳过晚到的快照）。
        _ = RestorePersistedFieldSnapshotsAsync();

        // 监听历史数据变化
        _dataModule.ProviderHistoryChanged += OnProviderHistoryChanged;
        _dataModule.HistoryChanged += OnAnyHistoryChanged;

        // 订阅全局用量色阶变更：档位 / 颜色改了之后，强制让所有进度条 XAML 绑定刷新
        // （PercentToBrushConverter 重新走 ResolveBrush → 返回新 Brush），同时重着色卡片热力图单元。
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnUsageTierChanged;

        // req-009：订阅热力图色阶变更（按 token 走 ResolveBrush 重算每个 Cell 的背景色）。
        UsageMonitor.App.Helpers.HeatMapTierScale.TierChanged += OnHeatMapTierChanged;
    }

    /// <summary>
    /// 获取当前生效的档位配置供设置页"用量色阶" Tab 回显使用。
    /// 缺省时返回出厂默认。
    /// </summary>
    public List<UsageMonitor.Core.Models.UsageTierConfig> GetCurrentTierConfigForEditor()
        => _configService.GetEffectiveUsageTierConfig();

    /// <summary>
    /// 把编辑结果仅推到全局色阶（预览，不写盘）。点保存按钮才会落盘。
    /// </summary>
    public void PreviewTierConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig> snapshot)
    {
        UsageMonitor.App.Helpers.UsageTierScale.ApplyConfig(snapshot);
    }

    /// <summary>
    /// 写入内存配置 + 落盘 + 推送全局色阶。
    /// </summary>
    public void SaveTierConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig> snapshot)
    {
        _configService.SetUsageTierConfig(snapshot);
        _configService.Save();
        // Save() 内部已会触发 ConfigChanged，App.OnStartup 里挂的 ApplyConfig 会重新拉一次。
    }

    // =====================================================================
    // req-011：热力图色阶设置项化 UI 相关回调（HeatMapTierListEditorViewModel 调用）
    // =====================================================================

    /// <summary>
    /// 返回已加载的插件列表（providerId → displayName），供设置页"热力图色阶" Tab 的 Provider 下拉框使用。
    /// <para>
    /// "通用默认"是固定的第一项（空字符串 key），表示编辑 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/> 兜底色阶。
    /// 但本期"通用默认"不允许保存（<see cref="SaveHeatMapTierConfig"/> 收到空 key 时直接 return）。
    /// </para>
    /// </summary>
    public System.Collections.Generic.IEnumerable<(string providerId, string displayName)> GetLoadedProviderOptions()
    {
        // req-011：按 plugin.DisplayName 升序排列，与设置页"插件" Tab 顺序一致
        return _pluginManager.Plugins
            .Select(p => (p.Provider.ProviderId, p.Provider.DisplayName))
            .OrderBy(x => x.Item2, System.StringComparer.CurrentCulture);
    }

    /// <summary>
    /// 拉取指定 Provider 的当前生效热力图色阶（<see cref="HeatMapTierConfig"/> 列表），供编辑器回显。
    /// <para>
    /// 优先级（与 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush"/> 一致）：
    /// <list type="number">
    ///   <item><description>providerId 为空 → <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/></description></item>
    ///   <item><description><c>ConfigService.Settings.ProviderHeatMapTiers[providerId]</c> 存在且非空 → 持久化的色阶</description></item>
    ///   <item><description>插件声明包 card.heatMapTiers 已注册 → 声明默认色阶</description></item>
    ///   <item><description>其他 → <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/> 4 档</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public System.Collections.Generic.List<UsageMonitor.Core.Models.HeatMapTierConfig> GetCurrentHeatMapTiersForEditor(string providerId)
    {
        var key = (providerId ?? string.Empty).Trim();
        // 通用默认：直接返回 GenericDefaults
        if (string.IsNullOrEmpty(key))
            return RuntimeTiersToConfigList(UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults);

        // 已持久化的色阶
        if (_configService.Settings.ProviderHeatMapTiers.TryGetValue(key, out var saved)
            && saved != null && saved.Count > 0)
            return saved.ToList();

        // 兜底：插件声明默认色阶 → 通用 4 档（Stage E：零 Provider 专名硬编码）
        var defaults = UsageMonitor.App.Helpers.HeatMapTierScale.GetDeclaredDefaults(key)
            ?? UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults;
        return RuntimeTiersToConfigList(defaults);
    }

    /// <summary>把运行时 <see cref="UsageMonitor.App.Helpers.HeatMapTier"/> 列表转 <see cref="HeatMapTierConfig"/>（用于编辑器回显）。</summary>
    private static System.Collections.Generic.List<UsageMonitor.Core.Models.HeatMapTierConfig> RuntimeTiersToConfigList(
        System.Collections.Generic.IEnumerable<UsageMonitor.App.Helpers.HeatMapTier> tiers)
    {
        var result = new System.Collections.Generic.List<UsageMonitor.Core.Models.HeatMapTierConfig>();
        foreach (var t in tiers)
        {
            result.Add(new UsageMonitor.Core.Models.HeatMapTierConfig
            {
                MinTokens = t.MinTokens,
                ColorHex = $"#{t.Color.R:X2}{t.Color.G:X2}{t.Color.B:X2}",
                IsEnabled = t.IsEnabled
            });
        }
        return result;
    }

    /// <summary>
    /// 把编辑结果仅推到全局 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale"/>（预览，不写盘）。
    /// <para>
    /// 为避免污染其他 Provider 的色阶，预览时合并"该 Provider 的预览值 + 其他 Provider 的当前持久化值"
    /// 一起推给 <c>ApplyConfig</c>。空 key 直接 return（通用默认不允许预览）。
    /// </para>
    /// </summary>
    public void PreviewHeatMapTierConfig(string providerId, IReadOnlyList<UsageMonitor.Core.Models.HeatMapTierConfig> snapshot)
    {
        var key = (providerId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key)) return; // 通用默认不允许保存/预览

        // 合并：当前持久化 + 该 Provider 的预览值
        var merged = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IList<UsageMonitor.Core.Models.HeatMapTierConfig>>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _configService.Settings.ProviderHeatMapTiers)
            merged[kv.Key] = kv.Value;
        merged[key] = snapshot.ToList();
        UsageMonitor.App.Helpers.HeatMapTierScale.ApplyConfig(merged);
    }

    /// <summary>
    /// 写入 <see cref="ConfigService"/> + 落盘 + 推送全局色阶。
    /// <para>
    /// 空 key 直接 return（通用默认不允许保存）。"__generic__" 也不允许（防止污染硬编码 GenericDefaults）。
    /// </para>
    /// </summary>
    public void SaveHeatMapTierConfig(string providerId, IReadOnlyList<UsageMonitor.Core.Models.HeatMapTierConfig> snapshot)
    {
        var key = (providerId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key) || key == "__generic__") return;

        _configService.Settings.ProviderHeatMapTiers[key] = snapshot.ToList();
        _configService.Save();
        // Save() 内部已会触发 ConfigChanged，App.OnStartup 里挂的 ApplyConfig 会重新拉一次。
    }

    /// <summary>
    /// 全局色阶变更回调：依次（1）让所有进度条的 Percent 属性发出 PropertyChanged，让 XAML 上的
    /// PercentToBrushConverter 重新解析；（2）重着色卡片热力图单元。
    /// </summary>
    private void OnUsageTierChanged(object? sender, EventArgs e)
    {
        ForceRefreshBars();
        RecolorAllHeatMaps();
    }

    /// <summary>
    /// req-009：热力图色阶变更回调，让所有卡片热力图按新色阶重算每个 Cell 的背景色。
    /// </summary>
    private void OnHeatMapTierChanged(object? sender, EventArgs e)
    {
        RecolorAllHeatMaps();
    }

    /// <summary>
    /// 对所有 ProviderUsageViewModel 的 4 个进度条 Percent 属性发出 PropertyChanged，
    /// 让 PercentToBrushConverter 重新取色。
    /// </summary>
    private void ForceRefreshBars()
    {
        foreach (var vm in Usages)
        {
            vm.RefreshAllPercentProperties();
        }
    }

    /// <summary>
    /// 重着色所有卡片热力图单元（用新色阶刷 Background）。
    /// 历史窗口的热力图由 HistoryViewModel 自行订阅事件负责重着色。
    /// </summary>
    private void RecolorAllHeatMaps()
    {
        foreach (var vm in Usages)
        {
            vm.RecolorHeatMapCells();
        }
    }

    /// <summary>
    /// 问题1/9：启动时从持久化仓库读取各卡片 (ProviderId, AccountId) 的最近字段快照，
    /// 回填到对应 <see cref="ProviderUsageViewModel"/>（数据概览 / 进度条 / 5h 倒计时等）。
    /// <para>同 (Provider, Account) 多卡片共享一次查询；构造函数在 UI 线程调用，await 后继续在 UI 线程回填，
    /// 无需额外 Dispatcher 切换；失败仅记日志，不影响启动。</para>
    /// </summary>
    private async Task RestorePersistedFieldSnapshotsAsync()
    {
        try
        {
            // 快照按 (ProviderId, AccountId) 维度缓存，避免同 Provider 多卡片重复查库。
            var cache = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vm in Usages.ToList())
            {
                if (vm == null || string.IsNullOrEmpty(vm.ProviderId)) continue;
                var key = $"{vm.ProviderId}::{vm.AccountIdSafe}";
                if (!cache.TryGetValue(key, out var fields))
                {
                    fields = await _dataModule.GetLatestFieldsAsync(vm.ProviderId, vm.AccountIdSafe);
                    cache[key] = fields ?? new Dictionary<string, object>();
                    fields = cache[key];
                }
                if (fields.Count == 0) continue;
                vm.RestoreFromFieldSnapshot(fields);
            }
        }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Error("MainViewModel", "RestorePersistedFieldSnapshotsAsync failed", ex);
        }
    }

    /// <summary>
    /// 当指定 Provider 的历史数据变化时刷新对应 VM
    /// </summary>
    private void OnProviderHistoryChanged(object? sender, string providerId)
    {
        var vm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (vm == null) return;
        vm.HistoryValues = _dataModule.GetHistoryValues(providerId);
    }

    /// <summary>
    /// 当 MaxPoints 等全局设置变化时刷新所有 VM
    /// </summary>
    private void OnAnyHistoryChanged(object? sender, EventArgs e)
    {
        foreach (var vm in Usages)
        {
            vm.HistoryValues = _dataModule.GetHistoryValues(vm.ProviderId);
        }
    }

    /// <summary>
    /// req-111：插件热重载后全量重建卡片 / 插件项集合（必须在 UI 线程调用）。
    /// <para>重建后同步全局 metric 启用状态，并回填持久化字段快照避免新卡片在下次刷新前长时间空白。</para>
    /// </summary>
    public void RebuildAllFromPlugins()
    {
        _displayModule.RebuildAll();
        SyncGlobalEnabledMetricsToAllProviders();
        _ = RestorePersistedFieldSnapshotsAsync();
    }

    /// <summary>
    /// 更新所有用量数据
    /// </summary>
    public void UpdateUsages(IReadOnlyList<UsageInfo> usages)
    {
        // req-099 B1：渲染路由已抽离到 DisplayModule。
        _displayModule.RenderCards(usages);
    }

    /// <summary>
    /// 更新插件启用状态：同步配置、插件管理器、用量VM，并刷新主窗口卡片集合。
    /// </summary>
    public void UpdatePluginEnabled(string providerId, bool isEnabled)
    {
        // req-099 B1：插件启用状态同步与卡片重建已抽离到 DisplayModule。
        _displayModule.SetPluginEnabled(providerId, isEnabled);
    }

    /// <summary>
    /// 修改 Provider 的任务栏显示模式（同步到 Usages + PluginItems + 配置）
    /// </summary>
    public void ChangeTaskbarMode(string providerId, TaskbarDisplayMode mode)
    {
        // req-099 B1：任务栏模式变更已抽离到 DisplayModule。
        _displayModule.ChangeTaskbarMode(providerId, mode);
    }

    /// <summary>
    /// 外部 ConfigService 变更时，统一通知所有 Settings 派生属性刷新。
    /// 保证触发区域调试矩形拖动 / 其它入口改配置后，SettingsWindow 中的 TextBox 双向绑定能拿到最新值。
    /// </summary>
    private void OnConfigChangedRefreshSettings(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RefreshInterval));
        OnPropertyChanged(nameof(ShowInTaskbar));
        OnPropertyChanged(nameof(ShowTrayTooltip));
        OnPropertyChanged(nameof(TrayTooltipHideDelayMs));
        OnPropertyChanged(nameof(TrayTriggerWidth));
        OnPropertyChanged(nameof(TrayTriggerHeight));
        OnPropertyChanged(nameof(RingChartWarningThreshold));
        OnPropertyChanged(nameof(RingChartDangerThreshold));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));

        // REQ-003：触发区域、sticky、动画同步
        OnPropertyChanged(nameof(RingChartStickySeconds));
        OnPropertyChanged(nameof(RingChartSwitchAnimationMs));
        SyncRingChartMetricOrderFromConfig();

        // REQ-004：触发区域 4 字段同步
        OnPropertyChanged(nameof(TriggerRectX));
        OnPropertyChanged(nameof(TriggerRectY));
        OnPropertyChanged(nameof(TriggerRectWidth));
        OnPropertyChanged(nameof(TriggerRectHeight));
    }

    /// <summary>REQ-003：把 ConfigService.Settings.RingChartMetricOrder 同步到 ListBox 绑定集合。</summary>
    private void SyncRingChartMetricOrderFromConfig()
    {
        var src = _configService.Settings.RingChartMetricOrder;
        if (src == null || src.Count == 0) return;
        // 简单同步：长度变化或顺序不同时整体重灌；否则保留当前 ListBox 选中状态
        if (RingChartMetricOrder.Count != src.Count)
        {
            RingChartMetricOrder.Clear();
            foreach (var k in src) RingChartMetricOrder.Add(k);
            return;
        }
        for (var i = 0; i < src.Count; i++)
        {
            if (!string.Equals(RingChartMetricOrder[i], src[i], StringComparison.OrdinalIgnoreCase))
            {
                RingChartMetricOrder.Clear();
                foreach (var k in src) RingChartMetricOrder.Add(k);
                return;
            }
        }
    }

    /// <summary>REQ-003：把当前 ListBox 顺序写回 ConfigService 并落盘。</summary>
    private void PersistRingChartMetricOrder()
    {
        _configService.Settings.RingChartMetricOrder = RingChartMetricOrder.ToList();
        _configService.Save();
    }

    /// <summary>
    /// req-091-005：手动触发某个 Provider 的重新登录（供卡片 ReLoginCommand 回调）。
    /// <para>
    /// 通过 <see cref="HostApp"/> 转发给 App.TriggerReLogin，复用现有 PluginConfigWindow 的 Cookie 获取流程。
    /// </para>
    /// </summary>
    public void TriggerManualReLogin(string providerId)
    {
        if (_hostAppRef == null)
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MainViewModel",
                $"[req-091] TriggerManualReLogin({providerId}) skipped: HostApp not set");
            return;
        }
        _hostAppRef.TriggerReLogin(providerId, isAutomatic: false);
    }

    /// <summary>req-053：把全局已启用 metric 集合同步到所有 ProviderUsageViewModel。</summary>
    public void SyncGlobalEnabledMetricsToAllProviders()
    {
        var globalEnabled = _configService.Settings.GlobalEnabledRingChartMetrics;
        foreach (var vm in Usages)
        {
            vm.EnabledRingChartMetrics = globalEnabled;
        }
    }

    /// <summary>
    /// req-016：主题切换事件处理。
    /// <para>
    /// req-032：单 logo 模式后不再需要切换 logo，但保留方法以便未来扩展其他主题切换逻辑。
    /// </para>
    /// </summary>
    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        // req-032：单 logo 不需要按主题重新加载，保持空实现
    }

    // =====================================================================
    // req-028：5h 刷新倒计时（托盘 + 悬浮窗 + 到时自动刷新）
    // =====================================================================

    /// <summary>
    /// req-028：启动全局每秒 <c>DispatcherTimer</c>，刷新所有 Provider 卡片的重置倒计时文本 + 到时自动刷新。
    /// <para>
    /// 调用方：MainViewModel 构造函数末尾（确保所有 <see cref="Usages"/> 已装配后再启动）。
    /// 期望与 <c>App.OnExit</c> 配对调 <see cref="StopResetCountdownTimer"/> 避免泄漏。
    /// </para>
    /// </summary>
    public void StartResetCountdownTimer()
    {
        if (_resetCountdownTimer != null) return; // 幂等
        _resetCountdownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _resetCountdownTimer.Tick += OnResetCountdownTick;
        _resetCountdownTimer.Start();
    }

    /// <summary>
    /// req-028：停止全局每秒 timer，App.OnExit 入口调用以避免 <c>DispatcherTimer</c> 引用泄漏。
    /// </summary>
    public void StopResetCountdownTimer()
    {
        if (_resetCountdownTimer == null) return;
        _resetCountdownTimer.Stop();
        _resetCountdownTimer.Tick -= OnResetCountdownTick;
        _resetCountdownTimer = null;
    }

    /// <summary>
    /// req-028：每秒 tick：遍历所有用量 VM 刷新 <see cref="ProviderUsageViewModel.FiveHourCountdownText"/> + 检查是否需要到时自动刷新。
    /// <para>每个 tick 都是 fire-and-forget；调用 <see cref="ProviderUsageViewModel.ShouldTriggerFiveHourAutoRefresh"/> 判断后调
    /// <see cref="RefreshService.RefreshProviderAsync"/>。为防重复触发，已经在 VM 上标记过的不会再被本 tick 选中，
    /// 直到新 <see cref="ProviderUsageViewModel.Next5hResetAt"/> 到来重置该标记。</para>
    /// </summary>
    private void OnResetCountdownTick(object? sender, EventArgs e)
    {
        if (Usages == null) return;
        foreach (var vm in Usages)
        {
            if (vm == null) continue;
            vm.RefreshFiveHourCountdownText(DateTime.Now);
            if (vm.ShouldTriggerFiveHourAutoRefresh())
            {
                vm.MarkFiveHourAutoRefreshTriggered();
                var providerId = vm.ProviderId;
                _ = TriggerProviderAutoRefreshAsync(providerId);
            }
        }
    }

    /// <summary>
    /// req-028：异步触发指定 Provider 的自动刷新（不带用户交互）。
    /// <para>用 <c>_ = </c> fire-and-forget 启动 <see cref="RefreshService.RefreshProviderAsync"/>，
    /// 失败仅写日志（不会弹出错误窗口打扰用户）。</para>
    /// </summary>
    private async Task TriggerProviderAutoRefreshAsync(string providerId)
    {
        try
        {
            UsageMonitor.Core.Services.FileLogger.Info("MainViewModel",
                $"5h 倒计时到 0，自动刷新 Provider={providerId}");
            await _refreshService.RefreshProviderAsync(providerId);
        }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Warn("MainViewModel",
                $"5h 自动刷新 Provider={providerId} 抛出异常（容许）", ex);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
