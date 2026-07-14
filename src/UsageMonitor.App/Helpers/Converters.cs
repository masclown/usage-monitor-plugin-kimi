using System.Globalization;
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
