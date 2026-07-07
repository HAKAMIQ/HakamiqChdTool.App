using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using System;
using System.Windows.Shell;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void UpdateTaskbarProgress(
        QueueUiSnapshot aggregate,
        bool hasTasks,
        FooterProgressState footerProgress)
    {
        if (!hasTasks)
        {
            _taskbarSessionProgress.State = TaskbarItemProgressState.None;
            _taskbarSessionProgress.NormalizedProgress = 0;
            return;
        }

        double normalizedProgress = NormalizeProgressForTaskbar(footerProgress.Percent);

        if (_coordinator.IsProcessing)
        {
            _taskbarSessionProgress.State = _coordinator.CancellationRequested
                ? TaskbarItemProgressState.Paused
                : footerProgress.IsIndeterminate
                    ? TaskbarItemProgressState.Indeterminate
                    : TaskbarItemProgressState.Normal;

            _taskbarSessionProgress.NormalizedProgress = footerProgress.IsIndeterminate
                ? 0d
                : normalizedProgress;

            return;
        }

        if (aggregate.HasFailedRows)
        {
            _taskbarSessionProgress.State = TaskbarItemProgressState.Error;
            _taskbarSessionProgress.NormalizedProgress = normalizedProgress;
            return;
        }

        _taskbarSessionProgress.State = TaskbarItemProgressState.None;
        _taskbarSessionProgress.NormalizedProgress = 0;
    }

    private static double NormalizeProgressForTaskbar(double progressPercent)
    {
        return Math.Clamp(progressPercent, 0, 100) / 100.0;
    }
}