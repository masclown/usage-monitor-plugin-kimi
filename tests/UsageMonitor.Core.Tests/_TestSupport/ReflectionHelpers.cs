using System.Reflection;
using Xunit;

namespace UsageMonitor.Core.Tests._TestSupport;

/// <summary>
/// req-059-001: 反射辅助工具。
/// <para>
/// 用于访问被测对象的 <c>private readonly</c> 字段（如 <c>ConfigService._configDirectory</c>、
/// <c>_ioLock</c>、<c>_settings</c>），避免在生产代码中加入
/// <c>InternalsVisibleTo</c> 或 <c>internal</c> 访问修饰符——这是侵入最小的测试策略。
/// </para>
/// </summary>
public static class ReflectionHelpers
{
    /// <summary>读取实例的私有/非公开字段值。</summary>
    public static T? GetField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(field);
        return (T?)field!.GetValue(instance);
    }

    /// <summary>设置实例的私有/非公开字段值（用于把测试临时目录注入 _configDirectory 等）。</summary>
    public static void SetField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    /// <summary>读取静态私有/非公开字段值。</summary>
    public static T? GetStaticField<T>(Type type, string fieldName)
    {
        var field = type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(field);
        return (T?)field!.GetValue(null);
    }
}
