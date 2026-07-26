using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using UsageMonitor.App.Helpers;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.MiniChart;
using Brush = System.Windows.Media.Brush;

namespace UsageMonitor.App.Views;

/// <summary>
/// req-088 B5：Taskbar 迷你图列表项的轻量包装 ViewModel。
/// <para>
/// 把 Core 层 <see cref="MiniChartDescriptor"/>（只读 DTO）与 App 层 UI 状态绑定起来：
/// <list type="bullet">
///   <item><description>descriptor：注册中心传入的描述符（Kind/Style/ColorTier/DataSource/Tooltip）。</description></item>
///   <item><description>UsageVm：关联的 <see cref="ViewModels.ProviderUsageViewModel"/>（用于订阅数据变化、刷新通知）。</description></item>
///   <item><description>DataSource 派生属性：把 <c>object?</c> 解包为 <c>double?</c>（百分比）便于 XAML 绑定。</description></item>
///   <item><description>TooltipText/TooltipTitle：把 <see cref="MiniChartTooltip"/> 模板渲染成具体字符串（req-088 B9）。</description></item>
/// </list>
/// </para>
/// <para>所有派生属性（UsagePercentage/TooltipText 等）通过 INPC 通知 XAML 刷新。
/// 注意：本类不做线程同步——仅在 UI 线程调用 <see cref="RefreshFromUsageVm"/>。</para>
/// </summary>
public class MiniChartItemViewModel : INotifyPropertyChanged
{
    /// <summary>底层描述符（注册中心传入，运行时只读）。</summary>
    public MiniChartDescriptor Descriptor { get; }

    /// <summary>关联的 ProviderUsageViewModel（用于同步实时数据；null 时显示空状态）。</summary>
    public ViewModels.ProviderUsageViewModel? UsageVm { get; }

    /// <summary>
    /// 问题8：本 mini 图表的有效 Tooltip/文本显示字段（用户配置优先，回退声明；由 TaskbarWindow 构建时解析注入）。
    /// <para>null = 无字段配置（沿用旧渲染路径）；空集合 = 不显示 tooltip；非空 = 按字段目录顺序渲染行/片段。</para>
    /// </summary>
    public IReadOnlyList<string>? EffectiveTooltipFields { get; set; }

    /// <summary>问题8：账号显示名（昵称优先，回退账号 ID；由 TaskbarWindow 构建时解析注入）。</summary>
    public string? AccountName { get; set; }

    /// <summary>问题11/12：用户勾选的可见数据组 ID 列表（含顺序；可含虚拟倒计时组 ID）。
    /// <para>null = 未配置（全部声明组可见）；由 TaskbarWindow 构建时从 AccountCustomization.VisibleMiniDataGroups 解析注入。</para></summary>
    public IReadOnlyList<string>? VisibleDataGroupIds { get; set; }

    /// <summary>问题13：是否显示 Provider Logo（来自用户设置/描述符）。</summary>
    public bool ShowLogo => Descriptor.ShowLogo;

    /// <summary>问题13：Provider Logo 路径（优先关联卡片 VM 的图标，回退按 ProviderId 解析）。</summary>
    public string? IconPath => UsageVm?.IconPath ?? ViewModels.ProviderUsageViewModel.ResolveIconPath(ProviderId);

    /// <summary>问题11：有效数据组列表——按用户勾选（VisibleDataGroupIds）过滤并排序声明组；未配置时为全部声明组。
    /// <para>滚轮循环、UsagePercent、tooltip 均基于本列表，保证取消勾选的数据组不再参与展示/切换。</para></summary>
    public IReadOnlyList<DataGroup> EffectiveDataGroups
    {
        get
        {
            var declared = Descriptor.DataGroups;
            if (declared is not { Count: > 0 }) return System.Array.Empty<DataGroup>();
            var visible = VisibleDataGroupIds;
            if (visible == null) return declared;
            // 按用户列表顺序过滤（虚拟 ID 如倒计时组不在声明中，自然跳过）
            var result = new List<DataGroup>();
            foreach (var id in visible)
            {
                var g = declared.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                if (g != null) result.Add(g);
            }
            return result;
        }
    }

