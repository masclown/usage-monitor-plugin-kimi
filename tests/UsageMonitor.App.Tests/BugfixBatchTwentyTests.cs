using UsageMonitor.App.Services.Display;
using UsageMonitor.App.Views;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.MiniChart;
using Xunit;

namespace UsageMonitor.App.Tests;

/// <summary>
/// 二十项缺陷修复批次（2026-07-25）：核心纯逻辑回归测试。
/// <para>覆盖：#4 进度条 Reset 字段透传；#6/#7 Number 图表 Upper/Meta 可选角色；
/// #11 迷你图数据组勾选过滤与滚轮循环；#12 文本迷你图数据组驱动与虚拟倒计时组。</para>
/// </summary>
public class BugfixBatchTwentyTests
{
    /// <summary>构造带 5h/周两个数据组的迷你环描述符（与 MiniMax 声明同构）。</summary>
    private static MiniChartDescriptor BuildRingDescriptor()
        => new()
        {
            ProviderId = "fake",
            ChartId = "fk.mini.ring",
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
            },
            Slicer = new SlicerSpec { Mode = SlicerMode.DataGroup, Default = "fk.taskbar.5h" }
        };

    /// <summary>#11：未配置可见数据组（null）时，有效数据组等于全部声明组。</summary>
    [Fact]
    public void EffectiveDataGroups_NullConfig_ReturnsAllDeclared()
    {
        var vm = new MiniChartItemViewModel(BuildRingDescriptor(), null);
        Assert.Equal(2, vm.EffectiveDataGroups.Count);
        Assert.True(vm.HasDataGroups);
    }

    /// <summary>#11：取消勾选「本周限额」后，有效数据组仅剩 5h，滚轮无法切换（仅 1 组）。</summary>
    [Fact]
    public void EffectiveDataGroups_UncheckWeekly_ExcludesGroupAndBlocksCycle()
    {
        var vm = new MiniChartItemViewModel(BuildRingDescriptor(), null)
        {
            VisibleDataGroupIds = new[] { "fk.taskbar.5h" }
        };
        vm.ReinitializeDataGroupIndex();

        Assert.Single(vm.EffectiveDataGroups);
        Assert.Equal("fk.taskbar.5h", vm.CurrentDataGroup!.Id);
        // 仅 1 组时滚轮不产生切换（透传给外层滚动）
        Assert.False(vm.CycleDataGroup(+1));
        Assert.Equal("fk.taskbar.5h", vm.CurrentDataGroup!.Id);
    }

    /// <summary>#11：勾选顺序即展示顺序，滚轮在勾选集合内循环切换。</summary>
    [Fact]
    public void CycleDataGroup_CyclesWithinCheckedGroupsInOrder()
    {
        var vm = new MiniChartItemViewModel(BuildRingDescriptor(), null)
        {
            VisibleDataGroupIds = new[] { "fk.taskbar.weekly", "fk.taskbar.5h" }
        };
        vm.ReinitializeDataGroupIndex();

        // Slicer.Default=5h → 在有效列表中定位到索引 1
        Assert.Equal("fk.taskbar.5h", vm.CurrentDataGroup!.Id);
        Assert.True(vm.CycleDataGroup(+1));
        Assert.Equal("fk.taskbar.weekly", vm.CurrentDataGroup!.Id);
        Assert.True(vm.CycleDataGroup(+1));
        Assert.Equal("fk.taskbar.5h", vm.CurrentDataGroup!.Id);
    }

    /// <summary>#12：文本迷你图未勾选任何数据组（空集合）时，正文不再显示 5h/周/倒计时。</summary>
    [Fact]
    public void MiniTextBody_EmptyVisibleGroups_ShowsNothing()
    {
        var descriptor = BuildRingDescriptor();
        var vm = new MiniChartItemViewModel(descriptor, null)
        {
            VisibleDataGroupIds = System.Array.Empty<string>()
        };
        vm.ReinitializeDataGroupIndex();

        Assert.Equal(string.Empty, vm.MiniTextBody);
        Assert.False(vm.ShowCountdownInText);
    }

    /// <summary>#12：勾选虚拟倒计时数据组 ID 时 ShowCountdownInText 为 true（倒计时段参与文本渲染）。</summary>
    [Fact]
    public void ShowCountdownInText_VirtualGroupChecked_ReturnsTrue()
    {
        var vm = new MiniChartItemViewModel(BuildRingDescriptor(), null)
        {
            VisibleDataGroupIds = new[] { "fk.taskbar.5h", "__refresh_countdown__" }
        };
        Assert.True(vm.ShowCountdownInText);
        // 虚拟组不在声明中，不影响有效数据组列表
        Assert.Single(vm.EffectiveDataGroups);
    }

    /// <summary>#4：Bar 数据组声明 Reset 角色字段时，构建的进度条携带 ResetFieldName 与重置文案 FooterText。</summary>
    [Fact]
    public void BuildMetricBars_ResetField_FillsResetFieldNameAndFooter()
    {
        var card = new CardDeclaration
        {
            Charts = new[]
            {
                new ChartDeclaration
                {
                    ChartId = "fk.chart.bar",
                    Kind = DeclarativeChartKind.Bar,
                    DataGroups = new[]
                    {
                        new DataGroup
                        {
                            Id = "fk.bar.5h",
                            Fields = new[]
                            {
                                new FieldReference { FieldName = UsageFields.FiveHourUsedPercent, Role = FieldRole.Value },
                                new FieldReference { FieldName = UsageFields.FiveHourResetAt, Role = FieldRole.Reset }
                            }
                        }
                    }
                }
            }
        };

        var bars = DeclarativeChartBuilder.BuildMetricBars(
            card,
            valueResolver: _ => 42,
            resetTextResolver: f => f == UsageFields.FiveHourResetAt ? "2 小时后重置" : null);

        Assert.NotNull(bars);
        var bar = Assert.Single(bars!.Bars);
        Assert.Equal(UsageFields.FiveHourResetAt, bar.ResetFieldName);
        Assert.Equal("2 小时后重置", bar.FooterText);
    }

    /// <summary>#6/#7：Number 图表声明 Upper（分母）与 Meta（备注）角色字段时通过规格校验。</summary>
    [Fact]
    public void ChartKindSpec_NumberChart_AllowsUpperAndMetaRoles()
    {
        var chart = new ChartDeclaration
        {
            ChartId = "fk.chart.number",
            Kind = DeclarativeChartKind.Number,
            DataGroups = new[]
            {
                new DataGroup
                {
                    Id = "fk.number.active_days",
                    Fields = new[]
                    {
                        new FieldReference { FieldName = UsageFields.ActiveDays, Role = FieldRole.Value },
                        new FieldReference { FieldName = UsageFields.TotalDays, Role = FieldRole.Upper },
                        new FieldReference { FieldName = UsageFields.MostActiveDate, Role = FieldRole.Meta }
                    }
                }
            }
        };

        var errors = ChartKindSpecRegistry.Validate(chart);
        Assert.Empty(errors);
    }

    /// <summary>#4：Bar 图表声明 Reset 角色字段时通过规格校验（OptionalRoles 已扩展）。</summary>
    [Fact]
    public void ChartKindSpec_BarChart_AllowsResetRole()
    {
        var chart = new ChartDeclaration
        {
            ChartId = "fk.chart.bar2",
            Kind = DeclarativeChartKind.Bar,
            DataGroups = new[]
            {
                new DataGroup
                {
                    Id = "fk.bar.weekly",
                    Fields = new[]
                    {
                        new FieldReference { FieldName = UsageFields.WeeklyUsedPercent, Role = FieldRole.Value },
                        new FieldReference { FieldName = UsageFields.WeeklyResetAt, Role = FieldRole.Reset }
                    }
                }
            }
        };

        var errors = ChartKindSpecRegistry.Validate(chart);
        Assert.Empty(errors);
    }
}
