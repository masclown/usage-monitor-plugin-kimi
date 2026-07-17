using System;
using System.Globalization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 强类型单位的抽象基类（REQ-005 SDK）。
/// <para>
/// 设计要点：货币 / Token / 百分比 / 积分 / 自定义等单位语义不同，<see cref="Quantity"/> 携带的
/// 单位类型在编译期就决定了 Format 行为，避免旧的 <c>Unit == "%"</c> 这类运行时字符串判定。
/// 任何"未识别单位"用 <see cref="UnknownUnit"/>；新单位只需继承 <see cref="UnitBase"/> 并实现
/// <see cref="Format(decimal)"/> 即可被现有 chart 控件直接使用。
/// </para>
/// </summary>
public abstract class UnitBase
{
    /// <summary>单位的稳定字符串键，写入 JSON / 跨 Provider 通信时使用。</summary>
    public abstract string Key { get; }

    /// <summary>人类可读的展示名（"美元"、"Token"、"积分"）。</summary>
    public abstract string DisplayName { get; }

    /// <summary>把数值格式化为带单位的展示字符串（不包含 DisplaySuffix，由 <see cref="Quantity"/> 拼装）。</summary>
    /// <param name="value">待格式化的数值。</param>
    public abstract string Format(decimal value);
}

/// <summary>单位类型未指定（兼容旧字段 <c>Unit</c> 缺省值）。</summary>
public sealed class UnknownUnit : UnitBase
{
    /// <inheritdoc />
    public override string Key => "Unspecified";

    /// <inheritdoc />
    public override string DisplayName => "";

    /// <inheritdoc />
    public override string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>货币单位（USD / CNY 等）。</summary>
public sealed class CurrencyUnit : UnitBase
{
    /// <summary>ISO 4217 货币代码（小写，序列化为 USD/CNY 等）。</summary>
    public string Code { get; }

    /// <summary>货币构造。</summary>
    public CurrencyUnit(string code) => Code = (code ?? "").Trim().ToUpperInvariant();

    /// <inheritdoc />
    public override string Key => Code;

    /// <inheritdoc />
    public override string DisplayName => Code switch
    {
        "USD" => "美元",
        "CNY" => "人民币",
        "EUR" => "欧元",
        "JPY" => "日元",
        _ => Code
    };

    /// <inheritdoc />
    public override string Format(decimal value) => $"{value.ToString("F2", CultureInfo.InvariantCulture)} {Code}";

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CurrencyUnit c && c.Code == Code;

    /// <inheritdoc />
    public override int GetHashCode() => Code.GetHashCode();
}

/// <summary>Token 计数单位（请求数 / Token 数）。</summary>
public sealed class TokenUnit : UnitBase
{
    /// <summary>Token 类型标记（如 "token"、"request"），缺省 "token"。</summary>
    public string SubKey { get; }

    /// <summary>Token 构造。</summary>
    public TokenUnit(string subKey = "token") => SubKey = (subKey ?? "token").Trim().ToLowerInvariant();

    /// <inheritdoc />
    public override string Key => SubKey;

    /// <inheritdoc />
    public override string DisplayName => SubKey;

    /// <inheritdoc />
    public override string Format(decimal value)
    {
        // 1.5K / 1.5M / 1.5B 风格紧凑格式
        var d = (double)value;
        if (d >= 1_000_000_000d) return $"{d / 1_000_000_000d:F1}B {SubKey}";
        if (d >= 1_000_000d) return $"{d / 1_000_000d:F1}M {SubKey}";
        if (d >= 1_000d) return $"{d / 1_000d:F1}K {SubKey}";
        return $"{value.ToString(CultureInfo.InvariantCulture)} {SubKey}";
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TokenUnit t && t.SubKey == SubKey;

    /// <inheritdoc />
    public override int GetHashCode() => SubKey.GetHashCode();
}

/// <summary>百分比单位（0~100）。</summary>
public sealed class PercentUnit : UnitBase
{
    /// <inheritdoc />
    public override string Key => "%";

    /// <inheritdoc />
    public override string DisplayName => "百分比";

    /// <inheritdoc />
    public override string Format(decimal value) => $"{value.ToString("F0", CultureInfo.InvariantCulture)}%";

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PercentUnit;

    /// <inheritdoc />
    public override int GetHashCode() => "%".GetHashCode();
}

/// <summary>积分 / 点数单位（Plugin 内部货币化资产）。</summary>
public sealed class CreditUnit : UnitBase
{
    /// <summary>积分名（如 "credits" / "点"），缺省 "credits"。</summary>
    public string Name { get; }

    /// <summary>积分构造。</summary>
    public CreditUnit(string name = "credits") => Name = (name ?? "credits").Trim();

    /// <inheritdoc />
    public override string Key => Name;

    /// <inheritdoc />
    public override string DisplayName => Name;

    /// <inheritdoc />
    public override string Format(decimal value)
        => $"{value.ToString("F0", CultureInfo.InvariantCulture)} {Name}";

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CreditUnit c && c.Name == Name;

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// 强类型"数量 = 值 + 单位 + 可选后缀"，取代旧 <c>UsedAmount + Unit</c> 双字段语义混淆（REQ-005 SDK）。
/// </summary>
/// <param name="Value">数值（decimal，足够覆盖货币 / 积分；Token 数超 2^31 时建议改用 long 字段）。</param>
/// <param name="Unit">单位实例（不同子类型不可隐式转换）。</param>
/// <param name="DisplaySuffix">附加在单位之后的小尾巴（缺省 null / ""）。</param>
public readonly record struct Quantity(decimal Value, UnitBase Unit, string? DisplaySuffix = null)
{
    /// <summary>创建指定单位的 0 值。</summary>
    public static Quantity Zero(UnitBase unit) => new(0m, unit);

    /// <summary>把 Quantity 格式化为带单位的展示串（值 + 单位 + DisplaySuffix）。</summary>
    public string Format()
        => (Unit?.Format(Value) ?? Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
           + (DisplaySuffix ?? "");
}