    /// <summary>问题12：文本迷你图是否展示刷新倒计时段（由虚拟倒计时数据组勾选控制；未配置时不显示）。</summary>
    public bool ShowCountdownInText
        => VisibleDataGroupIds != null
           && VisibleDataGroupIds.Contains(MiniTooltipFieldCatalog.RefreshCountdownVirtual, StringComparer.OrdinalIgnoreCase);

    /// <summary>问题11：宿主注入 VisibleDataGroupIds 后重新解析初始数据组索引（构造时尚未注入，需二次对齐）。</summary>
    public void ReinitializeDataGroupIndex() => _currentDataGroupIndex = ResolveInitialDataGroupIndex();

    /// <summary>唯一 ProviderId（来自 Descriptor，转发给 XAML 便于调试）。</summary>
    public string ProviderId => Descriptor.ProviderId;

    /// <summary>图类型枚举（用于 DataTemplateSelector 选择模板）。</summary>
    public MiniChartKind Kind => Descriptor.Kind;

    /// <summary>视觉样式枚举。</summary>
    public MiniChartStyle Style => Descriptor.Style;

    /// <summary>
    /// 派生：当前用量百分比（0-100）。
    /// <para>req-107 B4：有数据组声明时按当前组的 Value 字段经 <see cref="ResolveMiniFieldValue"/> 解析；
    /// 无数据组时回退原路径（UsageVm.UsagePercentage / descriptor.DataSource）。</para>
    /// <para>MiniRingChart / MiniText 必须；其它类型（MiniLineChart / MiniBarChart / MiniHeatMap）忽略此值。</para>
    /// </summary>
    public double? UsagePercent
    {
        get
        {
            // req-107 B4：数据组路径——取当前组 Value 角色字段解析值
            var group = CurrentDataGroup;
            if (group != null)
            {
                var valueField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
                if (valueField != null)
                    return ResolveMiniFieldValue(valueField);
            }
            // 回退：无数据组或组内无 Value 字段
            if (UsageVm != null) return UsageVm.UsagePercentage;
            return Descriptor.DataSource as double?;
        }
    }

    /// <summary>
    /// B5 色阶接入：按当前用量百分比解析出的进度画刷（已 Freeze，可安全跨线程使用）。
    /// <para>
    /// 优先使用 <see cref="Descriptor"/> 的 <see cref="MiniChartDescriptor.ColorTier"/> 声明的私有档位换色；
    /// ColorTier 为 null 或全部档位禁用时落地全局 <see cref="UsageTierScale"/> 色阶（与主界面进度条取色一致）。
    /// </para>
    /// <para>
    /// 色阶变更实时刷新链路：UsageTierScale.TierChanged → MainViewModel.OnUsageTierChanged →
    /// ForceRefreshBars → ProviderUsageViewModel PropertyChanged → TaskbarWindow.OnUsageVmPropertyChanged →
    /// RefreshFromUsageVm → TierBrush 通知。无需本 VM 独立订阅 TierChanged。
    /// </para>
    /// </summary>
    public Brush TierBrush
    {
        get
        {
            var percent = UsagePercent ?? 0;
            // 私有档位优先（升序匹配 + IsEnabled 过滤）；null/空/全禁用时内部自动回退全局色阶
            return UsageTierScale.ResolveBrush(Descriptor.ColorTier?.Tiers, percent);
        }
    }

    /// <summary>
    /// 派生：环图模式是否处于错误态（用于 XAML 显隐「错误」覆盖层）。
    /// </summary>
    public bool IsError => UsageVm?.IsError ?? false;

    /// <summary>
    /// req-079 U-33：数据是否就绪（关联卡片 VM 已收到过至少一次数据更新）。
    /// <para>为 false 时任务栏迷你图模板显示骨架屏占位（SkeletonPlaceholder），
    /// 避免“数据尚未就绪 / 正在刷新且无缓存数据”状态下的空白闪烁；
    /// 有缓存数据的刷新期间保持显示旧值，不闪骨架屏。</para>
    /// </summary>
    public bool IsDataReady => UsageVm != null && UsageVm.HasReceivedData;

