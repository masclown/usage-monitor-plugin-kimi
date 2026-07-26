using System.Reflection;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Plugins.MiniChart;
using Xunit;

namespace UsageMonitor.App.Tests;

/// <summary>
/// 八项缺陷修复批次 V2（2026-07-26 第二轮）：核心纯逻辑回归测试。
/// <para>覆盖：#8 周限额倒计时（VM 周倒计时刷新 + 迷你图按当前数据组选择倒计时来源）；
/// #5 折叠分界线槽位插入；#4 数据概览按数据组拆分 tooltip。
/// 头部布局 / 横向滚动 / 热力图 tooltip 顺序等 UI 行为由 ComputerUse 验证。</para>
/// </summary>
public class BugfixBatchEightV2Tests
{
    // =====================================================================
    // #8：ProviderUsageViewModel 周限额倒计时
    // =====================================================================

    /// <summary>#8：设置周重置时刻后，全局 timer tick（RefreshFiveHourCountdownText）同步刷新周倒计时文本。</summary>
    [Fact]
    public void WeeklyCountdown_RefreshedByGlobalTimerTick()
    {
        var vm = new ProviderUsageViewModel();
        var now = new DateTime(2026, 7, 26, 10, 0, 0);
        vm.NextWeeklyResetAt = now.AddHours(1).AddMinutes(2).AddSeconds(3);

        vm.RefreshFiveHourCountdownText(now);

        Assert.Equal("01:02:03", vm.WeeklyCountdownText);
    }

    /// <summary>#8：无周重置时刻时周倒计时保持 00:00:00（不影响 5h 倒计时语义）。</summary>
    [Fact]
    public void WeeklyCountdown_NullResetAt_KeepsZero()
    {
        var vm = new ProviderUsageViewModel();
        vm.RefreshFiveHourCountdownText(new DateTime(2026, 7, 26, 10, 0, 0));
        Assert.Equal("00:00:00", vm.WeeklyCountdownText);
    }

    // =====================================================================
    // #8：迷你图按当前数据组（5h/周）选择倒计时来源
    // =====================================================================

    /// <summary>构造带 5h/周两个数据组的迷你图描述符（与 MiniMax taskbar 声明同构）。</summary>
    private static MiniChartDescriptor BuildMiniDescriptor() => new()
    {
        ProviderId = "fake",
        Kind = MiniChartKind.MiniRingChart,
        DataGroups = new[]
        {
            new DataGroup
            {
                Id = "fk.taskbar.5h",
                Display = "5h 限额",
                Fields = new[] { new FieldReference { FieldName = UsageFields.FiveHourUsedPercent, Role = FieldRole.Value } }
            },
            new DataGroup
            {
                Id = "fk.taskbar.weekly",
                Display = "本周限额",
                Fields = new[] { new FieldReference { FieldName = UsageFields.WeeklyUsedPercent, Role = FieldRole.Value } }
            }
        }
    };

    /// <summary>#8：当前数据组为 5h 时展示 5h 重置倒计时；滚轮切到周限额组后展示周限额重置倒计时。</summary>
    [Fact]
    public void MiniChart_CountdownSource_FollowsCurrentDataGroup()
    {
        var now = new DateTime(2026, 7, 26, 10, 0, 0);
        var usageVm = new ProviderUsageViewModel
        {
            Next5hResetAt = now.AddHours(4),
            NextWeeklyResetAt = now.AddHours(13)
        };
        usageVm.RefreshFiveHourCountdownText(now);

        var item = new UsageMonitor.App.Views.MiniChartItemViewModel(BuildMiniDescriptor(), usageVm);

        // 初始组 = 5h 限额 → 5h 重置倒计时
        Assert.StartsWith("重置倒计时：", item.RefreshCountdownText);
        Assert.Contains("04:00:00", item.RefreshCountdownText);

        // 切到周限额组 → 周限额重置倒计时
        Assert.True(item.CycleDataGroup(1));
        Assert.StartsWith("周重置倒计时：", item.RefreshCountdownText);
        Assert.Contains("13:00:00", item.RefreshCountdownText);
    }

    // =====================================================================
    // #5：折叠分界线槽位插入
    // =====================================================================

    /// <summary>构造两图表声明的 Card（Bar + Line）。</summary>
    private static CardDeclaration BuildTwoChartCard(int? dividerIndex) => new()
    {
        CollapseDividerIndex = dividerIndex,
        Charts = new[]
        {
            new ChartDeclaration
            {
                ChartId = "fk.chart.bar",
                Kind = DeclarativeChartKind.Bar,
                DefaultOrder = 1,
                DataGroups = new[]
                {
                    new DataGroup
                    {
                        Id = "fk.bar.5h",
                        Fields = new[] { new FieldReference { FieldName = UsageFields.FiveHourUsedPercent, Role = FieldRole.Value } }
                    }
                }
            },
            new ChartDeclaration
            {
                ChartId = "fk.chart.line",
                Kind = DeclarativeChartKind.Line,
                DefaultOrder = 2,
                DataGroups = new[]
                {
                    new DataGroup
                    {
                        Id = "fk.line.daily",
                        Fields = new[] { new FieldReference { FieldName = UsageFields.DailyTokenValue, Role = FieldRole.Value } }
                    }
                }
            }
        }
    };

