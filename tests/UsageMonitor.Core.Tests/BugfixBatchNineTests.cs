using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Services.Data;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// 九项缺陷修复批次的 Core 层测试：
/// <para>① 问题1/9：DataModule.GetLatestFieldsAsync 透传（纯内存模式空字典 / SQLite 模式读写往返）；
/// ② 问题1：json 数组字段（如 daily_token_values）经 usage_field_versions 往返后还原为可枚举列表；
/// ③ 问题8：AccountCustomization.MiniTooltipFields 深拷贝独立；
/// ④ 问题7：MiniText 图表类型支持 DataGroup 切片器（ChartKindSpec 校验通过）。</para>
/// </summary>
public class BugfixBatchNineTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"um_test_{Guid.NewGuid():N}.db");

    /// <summary>删除临时 SQLite 文件（含 wal/shm 伴生文件）。</summary>
    private static void TryDelete(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* 临时文件，忽略清理失败 */ }
        }
    }

    /// <summary>问题1/9：纯内存模式（无仓库）下 GetLatestFieldsAsync 返回空字典且不抛异常。</summary>
    [Fact]
    public async Task GetLatestFieldsAsync_MemoryMode_ReturnsEmpty()
    {
        var module = new DataModule();
        var fields = await module.GetLatestFieldsAsync("MiniMax", "default");
        fields.Should().NotBeNull();
        fields.Should().BeEmpty();
    }

    /// <summary>问题1/9：SQLite 模式下 GetLatestFieldsAsync 透传仓库——写入字段版本后可按账号读回。</summary>
    [Fact]
    public async Task GetLatestFieldsAsync_WithRepository_ReadsBackSavedFields()
    {
        var db = TempDb();
        try
        {
            var repo = new UsageHistoryRepository(db);
            repo.EnsureSchema();
            await repo.SaveIncrementalAsync("MiniMax", "acctA",
                new[] { new FieldChange("five_hour_used_percent", null, 42.5, ChangeType.Added) });

            var module = new DataModule(repo);
            var fields = await module.GetLatestFieldsAsync("MiniMax", "acctA");
            fields.Should().ContainKey("five_hour_used_percent");
            Convert.ToDouble(fields["five_hour_used_percent"]).Should().Be(42.5);

            // 空账号回退 default（无数据 → 空字典）
            (await module.GetLatestFieldsAsync("MiniMax", "other")).Should().BeEmpty();
        }
        finally { TryDelete(db); }
    }

    /// <summary>问题1：json 数组字段（daily_token_values）经字段版本表往返后应还原为可枚举列表（非原始 JSON 字符串）。</summary>
    [Fact]
    public async Task JsonArrayField_RoundTrips_AsEnumerableList()
    {
        var db = TempDb();
        try
        {
            var repo = new UsageHistoryRepository(db);
            repo.EnsureSchema();
            var values = new List<long> { 100, 200, 300 };
            await repo.SaveIncrementalAsync("MiniMax", "default",
                new[] { new FieldChange("daily_token_values", null, values, ChangeType.Added) });

            var fields = await repo.GetLatestFieldsAsync("MiniMax", "default");
            fields.Should().ContainKey("daily_token_values");
            var restored = fields["daily_token_values"];
            restored.Should().NotBeOfType<string>("json 数组应还原为列表而非原始 JSON 字符串");
            var list = ((System.Collections.IEnumerable)restored).Cast<object>()
                .Select(x => Convert.ToInt64(x)).ToList();
            list.Should().Equal(100L, 200L, 300L);
        }
        finally { TryDelete(db); }
    }

    /// <summary>问题8：MiniTooltipFields 深拷贝——克隆后修改原对象不影响副本。</summary>
    [Fact]
    public void MiniTooltipFields_Clone_IsDeepCopy()
    {
        var source = new AccountCustomization();
        source.MiniTooltipFields["mm.mini.ring"] = new List<string> { "__provider_name__", "five_hour_used_percent" };
        source.MiniTooltipFields["mm.mini.text"] = null;

        var clone = source.Clone();
        clone.MiniTooltipFields.Should().HaveCount(2);
        clone.MiniTooltipFields["mm.mini.text"].Should().BeNull();

        // 修改原始对象不影响克隆
        source.MiniTooltipFields["mm.mini.ring"]!.Add("weekly_used_percent");
        clone.MiniTooltipFields["mm.mini.ring"].Should().Equal("__provider_name__", "five_hour_used_percent");
    }

    /// <summary>问题7：MiniText 图表声明 DataGroup 切片器 + 百分比 Value 字段应通过 ChartKindSpec 校验。</summary>
    [Fact]
    public void MiniText_WithDataGroupSlicer_PassesValidation()
    {
        var chart = new ChartDeclaration
        {
            ChartId = "mm.mini.text",
            Kind = DeclarativeChartKind.MiniText,
            Slicer = new SlicerSpec { Mode = SlicerMode.DataGroup, Default = "mm.taskbar.5h" },
            DataGroups = new[]
            {
                new DataGroup
                {
                    Id = "mm.taskbar.5h",
                    Fields = new[] { new FieldReference { FieldName = "five_hour_used_percent", Role = FieldRole.Value } }
                },
                new DataGroup
                {
                    Id = "mm.taskbar.weekly",
                    Fields = new[] { new FieldReference { FieldName = "weekly_used_percent", Role = FieldRole.Value } }
                }
            }
        };

        var errors = ChartKindSpecRegistry.Validate(chart);
        errors.Should().BeEmpty();
    }
}