    /// <summary>
    /// req-088 B9：渲染后的 Tooltip 标题文本（来自 MiniChartTooltip.TitleTemplate）。
    /// <para>支持占位符：<c>{ProviderName}</c> / <c>{Percent}</c> / <c>{Value}</c> / <c>{Timestamp}</c>。</para>
    /// </summary>
    public string TooltipTitle
    {
        get
        {
            var template = Descriptor.Tooltip?.TitleTemplate;
            if (string.IsNullOrEmpty(template)) return UsageVm?.DisplayName ?? ProviderId;
            return ResolveTooltipTemplate(template);
        }
    }

    /// <summary>
    /// req-088 B9：渲染后的 Tooltip 正文文本（来自 MiniChartTooltip.BodyTemplate）。
    /// <para>req-107 B4：有数据组声明时正文为“当前组名称：数值%”（如“5h 限额：42%”），
    /// 切组后自动更新；无数据组时回退模板渲染。空字符串表示不显示 Body 行。</para>
    /// </summary>
    public string TooltipBody
    {
        get
        {
            // req-107 B4：数据组模式——正文 = 组名称 + 当前值
            if (HasDataGroups)
            {
                var val = UsagePercent;
                return val.HasValue
                    ? $"{CurrentDataGroupName}：{val.Value:0}%"
                    : $"{CurrentDataGroupName}：--";
            }
            var template = Descriptor.Tooltip?.BodyTemplate;
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return ResolveTooltipTemplate(template);
        }
    }

    /// <summary>
    /// req-107 B4：复合 Tooltip 文本（标题 + 换行 + 正文）。
    /// <para>问题8：有效字段配置（<see cref="EffectiveTooltipFields"/> 非 null）时按字段逐行构建；
    /// 空集合/无可展示内容时返回 null（WPF ToolTip 为 null 时不显示）；
    /// 无字段配置时回退旧行为（正文为空时仅返回标题）。</para>
    /// </summary>
    public string? CompositeTooltipText
    {
        get
        {
            var fields = EffectiveTooltipFields;
            if (fields != null)
            {
                var lines = BuildFieldTooltipLines(fields);
                return lines.Count > 0 ? string.Join("\n", lines) : null;
            }
            var title = TooltipTitle;
            var body = TooltipBody;
            return string.IsNullOrEmpty(body) ? title : $"{title}\n{body}";
        }
    }

    /// <summary>
    /// 问题8：按有效字段构建 tooltip 行列表（问题9：按用户保存的字段顺序；取不到值的字段跳过）。
    /// <para>问题11：存在数据组时，百分比值字段仅展示当前滚轮选中数据组的 Value 字段，
    /// 保证 tooltip 随滚轮切换分别显示 5h 限额 / 周限额。</para>
    /// </summary>
    private List<string> BuildFieldTooltipLines(IReadOnlyList<string> fields)
    {
        var lines = new List<string>();
        var currentValueField = CurrentDataGroup?.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
        foreach (var fieldName in fields)
        {
            switch (fieldName)
            {
                case MiniTooltipFieldCatalog.ProviderNameVirtual:
                    lines.Add(UsageVm?.DisplayName ?? ProviderId);
                    break;
                case UsageFields.AccountDisplayName:
                    if (!string.IsNullOrWhiteSpace(AccountName)) lines.Add($"账号：{AccountName}");
                    break;
                case UsageFields.FiveHourUsedPercent:
                {
                    // 问题11：当前数据组非 5h 时跳过（无数据组声明时不限制）
                    if (HasDataGroups && currentValueField != null &&
                        !string.Equals(currentValueField, UsageFields.FiveHourUsedPercent, StringComparison.OrdinalIgnoreCase)) break;
                    var v = ResolveMiniFieldValue(UsageFields.FiveHourUsedPercent);
                    if (v.HasValue) lines.Add($"5h 已用 {v.Value:0}%");
                    break;
                }
                case UsageFields.WeeklyUsedPercent:
                {
                    if (HasDataGroups && currentValueField != null &&
                        !string.Equals(currentValueField, UsageFields.WeeklyUsedPercent, StringComparison.OrdinalIgnoreCase)) break;
                    var v = ResolveMiniFieldValue(UsageFields.WeeklyUsedPercent);
                    if (v.HasValue) lines.Add($"本周已用 {v.Value:0}%");
                    break;
                }
                case MiniTooltipFieldCatalog.RefreshCountdownVirtual:
                    if (!string.IsNullOrEmpty(RefreshCountdownText)) lines.Add(RefreshCountdownText);
                    break;
            }
        }
        return lines;
    }

