using UsageMonitor.App.Tests._TestSupport;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using Xunit;

namespace UsageMonitor.App.Tests;

/// <summary>
/// 九项缺陷修复批次（问题2/3/4）：卡片图表 tooltip 有效字段三态语义测试。
/// <para>验证 <see cref="ProviderUsageViewModel.GetEffectiveTooltipFieldsForChart"/>：
/// ① 无用户配置 → 回退声明的 tooltip.fields；② 声明也无 → null（=不过滤，全部显示）；
/// ③ 用户显式空集合 → 空集合（=不显示 tooltip）；④ 用户非空配置 → 用户列表优先。</para>
/// </summary>
public class TooltipFieldSemanticsTests
{
    private const string ProviderId = "tooltip_fake";
    private const string LineChartId = "tf.chart.line";

    /// <summary>带 Card 声明的测试 Provider（折线图可选声明 tooltip 字段）。</summary>
    private sealed class TooltipFakeProvider : IUsageProvider
    {
        private readonly CardDeclaration _card;

        /// <summary>构造：按传入的 tooltip 字段列表生成折线图声明（null = 不声明 tooltip 节）。</summary>
        public TooltipFakeProvider(IReadOnlyList<string>? declaredTooltipFields)
        {
            _card = new CardDeclaration
            {
                Charts = new[]
                {
                    new ChartDeclaration
                    {
                        ChartId = LineChartId,
                        Kind = DeclarativeChartKind.Line,
                        Tooltip = declaredTooltipFields != null ? new TooltipSpec { Fields = declaredTooltipFields } : null,
                        DataGroups = new[]
                        {
                            new DataGroup
                            {
                                Id = "tf.line.daily",
                                Fields = new[]
                                {
                                    new FieldReference { FieldName = UsageFields.DailyTokenDate, Role = FieldRole.Meta },
                                    new FieldReference { FieldName = UsageFields.DailyTokenValue, Role = FieldRole.Value }
                                }
                            }
                        }
                    }
                }
            };
        }

        public string ProviderId => TooltipFieldSemanticsTests.ProviderId;
        public string DisplayName => "TooltipFake";
        public string? IconPath => null;
        public string Version => "1.0.0-test";
        public string Author => "test";
        public string Description => "tooltip 三态语义测试桩";
        public IReadOnlyList<ConfigField> ConfigFields => Array.Empty<ConfigField>();
        public CardDeclaration? Card => _card;

        /// <summary>测试桩不参与刷新，返回固定成功结果。</summary>
        public Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
            => Task.FromResult(new UsageInfo { ProviderId = ProviderId, IsSuccess = true });

        /// <summary>配置校验固定通过。</summary>
        public Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>装配一个绑定临时目录 ConfigService 的卡片 VM。</summary>
    private static (ProviderUsageViewModel vm, ConfigService config, TempDir dir) BuildVm(IReadOnlyList<string>? declaredFields)
    {
        var dir = new TempDir();
        var config = new ConfigService();
        ReflectionHelpers.SetField(config, "_configDirectory", dir.Path);
        ReflectionHelpers.SetField(config, "_configFilePath", dir.Combine("config.json"));

        var vm = new ProviderUsageViewModel
        {
            ProviderId = ProviderId,
            Provider = new TooltipFakeProvider(declaredFields)
        };
        vm.AttachConfigService(config);
        return (vm, config, dir);
    }

    /// <summary>写入用户级 tooltip 字段配置（VisibleTooltipFields[chartId]）。</summary>
    private static void SaveUserTooltipFields(ConfigService config, List<string>? fields)
    {
        var customization = new AccountCustomization();
        customization.VisibleTooltipFields[LineChartId] = fields;
        config.SetCardChartConfiguration(ProviderId, customization);
    }

    [Fact]
    public void NoUserConfig_FallsBackToDeclaredFields()
    {
        var declared = new[] { "__date__", UsageFields.DailyTokenValue };
        var (vm, _, dir) = BuildVm(declared);
        using (dir)
        {
            var fields = vm.GetEffectiveTooltipFieldsForChart(LineChartId);
            Assert.NotNull(fields);
            Assert.Equal(declared, fields);
        }
    }

    [Fact]
    public void NoUserConfig_NoDeclaration_ReturnsNull_MeansNoFilter()
    {
        var (vm, _, dir) = BuildVm(declaredFields: null);
        using (dir)
        {
            var fields = vm.GetEffectiveTooltipFieldsForChart(LineChartId);
            Assert.Null(fields); // null = 不过滤（全部显示，向后兼容）
        }
    }

    [Fact]
    public void UserEmptyList_ReturnsEmpty_MeansHideTooltip()
    {
        var (vm, config, dir) = BuildVm(new[] { UsageFields.DailyTokenValue });
        using (dir)
        {
            SaveUserTooltipFields(config, new List<string>());
            var fields = vm.GetEffectiveTooltipFieldsForChart(LineChartId);
            Assert.NotNull(fields);
            Assert.Empty(fields); // 空集合 = 不显示 tooltip（问题3 语义）
        }
    }

    [Fact]
    public void UserNonEmptyList_OverridesDeclaration()
    {
        var (vm, config, dir) = BuildVm(new[] { UsageFields.DailyTokenValue });
        using (dir)
        {
            SaveUserTooltipFields(config, new List<string> { "__field_name__", "__date__" });
            var fields = vm.GetEffectiveTooltipFieldsForChart(LineChartId);
            Assert.NotNull(fields);
            Assert.Equal(new[] { "__field_name__", "__date__" }, fields);
        }
    }

    [Fact]
    public void EffectiveLineTooltipFields_LocatesLineChartDeclaration()
    {
        var declared = new[] { UsageFields.DailyTokenValue };
        var (vm, _, dir) = BuildVm(declared);
        using (dir)
        {
            Assert.Equal(declared, vm.EffectiveLineTooltipFields);
        }
    }
}
