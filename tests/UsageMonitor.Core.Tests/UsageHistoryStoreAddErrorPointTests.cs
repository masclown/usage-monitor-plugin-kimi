using FluentAssertions;
using UsageMonitor.Core.Services;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-069-015（对应 F-23）：<see cref="UsageHistoryStore.AddErrorPoint"/> 的 lastPercent 行为覆盖。
/// <para>
/// F-23 将 AddErrorPoint 由 <c>queue.Last()</c>（O(n) LINQ 全遍历）改为 O(1) 读取
/// <c>HistoryEntry.LastPercent</c> 缓存。本测试验证：
/// <list type="number">
///   <item>空队列时错误点 UsagePercent = 0；</item>
///   <item>非空队列时错误点沿用上一次有效百分比；</item>
///   <item>连续错误点不改写 LastPercent（复用同一有效值）；</item>
///   <item>成功点会刷新 LastPercent，后续错误点采用新值。</item>
/// </list>
/// </para>
/// <para>注意：按项目规则本测试仅编写、不自动运行；请由维护者手动执行
/// <c>dotnet test tests/UsageMonitor.Core.Tests</c> 验证。</para>
/// </summary>
public class UsageHistoryStoreAddErrorPointTests
{
    /// <summary>空队列下 AddErrorPoint：错误点百分比应为默认 0。</summary>
    [Fact]
    public void AddErrorPoint_EmptyQueue_UsesZeroLastPercent()
    {
        var store = new UsageHistoryStore(); // 纯内存，不注入仓库
        store.AddErrorPoint("p1");

        var history = store.GetHistory("p1");
        history.Should().HaveCount(1);
        history[^1].IsError.Should().BeTrue();
        history[^1].UsagePercent.Should().Be(0);
    }

    /// <summary>非空队列下 AddErrorPoint：错误点应沿用上一次有效百分比。</summary>
    [Fact]
    public void AddErrorPoint_NonEmptyQueue_CarriesLastValidPercent()
    {
        var store = new UsageHistoryStore();
        store.AddPoint("p1", 50);   // LastPercent = 50
        store.AddErrorPoint("p1");  // 错误点沿用 50

        var history = store.GetHistory("p1");
        history.Should().HaveCount(2);
        history[^1].IsError.Should().BeTrue();
        history[^1].UsagePercent.Should().Be(50);
    }

    /// <summary>连续多次 AddErrorPoint：错误点不更新 LastPercent，均复用上一个有效值。</summary>
    [Fact]
    public void AddErrorPoint_ConsecutiveErrors_ReuseSameLastPercent()
    {
        var store = new UsageHistoryStore();
        store.AddPoint("p1", 30);
        store.AddErrorPoint("p1");
        store.AddErrorPoint("p1");

        var history = store.GetHistory("p1");
        history.Should().HaveCount(3);
        history[1].IsError.Should().BeTrue();
        history[1].UsagePercent.Should().Be(30);
        history[2].IsError.Should().BeTrue();
        history[2].UsagePercent.Should().Be(30);
    }

    /// <summary>成功点刷新 LastPercent 后，后续错误点采用新的有效值。</summary>
    [Fact]
    public void AddPoint_AfterError_UpdatesLastPercentForSubsequentErrors()
    {
        var store = new UsageHistoryStore();
        store.AddPoint("p1", 20);
        store.AddErrorPoint("p1");   // 沿用 20
        store.AddPoint("p1", 70);    // LastPercent -> 70
        store.AddErrorPoint("p1");   // 沿用 70

        var history = store.GetHistory("p1");
        history.Should().HaveCount(4);
        history[^1].IsError.Should().BeTrue();
        history[^1].UsagePercent.Should().Be(70);
    }
}