    /// <summary>
    /// 问题8：MiniText 模板是否显示 Provider 名片段（无字段配置时沿用旧行为始终显示）。
    /// </summary>
    public bool ShowProviderInText
        => EffectiveTooltipFields == null
           || EffectiveTooltipFields.Contains(MiniTooltipFieldCatalog.ProviderNameVirtual, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 问题12：MiniText 模板的正文片段（Provider 名之外的内容）。
    /// <para>改为数据组驱动：仅展示用户勾选的数据组（按勾选顺序，如 "5h 42% 周 30%"），
    /// 倒计时段由虚拟倒计时数据组勾选控制；未勾选任何数据组时不显示数据段。
    /// 无数据组声明（旧注册路径）时回退当前百分比。</para>
    /// </summary>
    public string MiniTextBody
    {
        get
        {
            if (Descriptor.DataGroups is { Count: > 0 })
            {
                var parts = new List<string>();
                foreach (var group in EffectiveDataGroups)
                {
                    var valueField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
                    if (valueField == null) continue;
                    var v = ResolveMiniFieldValue(valueField);
                    var shortLabel = ResolveFieldShortLabel(valueField, group);
                    parts.Add(v.HasValue ? $"{shortLabel} {v.Value:0}%" : $"{shortLabel} --");
                }
                // 问题12：倒计时段由虚拟数据组勾选控制
                if (ShowCountdownInText)
                {
                    var countdown = UsageVm?.FiveHourCountdownText;
                    if (!string.IsNullOrWhiteSpace(countdown) && countdown != "00:00:00") parts.Add(countdown!);
                }
                return string.Join(" ", parts);
            }
            // 回退：无数据组声明时显示当前百分比（与旧模板 StringFormat 行为一致）
            var val = UsagePercent;
            return val.HasValue ? $"{val.Value:0}%" : "--";
        }
    }

    /// <summary>问题12：字段短标签（文本迷你图紧凑展示用，如 "5h" / "周"）；未知字段回退数据组显示名。</summary>
    private static string ResolveFieldShortLabel(string fieldName, DataGroup group) => fieldName switch
    {
        UsageFields.FiveHourUsedPercent => "5h",
        UsageFields.WeeklyUsedPercent => "周",
        UsageFields.VideoQuota => "视频",
        UsageFields.RemainingCredits => "积分",
        _ => group.Display ?? group.Id
    };

    /// <summary>
    /// req-088 B9：当前 descriptor 是否启用 Tooltip（ShowDelayMs ≥ 0 时显示）。
    /// </summary>
    public bool HasTooltip => (Descriptor.Tooltip?.ShowDelayMs ?? 0) >= 0 && !string.IsNullOrEmpty(TooltipTitle);

    /// <summary>req-088 B9：Tooltip 显示延迟（毫秒），负数表示禁用。</summary>
    public int TooltipShowDelayMs => Descriptor.Tooltip?.ShowDelayMs ?? 0;

    // ===================== req-107 B4：数据组状态 =====================

    /// <summary>req-107 B4：当前数据组索引（循环切换，由 <see cref="CycleDataGroup"/> 更新）。</summary>
    private int _currentDataGroupIndex;

    /// <summary>req-107 B4：当前数据组索引（只读暴露，供调试 / 测试断言）。</summary>
    public int CurrentDataGroupIndex => _currentDataGroupIndex;

    /// <summary>
    /// req-107 B4：是否存在可用数据组（问题11：按用户勾选过滤后至少 1 组）。
    /// <para>有数据组时 UsagePercent / TooltipBody 走组解析路径；无则回退原有单值逻辑。</para>
    /// </summary>
    public bool HasDataGroups => EffectiveDataGroups.Count > 0;

    /// <summary>
    /// req-107 B4：当前选中的数据组（问题11：基于有效数据组列表，索引越界时自动钳位）；无可用组时返回 null。
    /// </summary>
    public DataGroup? CurrentDataGroup
    {
        get
        {
            var groups = EffectiveDataGroups;
            if (groups.Count == 0) return null;
            return groups[Math.Clamp(_currentDataGroupIndex, 0, groups.Count - 1)];
        }
    }

    /// <summary>
    /// req-107 B4：当前数据组的显示名称（按 Value 字段名解析中文标签，如 "5h 限额" / "本周限额"）。
    /// <para>组内无 Value 字段时回退显示组 Id。</para>
    /// </summary>
    public string CurrentDataGroupName
    {
        get
        {
            var group = CurrentDataGroup;
            if (group == null) return string.Empty;
            var valueField = group.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
            return valueField != null ? ResolveFieldLabel(valueField) : group.Id;
        }
    }

    /// <summary>
    /// req-107 B4：循环切换数据组（滚轮驱动）。
    /// <para>无数据组或仅 1 组时不产生变化（返回 false，滚轮事件透传给 ScrollViewer）；
    /// 多组时按 delta 方向循环切换，并触发 UsagePercent / TierBrush / Tooltip 等属性通知。</para>
    /// </summary>
    /// <param name="delta">切换方向：+1 = 下一组，-1 = 上一组。</param>
    /// <returns>是否实际发生了切换。</returns>
    public bool CycleDataGroup(int delta)
    {
        // 问题11：仅在用户勾选的有效数据组内循环，取消勾选的组不再参与滚轮切换。
        var groups = EffectiveDataGroups;
        if (groups.Count <= 1) return false;
        // 循环索引（支持负数 delta；先钳位再循环，避免过滤后列表变短导致越界）
        var current = Math.Clamp(_currentDataGroupIndex, 0, groups.Count - 1);
        var newIndex = ((current + delta) % groups.Count + groups.Count) % groups.Count;
        if (newIndex == _currentDataGroupIndex) return false;
        _currentDataGroupIndex = newIndex;
        // 切组后全量通知：环 Percent、色阶画刷、tooltip 均随当前组重算
        OnPropertyChanged(nameof(CurrentDataGroupIndex));
        OnPropertyChanged(nameof(CurrentDataGroup));
        OnPropertyChanged(nameof(CurrentDataGroupName));
        OnPropertyChanged(nameof(UsagePercent));
        OnPropertyChanged(nameof(TierBrush));
        OnPropertyChanged(nameof(TooltipTitle));
        OnPropertyChanged(nameof(TooltipBody));
        OnPropertyChanged(nameof(CompositeTooltipText));
        // 问题8：MiniText 回退模式下正文随当前组切换；倒计时来源也随当前组（5h/周）切换。
        OnPropertyChanged(nameof(MiniTextBody));
        OnPropertyChanged(nameof(RefreshCountdownText));
        return true;
    }

    /// <summary>
    /// req-107 B4：迷你图字段取值器——SDK 标准字段名 → 当前值。
    /// <para>参照 <c>ProviderUsageViewModel.ResolveFieldValue</c> 的映射规则（过渡期映射到已刷新的 VM 属性）；
    /// 未知字段回退 <c>UsageVm.UsagePercentage</c>，无 UsageVm 时回退 descriptor.DataSource。</para>
    /// </summary>
    private double? ResolveMiniFieldValue(string fieldName)
    {
        if (UsageVm == null) return Descriptor.DataSource as double?;
        return fieldName switch
        {
            UsageFields.FiveHourUsedPercent => UsageVm.PrimaryBarPercent,
            UsageFields.WeeklyUsedPercent => UsageVm.WeeklyBarPercent,
            UsageFields.VideoQuota => UsageVm.VideoIntervalPercent,
            UsageFields.RemainingCredits => UsageVm.RemainingCredits,
            _ => UsageVm.UsagePercentage // 未知字段回退 UsagePercent
        };
    }

    /// <summary>
    /// req-107 B4：字段显示标签解析（与 ProviderUsageViewModel.DeclarativeFieldLabel 保持一致的中文标签）。
    /// </summary>
    private static string ResolveFieldLabel(string fieldName) => fieldName switch
    {
        UsageFields.FiveHourUsedPercent => "5h 限额",
        UsageFields.WeeklyUsedPercent => "本周限额",
        UsageFields.VideoQuota => "视频赠送",
        UsageFields.RemainingCredits => "剩余积分",
        _ => fieldName
    };

    /// <summary>
    /// req-107 B4：解析初始数据组索引——优先定位 Slicer.Default 声明的组 Id（在有效数据组内查找），未匹配时默认第 0 组。
    /// </summary>
    private int ResolveInitialDataGroupIndex()
    {
        var groups = EffectiveDataGroups;
        if (groups.Count == 0) return 0;
        var defaultId = Descriptor.Slicer?.Default;
        if (!string.IsNullOrEmpty(defaultId))
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (string.Equals(groups[i].Id, defaultId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return 0;
    }

    // req-105：本地 DispatcherTimer 推动 RefreshCountdownText 每秒刷新。
    // 默认不启动，宿主（TaskbarWindow）可调 <see cref="StartCountdownTimer"/> 启动。
    // 设计上与 MainViewModel 全局 timer 并存：全局 timer 每秒更新 <c>FiveHourCountdownText</c>
    // （依赖 Provider 数据推送），本地 timer 负责本实例的 INPC 触发（与数据推送解耦）。
    private DispatcherTimer? _countdownTimer;

    /// <summary>
    /// req-105：动态刷新倒计时文案（如 "重置倒计时：2 小时 21 分钟"）。
    /// <para>问题8：按当前数据组选择倒计时来源——周限额组（weekly_used_percent）取
    /// <c>WeeklyCountdownText</c>（周限额重置倒计时），其它组取 <c>FiveHourCountdownText</c>（5h 重置倒计时）。
    /// 无有效倒计时时回退 "上次更新：{LastUpdateText}" 兜底；两者都为空时返回空串。</para>
    /// </summary>
    public string RefreshCountdownText
    {
        get
        {
            if (UsageVm == null) return string.Empty;
            // 问题8：当前数据组为周限额时展示周限额重置倒计时（而非 5h 刷新倒计时）。
            var currentValueField = CurrentDataGroup?.Fields.FirstOrDefault(f => f.Role == FieldRole.Value)?.FieldName;
            var isWeekly = string.Equals(currentValueField, UsageFields.WeeklyUsedPercent, StringComparison.OrdinalIgnoreCase);
            var countdown = isWeekly ? UsageVm.WeeklyCountdownText : UsageVm.FiveHourCountdownText;
            if (!string.IsNullOrWhiteSpace(countdown) && countdown != "00:00:00")
                return isWeekly ? $"周重置倒计时：{countdown}" : $"重置倒计时：{countdown}";
            if (!string.IsNullOrWhiteSpace(UsageVm.LastUpdateText))
                return $"上次更新：{UsageVm.LastUpdateText}";
            return string.Empty;
        }
    }

    /// <summary>
    /// req-105：启动本 VM 的倒计时 DispatcherTimer（每秒 Tick 触发 RefreshCountdownText 通知）。
    /// <para>幂等：多次调用不会重复启动 timer。宿主在 <c>TaskbarWindow.Loaded</c> 时调用。</para>
    /// </summary>
    public void StartCountdownTimer()
    {
        if (_countdownTimer != null) return;
        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(RefreshCountdownText));
            // 问题8/12：倒计时可能参与 tooltip/文本渲染（字段勾选或虚拟倒计时数据组），每秒同步刷新。
            if (EffectiveTooltipFields != null || ShowCountdownInText)
            {
                OnPropertyChanged(nameof(CompositeTooltipText));
                OnPropertyChanged(nameof(MiniTextBody));
            }
        };
        _countdownTimer.Start();
    }

    /// <summary>
    /// req-105：停止本 VM 的倒计时 DispatcherTimer。宿主在 <c>TaskbarWindow.Unloaded</c> 时调用避免内存泄漏。
    /// </summary>
    public void StopCountdownTimer()
    {
        if (_countdownTimer == null) return;
        _countdownTimer.Stop();
        _countdownTimer = null;
    }

    public MiniChartItemViewModel(MiniChartDescriptor descriptor, ViewModels.ProviderUsageViewModel? usageVm)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        UsageVm = usageVm;
        // req-107 B4：初始数据组索引从 Slicer.Default 解析（未声明时默认第 0 组）
        _currentDataGroupIndex = ResolveInitialDataGroupIndex();
    }

