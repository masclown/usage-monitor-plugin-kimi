using System.Collections.Generic;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// Stage B（宿主零 Provider 硬编码）：通用业务指纹测试。
/// <para>验证 <see cref="UsageHistoryRepository.BuildBusinessFingerprint"/> 泛化后：
/// 不特判任何 Provider、标量 extras 全量参与、集合型 extras 不参与。</para>
/// </summary>
public class BusinessFingerprintTests
{
    /// <summary>构造带指定 extras 的采样点。</summary>
    private static UsageInfo Make(string providerId, Dictionary<string, object> extra)
        => new()
        {
            ProviderId = providerId,
            ProviderName = providerId,
            IsSuccess = true,
            UsedTokens = 100,
            TotalTokens = 1000,
            Extra = extra
        };

    [Fact(DisplayName = "指纹与 ProviderId 无关：相同数据不同 Provider 指纹一致（无特判）")]
    public void Fingerprint_IsProviderAgnostic()
    {
        var extra = new Dictionary<string, object> { ["five_hour_used_percent"] = 3.0 };
        var a = UsageHistoryRepository.BuildBusinessFingerprint(Make("MiniMax", extra));
        var b = UsageHistoryRepository.BuildBusinessFingerprint(Make("Other", new Dictionary<string, object>(extra)));

        a.Should().Be(b);
    }

    [Fact(DisplayName = "标量 extras 变化 → 指纹变化")]
    public void Fingerprint_ChangesWhenScalarChanges()
    {
        var a = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["five_hour_used_percent"] = 3.0 }));
        var b = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["five_hour_used_percent"] = 4.0 }));

        a.Should().NotBe(b);
    }

    [Fact(DisplayName = "集合型 extras（列表/字典）不参与指纹")]
    public void Fingerprint_IgnoresCollections()
    {
        var baseline = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["remaining_credits"] = 5m }));
        var withCollections = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object>
            {
                ["remaining_credits"] = 5m,
                ["daily_token_value"] = new List<long> { 1, 2, 3 },
                ["model_daily"] = new List<Dictionary<string, object>>()
            }));

        withCollections.Should().Be(baseline);
    }

    [Fact(DisplayName = "extras 键顺序不影响指纹（键排序稳定）")]
    public void Fingerprint_IsKeyOrderStable()
    {
        var a = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["b_key"] = 1, ["a_key"] = 2 }));
        var b = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["a_key"] = 2, ["b_key"] = 1 }));

        a.Should().Be(b);
    }

    [Fact(DisplayName = "字符串型 extras 参与指纹（string 不被当作集合跳过）")]
    public void Fingerprint_IncludesStringScalars()
    {
        var a = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["subscription_tier"] = "Max" }));
        var b = UsageHistoryRepository.BuildBusinessFingerprint(
            Make("P", new Dictionary<string, object> { ["subscription_tier"] = "Pro" }));

        a.Should().NotBe(b);
    }
}
