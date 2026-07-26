using System.Threading;
using UsageMonitor.App.Controls;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using Xunit;

namespace UsageMonitor.App.Tests;

/// <summary>
/// 八项缺陷修复批次（2026-07-26）：核心纯逻辑回归测试。
/// <para>覆盖：#3 折线图周期切片器由插件声明驱动（ChartPeriods 泛化 + VM 声明派生）；
/// 非声明路径不再误关切片器。控件级 tooltip 顺序 / 色阶直驱等 UI 行为由 ComputerUse 验证。</para>
/// </summary>
public class BugfixBatchEightTests
{
    // =====================================================================
    // #3：ChartPeriods 泛化（任意 "Nd" 周期键 + TimeRange 派生选项）
    // =====================================================================

    /// <summary>#3：ToDays 支持任意 "Nd" 形式周期键，未知值回退 7 天。</summary>
    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    [InlineData("abc", 7)]
    [InlineData(null, 7)]
    public void ChartPeriods_ToDays_ParsesArbitraryDayKeys(string? period, int expected)
        => Assert.Equal(expected, ChartPeriods.ToDays(period));

    /// <summary>#3：TimeRange 派生周期选项（键 = "Nd"，文案 = "近 N 天"）。</summary>
    [Fact]
    public void ChartPeriods_FromTimeRange_DerivesOption()
    {
        var opt7 = ChartPeriods.FromTimeRange(TimeRange.Last7Days);
        Assert.NotNull(opt7);
        Assert.Equal("7d", opt7!.Period);
        Assert.Equal("近 7 天", opt7.Label);

        var opt90 = ChartPeriods.FromTimeRange(TimeRange.Last90Days);
        Assert.NotNull(opt90);
        Assert.Equal("90d", opt90!.Period);
    }

    /// <summary>#3：无限窗口（All）无法映射为天数，返回 null 不生成按钮。</summary>
    [Fact]
    public void ChartPeriods_FromTimeRange_AllReturnsNull()
        => Assert.Null(ChartPeriods.FromTimeRange(TimeRange.All));

    // =====================================================================
    // #3：VM 按声明派生 SupportsPeriodSwitch / PeriodOptions / 默认周期
    // =====================================================================

    /// <summary>构造带 Period 切片器折线图声明的桩 Provider（与 MiniMax 声明同构）。</summary>
    private static DeclarativeFakeProvider BuildProviderWithPeriodSlicer()
        => new(new CardDeclaration
        {
            Charts = new[]
            {
                new ChartDeclaration
                {
                    ChartId = "fk.chart.daily_line",
                    Kind = DeclarativeChartKind.Line,
                    Slicer = new SlicerSpec
                    {
                        Mode = SlicerMode.Period,
                        Interaction = SlicerInteraction.Button,
                        TimeRanges = new[] { TimeRange.Last7Days, TimeRange.Last30Days },
                        Default = "Last30Days"
                    },
                    DataGroups = new[]
                    {
                        new DataGroup
                        {
                            Id = "fk.line.daily",
                            Fields = new[]
                            {
                                new FieldReference { FieldName = UsageFields.DailyTokenDate, Role = FieldRole.Meta },
                                new FieldReference { FieldName = UsageFields.DailyTokenValue, Role = FieldRole.Value }
                            }
                        }
                    }
                }
            }
        });

    /// <summary>#3：注入声明了 Period 切片器的 Provider 后，VM 开启切片器并派生选项与默认周期。</summary>
    [Fact]
    public void Provider_WithDeclaredPeriodSlicer_EnablesSwitchAndOptions()
    {
        var vm = new ProviderUsageViewModel { Provider = BuildProviderWithPeriodSlicer() };

        Assert.True(vm.SupportsPeriodSwitch);
        Assert.NotNull(vm.PeriodOptions);
        Assert.Equal(2, vm.PeriodOptions!.Count);
        Assert.Equal("7d", vm.PeriodOptions[0].Period);
        Assert.Equal("30d", vm.PeriodOptions[1].Period);
        // 默认周期取声明的 default（Last30Days → "30d"）
        Assert.Equal("30d", vm.CurrentPeriod);
    }

    /// <summary>#3：无折线图切片器声明时切片器保持关闭、选项为 null。</summary>
    [Fact]
    public void Provider_WithoutPeriodSlicer_KeepsSwitchDisabled()
    {
        var vm = new ProviderUsageViewModel { Provider = new DeclarativeFakeProvider(new CardDeclaration()) };

        Assert.False(vm.SupportsPeriodSwitch);
        Assert.Null(vm.PeriodOptions);
    }

    /// <summary>#3 回归：HistoryValues 赋值（非声明式渲染路径）不再无条件关闭声明驱动的切片器。</summary>
    [Fact]
    public void HistoryValues_DoesNotDisableDeclaredPeriodSwitch()
    {
        var vm = new ProviderUsageViewModel { Provider = BuildProviderWithPeriodSlicer() };
        Assert.True(vm.SupportsPeriodSwitch);

        vm.HistoryValues = new double[] { 10, 20, 30 };

        Assert.True(vm.SupportsPeriodSwitch);
    }

    /// <summary>
    /// 测试用声明式 Provider 桩：仅提供 Card 声明聚合根，其余成员为最小实现。
    /// </summary>
    private sealed class DeclarativeFakeProvider : IUsageProvider
    {
        private readonly CardDeclaration _card;

        /// <summary>以指定卡片声明构造桩 Provider。</summary>
        public DeclarativeFakeProvider(CardDeclaration card) => _card = card;

        public string ProviderId => "fake-declarative";
        public string DisplayName => "FakeDeclarative";
        public string? IconPath => null;
        public string Version => "1.0.0-test";
        public string Author => "test";
        public string Description => "Fake declarative provider for slicer tests";
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
