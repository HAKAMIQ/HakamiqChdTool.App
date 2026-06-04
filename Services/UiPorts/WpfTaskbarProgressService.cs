using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.UiPorts.Shell;
using System;
using System.Windows.Shell;

namespace HakamiqChdTool.App.Services.UiPorts;

public sealed class WpfTaskbarProgressService : ITaskbarProgressService
{
    private readonly TaskbarSessionProgressViewModel _viewModel;

    public WpfTaskbarProgressService(TaskbarSessionProgressViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public void SetProgress(double value, UiTaskbarProgressState state)
    {
        _viewModel.NormalizedProgress = Math.Clamp(value, 0, 1);
        _viewModel.State = MapState(state);
    }

    public void Clear()
    {
        _viewModel.NormalizedProgress = 0;
        _viewModel.State = TaskbarItemProgressState.None;
    }

    private static TaskbarItemProgressState MapState(UiTaskbarProgressState state)
    {
        return state switch
        {
            UiTaskbarProgressState.None => TaskbarItemProgressState.None,
            UiTaskbarProgressState.Indeterminate => TaskbarItemProgressState.Indeterminate,
            UiTaskbarProgressState.Normal => TaskbarItemProgressState.Normal,
            UiTaskbarProgressState.Paused => TaskbarItemProgressState.Paused,
            UiTaskbarProgressState.Error => TaskbarItemProgressState.Error,
            _ => TaskbarItemProgressState.None
        };
    }
}
