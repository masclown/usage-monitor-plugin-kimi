using System.Collections.Concurrent;
using FluentAssertions;
using UsageMonitor.Core.Models;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-057-002 / req-059-001: ProviderConfig.Values 已改为 ConcurrentDictionary，
/// 验证并发读写安全 + 基本 Get/Set 语义。
/// </summary>
public class ProviderConfigTests
{
    [Fact]
    public void Values_Is_ConcurrentDictionary()
    {
        // 验证需求：ProviderConfig.Values 改为线程安全集合。
        var config = new ProviderConfig();
        config.Values.Should().BeOfType<ConcurrentDictionary<string, string>>();
    }

    [Fact]
    public void GetValue_Returns_Null_When_Key_Missing()
    {
        var config = new ProviderConfig { ProviderId = "test" };
        config.GetValue("missing").Should().BeNull();
    }

    [Fact]
    public void SetValue_And_GetValue_Roundtrip()
    {
        var config = new ProviderConfig { ProviderId = "test" };
        config.SetValue("ApiKey", "sk-test-123");
        config.GetValue("ApiKey").Should().Be("sk-test-123");
    }

    [Fact]
    public void SetValue_Overwrites_Existing_Key()
    {
        var config = new ProviderConfig { ProviderId = "test" };
        config.SetValue("ApiKey", "v1");
        config.SetValue("ApiKey", "v2");
        config.GetValue("ApiKey").Should().Be("v2");
    }

    [Fact]
    public async Task Concurrent_Read_Write_Does_Not_Throw()
    {
        // req-057-002: 多线程并发读写 Values 不应抛 InvalidOperationException 或损坏结构。
        var config = new ProviderConfig { ProviderId = "test" };
        const int writesPerThread = 1000;
        const int readerCount = 4;
        const int writerCount = 4;

        var startSignal = new ManualResetEventSlim(false);

        var writers = Enumerable.Range(0, writerCount).Select(t => Task.Run(() =>
        {
            startSignal.Wait();
            for (int i = 0; i < writesPerThread; i++)
            {
                config.SetValue($"key-{t}-{i}", $"value-{t}-{i}");
            }
        })).ToArray();

        var readers = Enumerable.Range(0, readerCount).Select(t => Task.Run(() =>
        {
            startSignal.Wait();
            for (int i = 0; i < writesPerThread; i++)
            {
                _ = config.GetValue($"key-{t % writerCount}-{i}");
            }
        })).ToArray();

        // 同时放行所有 writer 和 reader
        startSignal.Set();

        // 应不抛异常
        await Task.WhenAll(writers);
        await Task.WhenAll(readers);

        // 写过的 key 应能读到（非并发安全实现会丢失写入或读到损坏值）
        config.GetValue("key-0-0").Should().Be("value-0-0");
        config.GetValue($"key-{writerCount - 1}-{writesPerThread - 1}")
            .Should().Be($"value-{writerCount - 1}-{writesPerThread - 1}");
    }

    [Fact]
    public void Validate_All_Required_Fields_Filled_Returns_True()
    {
        var config = new ProviderConfig { ProviderId = "test" };
        config.SetValue("ApiKey", "sk-test");
        var fields = new[]
        {
            new ConfigField("ApiKey", "API Key", ConfigFieldType.Text, isRequired: true),
            new ConfigField("Region", "Region", ConfigFieldType.Text, isRequired: false, defaultValue: "CN"),
        };
        config.Validate(fields).Should().BeTrue();
    }

    [Fact]
    public void Validate_Required_Field_Missing_Returns_False()
    {
        var config = new ProviderConfig { ProviderId = "test" };
        // 没填 ApiKey
        var fields = new[]
        {
            new ConfigField("ApiKey", "API Key", ConfigFieldType.Text, isRequired: true),
        };
        config.Validate(fields).Should().BeFalse();
    }

    [Fact]
    public void Validate_Required_Field_Whitespace_Returns_False()
    {
        var config = new ProviderConfig { ProviderId = "test" };
        config.SetValue("ApiKey", "   ");
        var fields = new[]
        {
            new ConfigField("ApiKey", "API Key", ConfigFieldType.Text, isRequired: true),
        };
        config.Validate(fields).Should().BeFalse();
    }
}
