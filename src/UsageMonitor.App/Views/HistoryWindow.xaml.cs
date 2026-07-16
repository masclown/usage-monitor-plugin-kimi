using System.Windows;
using Microsoft.Win32;
using UsageMonitor.App.ViewModels;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace UsageMonitor.App.Views;

/// <summary>
/// 历史查看窗口。
/// <para>
/// 复用项目内的 StaticResource、Converters 与图表控件（HistoryLineChartControl / YearHeatMapControl / RingChartControl）。
/// 仅负责 UI 行为：刷新按钮、导出 CSV、Dialog 关闭；数据逻辑全部走 HistoryViewModel。
/// </para>
/// </summary>
public partial class HistoryWindow : Window
{
    /// <summary>历史窗口主 VM（注入）</summary>
    public HistoryViewModel ViewModel { get; }

    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        // 初次进入时一次性加载 Provider 列表与默认数据
        Loaded += async (_, _) => await ViewModel.InitializeProvidersAsync();
    }

    /// <summary>
    /// "刷新"按钮：手动重新查询一次
    /// </summary>
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDataAsync();
    }

    /// <summary>
    /// "导出 CSV"按钮：弹 SaveFileDialog 写当前 DetailRows 内容
    /// </summary>
    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出历史为 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = $"UsageMonitor-历史-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dlg.ShowDialog(this) != true) return;
        var ok = ViewModel.SaveCsvToFile(dlg.FileName);
        MessageBox.Show(this,
            ok ? $"已导出到 {dlg.FileName}" : "导出失败，请查看 logs/UsageMonitor-*.log",
            ok ? "导出成功" : "导出失败",
            MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
