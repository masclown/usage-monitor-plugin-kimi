using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Services;
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
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeProvidersAsync();
            // req-041：加载完成后应用默认排序（日期倒序）
            ApplyDefaultSort();
        };
    }

    /// <summary>
    /// "刷新"按钮：手动重新查询一次
    /// </summary>
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDataAsync();
    }

    /// <summary>
    /// “导出 CSV”按钮：弹 SaveFileDialog 写当前 DetailRows 内容（req-069 i18n：文案经 I18n.T 解析）。
    /// </summary>
    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = I18n.T(I18nKeys.History_Export_DialogTitle),
            Filter = I18n.T(I18nKeys.History_Export_DialogFilter),
            FileName = I18n.T(I18nKeys.History_Export_FileNameFormat, DateTime.Now)
        };
        if (dlg.ShowDialog(this) != true) return;
        var ok = ViewModel.SaveCsvToFile(dlg.FileName);
        MessageBox.Show(this,
            ok ? I18n.T(I18nKeys.History_Export_SuccessMessageFormat, dlg.FileName)
               : I18n.T(I18nKeys.History_Export_FailMessage),
            ok ? I18n.T(I18nKeys.History_Export_SuccessTitle) : I18n.T(I18nKeys.History_Export_FailTitle),
            MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>
    /// req-041：应用默认排序（日期倒序）。
    /// </summary>
    private void ApplyDefaultSort()
    {
        if (DetailGrid.ItemsSource == null) return;
        var dateColumn = DetailGrid.Columns.FirstOrDefault(c => c.SortMemberPath == "Day");
        if (dateColumn != null)
        {
            dateColumn.SortDirection = ListSortDirection.Descending;
        }
        var collectionView = CollectionViewSource.GetDefaultView(DetailGrid.ItemsSource);
        if (collectionView != null)
        {
            collectionView.SortDescriptions.Clear();
            collectionView.SortDescriptions.Add(new SortDescription("Day", ListSortDirection.Descending));
            collectionView.Refresh();
        }
    }

    /// <summary>
    /// req-041：DataGrid 排序事件处理。更新列的排序方向指示器。
    /// </summary>
    private void OnDataGridSorting(object sender, DataGridSortingEventArgs e)
    {
        // 清除其他列的排序方向
        foreach (var col in DetailGrid.Columns)
        {
            if (col != e.Column) col.SortDirection = null;
        }

        // 切换当前列的排序方向
        var newDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        e.Column.SortDirection = newDirection;

        // 应用排序
        var collectionView = CollectionViewSource.GetDefaultView(DetailGrid.ItemsSource);
        if (collectionView != null)
        {
            collectionView.SortDescriptions.Clear();
            collectionView.SortDescriptions.Add(new SortDescription(e.Column.SortMemberPath, newDirection));
            collectionView.Refresh();
        }

        e.Handled = true;
    }
}
