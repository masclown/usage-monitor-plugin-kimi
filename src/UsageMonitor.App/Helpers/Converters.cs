using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 布尔值转可见性转换器
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        if (parameter?.ToString() == "Invert")
            boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// 布尔值转透明度转换器（启用=1.0，禁用=0.5）
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : 0.5;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (double)value > 0.75;
}

/// <summary>
/// 错误状态颜色转换器
/// </summary>
public class ErrorColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isError = value is bool b && b;
        return isError
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38))   // 红色
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // 灰色
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 百分比宽度转换器（用于进度条，将百分比转换为固定像素宽度）
/// </summary>
public class PercentageWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0.0;
        if (values[0] is not double percentage || values[1] is not double totalWidth)
            return 0.0;
        return Math.Max(0, Math.Min(totalWidth, totalWidth * percentage / 100.0));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 百分比转宽度转换器（单值转换器，通过 ConverterParameter 指定最大宽度）
/// </summary>
public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double percentage) return 0.0;
        double maxWidth = 700;
        if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            maxWidth = parsed;
        return Math.Max(0, maxWidth * percentage / 100.0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 字符串转可见性转换器：非空且非 null 时显示，否则折叠。
/// 用于可选附加信息的折叠面板（如余额详情）。
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        return !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 渲染需求（RenderKinds）名字为参数的情况下判断字符串集合是否包含。供通用卡片 XAML 控制是否呈现某个段落。
/// 用法：Visibility="{Binding RenderKinds, Converter={StaticResource RenderKindToVisibility}, ConverterParameter=primaryBar}"
/// </summary>
public class RenderKindToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var kind = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(kind) || value is not IEnumerable enumerable)
            return Visibility.Collapsed;

        foreach (var item in enumerable)
        {
            if (item == null) continue;
            if (string.Equals(item.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 订阅状态转为默认/已订阅文本。在 XAML 里使用：Text="{Binding IsSubscriptionActive, Converter={StaticResource SubscriptionTitleFallback}}"
/// 传 ConverterParameter=DefaultText 用于未订阅显示。
/// </summary>
public class SubscriptionTitleActiveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 多 Visibility 与运算：只要有一个输入不为 Visible，结果就是 Collapsed。
/// 用于把“插件声明的 render_kind”与“用户设置的开关”合并成单一 Visible 状态。
/// </summary>
public class MultiVisibilityAndConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null) return Visibility.Collapsed;
        foreach (var v in values)
        {
            if (v is Visibility vis && vis != Visibility.Visible)
                return Visibility.Collapsed;
            // 如果某个 binding 输出 null（未求值）也折叠，避免闪一帧可见。
            if (v == null) return Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 多 Visibility 或运算：只要有一个输入是 Visible，结果就是 Visible。
/// 用于“容器可见性 = 多段独立开关的合取”场景：避免单个开关关闭把整个容器收起。
/// </summary>
public class MultiVisibilityOrConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null) return Visibility.Collapsed;
        foreach (var v in values)
        {
            if (v is Visibility vis && vis == Visibility.Visible)
                return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 按已用百分比返回分档画笔（进度条 / 托盘 / 热力图单元格等“按比例选色”）。
/// 档位（阈值 + 颜色）统一由 <see cref="UsageTierScale"/> 定义——需要增减档位或调整阈值 / 配色时改那里即可，此处无需变动。
/// ConverterParameter 可显式传 "low|mid|high" 指定语义档位，否则按百分比自动分档。
/// </summary>
public class PercentToBrushConverter : IValueConverter
{
    /// <summary>
    /// 把已用百分比映射为分档画笔；或按 "low|mid|high" 显式取档（按 Tiers 升序的首/中/末）。
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 显式档位：low / mid / high 按当前 Tiers 升序索引取。
        if (parameter is string level && !string.IsNullOrWhiteSpace(level))
        {
            var tier = UsageTierScale.ResolveByLevel(level);
            if (tier != null)
                return new System.Windows.Media.SolidColorBrush(tier.Color);
        }

        // 按已用百分比（0-100）自动分档。
        var pct = value is double d ? d : 0.0;
        return UsageTierScale.ResolveBrush(pct);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 任务栏“文字”模式剩余额度/百分比前缀转换器。
/// 在剩余数值前加“剩余”二字，明确该数字是“剩余”而非“已用”，避免与折线图/圆环图的已用%混淆。
/// 无数据（null/空白/"--"）时原样返回，避免出现无意义的“剩余--”。
/// </summary>
public class RemainingPrefixConverter : IValueConverter
{
    /// <summary>
    /// 将剩余文本转换为带“剩余”前缀的显示文案。
    /// </summary>
    /// <param name="value">绑定的剩余文本（如 "45%"、"12.50 credits" 或占位 "--"）。</param>
    /// <returns>有数据时返回 "剩余"+原文；无数据（空白或占位 "--"）时原样返回。</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s) || s == "--") return s ?? string.Empty;
        return "剩余" + s;
    }

    /// <summary>不支持反向转换（仅用于单向显示）。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 枚举值转可见性：当枚举值名称与 ConverterParameter 匹配时返回 Visible，否则 Collapsed。
/// 用法：Visibility="{Binding CardChartKind, Converter={StaticResource EnumToVisibility}, ConverterParameter=Bar}"
/// </summary>
public class EnumToVisibilityConverter : IValueConverter
{
    /// <summary>枚举名与参数一致时可见，否则折叠。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>不支持反向转换。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 判断卡片图表类型集合（IReadOnlyList&lt;CardChartKind&gt; 等可枚举）是否包含 ConverterParameter 指定的图表类型：
/// 包含则返回 Visible，否则 Collapsed。用于主窗口卡片按用户「多选」的图表集合叠加显示对应图表控件。
/// 用法：Visibility="{Binding CardChartKinds, Converter={StaticResource CardChartKindsContains}, ConverterParameter=Line}"
/// </summary>
public class CardChartKindsContainsConverter : IValueConverter
{
    /// <summary>集合内任一元素名称与参数一致时可见，否则折叠（按名称做大小写不敏感匹配，无需引用枚举类型）。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var kind = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(kind) || value is not IEnumerable enumerable)
            return Visibility.Collapsed;

        foreach (var item in enumerable)
        {
            if (item == null) continue;
            if (string.Equals(item.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    /// <summary>不支持反向转换。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// REQ-003：环形图中心数字 metric 键（字符串）转人类可读提示。
/// 用于设置窗口中 ListBox 显示：内部存储 "Percent" / "Credits" / "WeeklyLimit" /
/// "RemainingQuota" / "ApiTokenUsed" 等机器键，绑定后转为中文提示。未识别键原样回退。
/// </summary>
public class RingMetricKeyToDisplayConverter : IValueConverter
{
    /// <summary>内嵌映射表：与 <see cref="UsageMonitor.Core.Models.RingChartMetricKeys"/> 字符串常量一一对应。未知键原样返回。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> Map = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Percent"] = "已用百分比（默认）",
        ["Credits"] = "积分余额",
        ["WeeklyLimit"] = "周限额剩余",
        ["RemainingQuota"] = "剩余用量（金额 / Token）",
        ["ApiTokenUsed"] = "已用 Token 数",
    };

    /// <summary>将 metric 键转为中文说明。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        return Map.TryGetValue(key, out var text) ? text : key;
    }

    /// <summary>不支持反向转换（设置中的 ListBox 只读提示）。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