    /// <summary>
    /// 由外部（ProviderUsageViewModel 数据更新时）调用以触发本 VM 刷新。
    /// <para>所有派生属性会发出 PropertyChanged 让 XAML 重新取样。</para>
    /// </summary>
    public void RefreshFromUsageVm()
    {
        OnPropertyChanged(nameof(UsagePercent));
        OnPropertyChanged(nameof(TierBrush)); // B5：色阶画刷随百分比联动刷新
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsDataReady)); // req-079 U-33：骨架屏随数据到达退出占位
        OnPropertyChanged(nameof(TooltipTitle));
        OnPropertyChanged(nameof(TooltipBody));
        OnPropertyChanged(nameof(HasTooltip));
        // req-107 B4：数据组相关派生属性随数据刷新联动
        OnPropertyChanged(nameof(CurrentDataGroupName));
        OnPropertyChanged(nameof(CompositeTooltipText));
        // 问题8：MiniText 字段驱动正文随数据刷新联动
        OnPropertyChanged(nameof(MiniTextBody));
        OnPropertyChanged(nameof(ShowProviderInText));
        // req-105：刷新动态倒计时。FiveHourCountdownText 由 MainViewModel 全局 timer 每秒刷新。
        OnPropertyChanged(nameof(RefreshCountdownText));
    }

    /// <summary>
    /// 替换 MiniChartTooltip 占位符为实际值。
    /// <para>占位符集合：<c>{ProviderName}</c> → DisplayName / <c>{Percent}</c> → UsagePercent /
    /// <c>{Value}</c> → 原始 double / <c>{Timestamp}</c> → HH:mm:ss /
    /// <c>{RefreshCountdown}</c> → <see cref="RefreshCountdownText"/>（req-105 新增）。</para>
    /// </summary>
    private string ResolveTooltipTemplate(string template)
    {
        // req-105：先按 descriptor.ToolTipFields 剔除未启用字段的占位符（未来扩展点落地）。
        var result = StripDisabledTooltipFields(template);
        var displayName = UsageVm?.DisplayName ?? ProviderId;
        result = result.Replace("{ProviderName}", displayName);
        if (UsagePercent.HasValue)
            result = result.Replace("{Percent}", UsagePercent.Value.ToString("0.0"));
        else
            result = result.Replace("{Percent}", "--");
        if (UsageVm?.HistoryValues is { Count: > 0 } last && double.IsFinite(last[^1]))
            result = result.Replace("{Value}", last[^1].ToString("0.0"));
        else
            result = result.Replace("{Value}", "--");
        // req-105：用动态倒计时文案替换占位符；无数据时移除占位符。
        result = result.Replace("{RefreshCountdown}", RefreshCountdownText);
        result = result.Replace("{Timestamp}", DateTime.Now.ToString("HH:mm:ss"));
        return result;
    }

    /// <summary>
    /// req-105：按 <see cref="MiniChartDescriptor.ToolTipFields"/> 剔除未启用字段的占位符。
    /// <para>字段名 → 占位符映射：ProviderName→{ProviderName}；CurrentValue→{Value}/{Percent}；
    /// RefreshCountdown→{RefreshCountdown}。{Timestamp} 为元数据始终保留。未列出的字段占位符被替换为空串。</para>
    /// </summary>
    private string StripDisabledTooltipFields(string template)
    {
        var fields = Descriptor.ToolTipFields;
        if (fields == null || fields.Count == 0) return template;
        var set = new System.Collections.Generic.HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
        var result = template;
        if (!set.Contains("ProviderName")) result = result.Replace("{ProviderName}", "");
        if (!set.Contains("CurrentValue"))
        {
            result = result.Replace("{Value}", "");
            result = result.Replace("{Percent}", "");
        }
        if (!set.Contains("RefreshCountdown")) result = result.Replace("{RefreshCountdown}", "");
        return result;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
