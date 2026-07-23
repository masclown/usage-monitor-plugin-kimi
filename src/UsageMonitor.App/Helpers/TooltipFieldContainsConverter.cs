using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
// req-105：项目同时启用 WPF + WinForms 导致 Binding 命名空间歧义，统一用 WPF 版本。
using Binding = System.Windows.Data.Binding;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-105：双向转换器——Tooltip 字段 CheckBox ↔ CardChartConfigItem.TooltipFields 集合。
/// <para>用法：<c>IsChecked="{Binding ., RelativeSource=..., Converter={StaticResource TooltipFieldContainsConverter}, ConverterParameter={Binding FieldName}}"</c>
/// （相对源 = 包含 ObservableCollection&lt;string&gt; 的 CardChartConfigItem；ConverterParameter = 字段名）。</para>
/// </summary>
public sealed class TooltipFieldContainsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ObservableCollection<string> fields || parameter is not string fieldName)
            return false;
        return fields.Contains(fieldName);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isChecked || parameter is not string fieldName)
            return Binding.DoNothing;
        // value 来自 TooltipFieldCatalog 静态项的 IsChecked（不可写回），
        // 真实集合通过 ToggleTooltipFieldCommand 维护——此分支不会触发。
        return Binding.DoNothing;
    }
}