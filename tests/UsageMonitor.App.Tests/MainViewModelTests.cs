using FluentAssertions;
using UsageMonitor.App.Tests._TestSupport;
using UsageMonitor.Core.Models;
using Xunit;

namespace UsageMonitor.App.Tests;

/// <summary>
/// app-layer-zero-test: MainViewModel 刷新命令路径覆盖。
/// <para>验证 RefreshCommand 成功路径更新状态栏属性、失败路径累计错误数。</para>
/// </summary>
public class MainViewModelRefreshTests
{
    // -----------------------------------------------------------------
    // 刷新成功：调用 IRefreshService.RefreshAllAsync 且更新状态属性
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshCommand_Success_Calls_RefreshAll_And_Updates_Status()
    {
        using var h = new MainViewModelHarness("prov-a");

        await h.ViewModel.RefreshCommand.ExecuteAsync(null);

        h.RefreshService.RefreshAllCallCount.Should().Be(1);
        h.ViewModel.RefreshProgress.Should().Be(100);
        h.ViewModel.LastRefreshTime.Should().NotBe("--:--:--");
        h.ViewModel.ErrorCount.Should().Be(0);
    }

    // -----------------------------------------------------------------
    // 刷新失败：异常被吞并累计 ErrorCount，进度不到 100
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshCommand_Failure_Increments_ErrorCount()
    {
        using var h = new MainViewModelHarness("prov-a");
        h.RefreshService.ThrowOnRefreshAll = true;

        await h.ViewModel.RefreshCommand.ExecuteAsync(null);

        h.RefreshService.RefreshAllCallCount.Should().Be(1);
        h.ViewModel.ErrorCount.Should().Be(1);
        h.ViewModel.RefreshProgress.Should().Be(0);
    }

    // -----------------------------------------------------------------
    // 连续两次刷新：每次都重置进度并重新调用刷新服务
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshCommand_Twice_Invokes_Service_Each_Time()
    {
        using var h = new MainViewModelHarness("prov-a");

        await h.ViewModel.RefreshCommand.ExecuteAsync(null);
        await h.ViewModel.RefreshCommand.ExecuteAsync(null);

        h.RefreshService.RefreshAllCallCount.Should().Be(2);
        h.ViewModel.RefreshProgress.Should().Be(100);
    }
}

/// <summary>
/// app-layer-zero-test: MainViewModel 配置联动路径覆盖。
/// <para>验证设置属性 setter 的持久化、钳制边界、IDataModule 联动与事件触发。</para>
/// </summary>
public class MainViewModelSettingsTests
{
    // -----------------------------------------------------------------
    // RefreshInterval：写配置并落盘
    // -----------------------------------------------------------------

    [Fact]
    public void RefreshInterval_Setter_Persists_To_Config()
    {
        using var h = new MainViewModelHarness();

        h.ViewModel.RefreshInterval = 120;

        h.ConfigService.Settings.RefreshIntervalSeconds.Should().Be(120);
        h.ViewModel.RefreshInterval.Should().Be(120);
    }

    // -----------------------------------------------------------------
    // HistoryPointCount：联动 IDataModule.MaxPoints；非法值回退 60
    // -----------------------------------------------------------------

    [Fact]
    public void HistoryPointCount_Setter_Syncs_DataModule_MaxPoints()
    {
        using var h = new MainViewModelHarness();

        h.ViewModel.HistoryPointCount = 30;

        h.ConfigService.Settings.HistoryPointCount.Should().Be(30);
        h.DataModule.Object.MaxPoints.Should().Be(30);
    }

    [Fact]
    public void HistoryPointCount_NonPositive_Falls_Back_To_60()
    {
        using var h = new MainViewModelHarness();

        h.ViewModel.HistoryPointCount = 0;

        h.ConfigService.Settings.HistoryPointCount.Should().Be(60);
        h.DataModule.Object.MaxPoints.Should().Be(60);
    }

    // -----------------------------------------------------------------
    // TrayTooltipHideDelayMs：钳制到 [100, 5000]
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(50, 100)]
    [InlineData(99999, 5000)]
    [InlineData(800, 800)]
    public void TrayTooltipHideDelayMs_Clamped_To_Valid_Range(int input, int expected)
    {
        using var h = new MainViewModelHarness();

        h.ViewModel.TrayTooltipHideDelayMs = input;

        h.ViewModel.TrayTooltipHideDelayMs.Should().Be(expected);
    }

    // -----------------------------------------------------------------
    // 触发区域：宽 / 高下限 10 的钳制
    // -----------------------------------------------------------------

    [Fact]
    public void TriggerRectWidth_Below_Minimum_Is_Clamped()
    {
        using var h = new MainViewModelHarness();

        h.ViewModel.TriggerRectWidth = 5;

        h.ViewModel.TriggerRectWidth.Should().BeGreaterOrEqualTo(10);
    }

    // -----------------------------------------------------------------
    // GlobalTaskbarMode：变更触发 TaskbarModeChanged 事件；同值 set 不重复触发
    // -----------------------------------------------------------------

    [Fact]
    public void GlobalTaskbarMode_Change_Raises_Event_And_Persists()
    {
        using var h = new MainViewModelHarness();
        var raised = 0;
        h.ViewModel.TaskbarModeChanged += (_, _) => raised++;

        var target = h.ViewModel.GlobalTaskbarMode == TaskbarDisplayMode.Text
            ? TaskbarDisplayMode.RingChart
            : TaskbarDisplayMode.Text;

        h.ViewModel.GlobalTaskbarMode = target;
        raised.Should().Be(1);
        h.ConfigService.Settings.GlobalTaskbarMode.Should().Be(target);

        // 同值再次 set：INPC 判重，事件不重复触发
        h.ViewModel.GlobalTaskbarMode = target;
        raised.Should().Be(1);
    }

    // -----------------------------------------------------------------
    // HasUnsavedChanges：INPC 判重（同值不触发 PropertyChanged）
    // -----------------------------------------------------------------

    [Fact]
    public void HasUnsavedChanges_Same_Value_Does_Not_Raise_PropertyChanged()
    {
        using var h = new MainViewModelHarness();
        var raised = 0;
        h.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(h.ViewModel.HasUnsavedChanges)) raised++;
        };

        h.ViewModel.HasUnsavedChanges = true;
        h.ViewModel.HasUnsavedChanges = true;

        raised.Should().Be(1);
    }

    // -----------------------------------------------------------------
    // SaveAllSettingsCommand：保存成功触发「保存后关闭」事件并清除未保存标记
    // -----------------------------------------------------------------

    [Fact]
    public void SaveAllSettingsCommand_Success_Requests_Close_With_Saved_True()
    {
        using var h = new MainViewModelHarness();
        bool? closedWithSaved = null;
        h.ViewModel.RequestCloseSettings += (_, saved) => closedWithSaved = saved;
        h.ViewModel.HasUnsavedChanges = true;

        h.ViewModel.SaveAllSettingsCommand.Execute(null);

        closedWithSaved.Should().BeTrue();
        h.ViewModel.HasUnsavedChanges.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // CancelSettingsCommand：取消关闭不写盘（saved=false）
    // -----------------------------------------------------------------

    [Fact]
    public void CancelSettingsCommand_Requests_Close_With_Saved_False()
    {
        using var h = new MainViewModelHarness();
        bool? closedWithSaved = null;
        h.ViewModel.RequestCloseSettings += (_, saved) => closedWithSaved = saved;

        h.ViewModel.CancelSettingsCommand.Execute(null);

        closedWithSaved.Should().BeFalse();
    }
}
