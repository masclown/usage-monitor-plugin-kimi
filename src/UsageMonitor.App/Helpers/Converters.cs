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
/// 按百分比阈值返回画笔（用于进度条按比例选色）。
/// <list type="bullet">
///   <item>小于警告阈值 (60)：绿色 #FF00B42A。</item>
///   <item>小于危险阈值 (85)：橙色 #FFF77234。</item>
///   <item>达到危险阈值 (含)：红色 #FFF14124。</item>
/// </list>
/// ConverterParameter 可以传 "low|mid|high" 三个具体等级，否则用阈值推断。
/// </summary>
public class PercentToBrushConverter : IValueConverter
{
    // 回退画笔（主题资源缺失时使用），与 Tokens.xaml 中 UsageLow/Mid/High 保持一致。
    private static readonly SolidColorBrush LowFallback = Freeze("#FF22C55E");
    private static readonly SolidColorBrush MidFallback = Freeze("#FFF59E0B");
    private static readonly SolidColorBrush HighFallback = Freeze("#FFEF4444");

    /// <summary>创建并冻结一个 SolidColorBrush（提升渲染性能）。</summary>
    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>按语义键从应用资源取当前主题画笔，缺失时回退到内置色。</summary>
    private static System.Windows.Media.Brush Lookup(string key, SolidColorBrush fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush ?? fallback;

    /// <summary>
    /// 把已用百分比映射为三档语义画笔（低绿 / 中橙 / 高红）。
    /// ConverterParameter 可显式传 "low|mid|high" 指定等级，否则按 60 / 85 阈值推断。
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double pct)
        {
            if (parameter is string level)
            {
                return level switch
                {
                    "low" => Lookup("UsageLowBrush", LowFallback),
                    "mid" => Lookup("UsageMidBrush", MidFallback),
                    "high" => Lookup("UsageHighBrush", HighFallback),
                    _ => Lookup("UsageLowBrush", LowFallback),
                };
            }

            if (pct >= 85.0) return Lookup("UsageHighBrush", HighFallback);
            if (pct >= 60.0) return Lookup("UsageMidBrush", MidFallback);
            return Lookup("UsageLowBrush", LowFallback);
        }
        return Lookup("UsageLowBrush", LowFallback);
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

