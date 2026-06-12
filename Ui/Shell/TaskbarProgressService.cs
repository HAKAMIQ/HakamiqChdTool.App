using HakamiqChdTool.App.ViewModels;
using System;
using System.Windows.Shell;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class TaskbarProgressService : ITaskbarProgressService
{
    private readonly TaskbarSessionProgressViewModel _viewModel;

    public TaskbarProgressService(TaskbarSessionProgressViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public void SetProgress(double value, TaskbarProgressState state)
    {
        _viewModel.NormalizedProgress = Math.Clamp(value, 0, 1);
        _viewModel.State = MapState(state);
    }

    public void Clear()
    {
        _viewModel.NormalizedProgress = 0;
        _viewModel.State = TaskbarItemProgressState.None;
    }

    private static TaskbarItemProgressState MapState(TaskbarProgressState state)
    {
        return state switch
        {
            TaskbarProgressState.None => TaskbarItemProgressState.None,
            TaskbarProgressState.Indeterminate => TaskbarItemProgressState.Indeterminate,
            TaskbarProgressState.Normal => TaskbarItemProgressState.Normal,
            TaskbarProgressState.Paused => TaskbarItemProgressState.Paused,
            TaskbarProgressState.Error => TaskbarItemProgressState.Error,
            _ => TaskbarItemProgressState.None
        };
    }
}