    /// <summary>#5：分界线位于图表列表中部时，槽位列表在对应位置插入 SlotKind=Divider 的分界线槽位（折叠态隐藏）。</summary>
    [Fact]
    public void RebuildChartSlots_MidDivider_InsertsDividerSlot()
    {
        var vm = new ProviderUsageViewModel { Provider = new DeclarativeFakeProvider(BuildTwoChartCard(dividerIndex: 1)) };

        vm.RebuildChartSlots();

        Assert.Equal(3, vm.CardChartSlots.Count);
        Assert.Equal("BarGroup", vm.CardChartSlots[0].SlotKind);
        Assert.Equal("Divider", vm.CardChartSlots[1].SlotKind);
        Assert.True(vm.CardChartSlots[1].IsDivider);
        Assert.False(vm.CardChartSlots[1].IsAboveDivider); // 折叠态随下方图表一同隐藏
        Assert.Equal("Line", vm.CardChartSlots[2].SlotKind);
    }

    /// <summary>#5：分界线位于列表末尾（未声明 = 全部可见）时不插入分界线槽位。</summary>
    [Fact]
    public void RebuildChartSlots_TailDivider_NoDividerSlot()
    {
        var vm = new ProviderUsageViewModel { Provider = new DeclarativeFakeProvider(BuildTwoChartCard(dividerIndex: null)) };

        vm.RebuildChartSlots();

        Assert.Equal(2, vm.CardChartSlots.Count);
        Assert.DoesNotContain(vm.CardChartSlots, s => s.IsDivider);
    }

    // =====================================================================
    // #4：数据概览 tooltip 按数据组拆分（不混入其它组字段）
    // =====================================================================

    /// <summary>#4：活跃天数项 tooltip 仅含本组字段（active_days），即便 tooltip 配置同时勾选了累计用量。</summary>
    [Fact]
    public void GroupTooltip_ExcludesOtherGroupFields()
    {
        var vm = new ProviderUsageViewModel();
        // 私有缓存字段直写：模拟一次数据刷新后的取值状态。
        SetPrivateField(vm, "_numberCumulativeText", "5.90B");
        SetPrivateField(vm, "_numberActiveDays", 41L);

        var activeDaysGroup = new DataGroup
        {
            Id = "fk.number.active_days",
            Display = "活跃天数",
            Fields = new[] { new FieldReference { FieldName = UsageFields.ActiveDays, Role = FieldRole.Value } }
        };
        var fields = new[] { "__field_name__", UsageFields.UsedTokens, UsageFields.ActiveDays };

        var tooltip = InvokeBuildGroupTooltip(vm, fields, activeDaysGroup, "活跃天数");

        Assert.NotNull(tooltip);
        Assert.Contains("活跃天数 41", tooltip);
        Assert.DoesNotContain("累计用量", tooltip); // 其它组字段不再混入
    }

    /// <summary>#4：tooltip 配置中无本组字段且未勾选字段名称时返回 null（不显示 tooltip）。</summary>
    [Fact]
    public void GroupTooltip_NoOwnFields_ReturnsNull()
    {
        var vm = new ProviderUsageViewModel();
        SetPrivateField(vm, "_numberCumulativeText", "5.90B");

        var activeDaysGroup = new DataGroup
        {
            Id = "fk.number.active_days",
            Fields = new[] { new FieldReference { FieldName = UsageFields.ActiveDays, Role = FieldRole.Value } }
        };
        var fields = new[] { UsageFields.UsedTokens };

        Assert.Null(InvokeBuildGroupTooltip(vm, fields, activeDaysGroup, "活跃天数"));
    }

    /// <summary>反射调用私有 BuildGroupTooltipText（fields, group, itemLabel）。</summary>
    private static string? InvokeBuildGroupTooltip(ProviderUsageViewModel vm, IReadOnlyList<string> fields, DataGroup group, string label)
    {
        var method = typeof(ProviderUsageViewModel).GetMethod("BuildGroupTooltipText", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (string?)method!.Invoke(vm, new object?[] { fields, group, label });
    }

    /// <summary>反射直写私有字段（模拟数据刷新后的内部缓存状态）。</summary>
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    /// <summary>测试用声明式 Provider 桩：仅提供 Card 声明聚合根，其余成员为最小实现。</summary>
    private sealed class DeclarativeFakeProvider : IUsageProvider
    {
        private readonly CardDeclaration _card;

        /// <summary>以指定卡片声明构造桩 Provider。</summary>
        public DeclarativeFakeProvider(CardDeclaration card) => _card = card;

        public string ProviderId => "fake-declarative-v2";
        public string DisplayName => "FakeDeclarativeV2";
        public string? IconPath => null;
        public string Version => "1.0.0-test";
        public string Author => "test";
        public string Description => "Fake declarative provider for bugfix batch V2 tests";
        public IReadOnlyList<ConfigField> ConfigFields => Array.Empty<ConfigField>();
        public CardDeclaration? Card => _card;

        /// <summary>返回固定成功结果（本测试不消费数据）。</summary>
        public Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
            => Task.FromResult(new UsageInfo { ProviderId = ProviderId, ProviderName = DisplayName, IsSuccess = true });

        /// <summary>配置校验固定通过。</summary>
        public Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
