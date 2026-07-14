using System.Windows;
using System.Windows.Controls;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Views;

/// <summary>
/// 任务栏窗口中各 Provider 项的模板选择器
/// - 根据 ProviderUsageViewModel.DisplayMode 选择对应 DataTemplate
/// - 模板资源需要在容器（TaskbarWindow / App.xaml）中以 x:Key 注册：
///     TaskbarTextTemplate, TaskbarLineChartTemplate, TaskbarRingChartTemplate
/// </summary>
public class TaskbarItemTemplateSelector : DataTemplateSelector
{
    /// <summary>文字模式模板（依赖属性绑定到外部资源）</summary>
    public DataTemplate? TextTemplate { get; set; }

    /// <summary>折线图模式模板</summary>
    public DataTemplate? LineChartTemplate { get; set; }

    /// <summary>圆环图模式模板</summary>
    public DataTemplate? RingChartTemplate { get; set; }

    /// <summary>
    /// 根据数据项的 DisplayMode 选择模板
    /// </summary>
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not ProviderUsageViewModel vm) return TextTemplate;
        return vm.DisplayMode switch
        {
            TaskbarDisplayMode.MiniLineChart => LineChartTemplate ?? TextTemplate,
            TaskbarDisplayMode.RingChart => RingChartTemplate ?? TextTemplate,
            _ => TextTemplate
        };
    }
}
