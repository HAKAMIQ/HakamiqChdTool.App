using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;
using System;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.ViewModels;

public sealed class QueueContextMenuViewModel
{
    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessSelectedCommand { get; }

    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessIsoCueGdiCommand { get; }

    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessArchiveCommand { get; }

    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessChdExtractCommand { get; }

    public IAsyncRelayCommand<TaskQueueItemViewModel?> VerifyChdCommand { get; }

    public IAsyncRelayCommand<TaskQueueItemViewModel?> IntegrityCheckCommand { get; }

    public IRelayCommand<TaskQueueItemViewModel?> OpenFolderCommand { get; }

    public QueueContextMenuViewModel(
        Func<bool> isQueueInteractionLocked,
        Func<TaskQueueItemViewModel?, Task> processSelected,
        Func<TaskQueueItemViewModel?, Task> verifyChd,
        Func<TaskQueueItemViewModel?, Task> integrityCheck,
        Action<TaskQueueItemViewModel?> openFolder,
        Func<TaskQueueItemViewModel?, bool> canRunPipeline,
        Func<TaskQueueItemViewModel?, bool> canProcessIsoCueGdiOnly,
        Func<TaskQueueItemViewModel?, bool> canProcessArchiveOnly,
        Func<TaskQueueItemViewModel?, bool> canProcessChdExtractOnly,
        Func<TaskQueueItemViewModel?, bool> canVerifyChd,
        Func<TaskQueueItemViewModel?, bool> canIntegrityCheck,
        Func<TaskQueueItemViewModel?, bool> canOpenFolder)
    {
        ArgumentNullException.ThrowIfNull(isQueueInteractionLocked);
        ArgumentNullException.ThrowIfNull(processSelected);
        ArgumentNullException.ThrowIfNull(verifyChd);
        ArgumentNullException.ThrowIfNull(integrityCheck);
        ArgumentNullException.ThrowIfNull(openFolder);
        ArgumentNullException.ThrowIfNull(canRunPipeline);
        ArgumentNullException.ThrowIfNull(canProcessIsoCueGdiOnly);
        ArgumentNullException.ThrowIfNull(canProcessArchiveOnly);
        ArgumentNullException.ThrowIfNull(canProcessChdExtractOnly);
        ArgumentNullException.ThrowIfNull(canVerifyChd);
        ArgumentNullException.ThrowIfNull(canIntegrityCheck);
        ArgumentNullException.ThrowIfNull(canOpenFolder);

        ProcessSelectedCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            processSelected,
            item => item is not null && !isQueueInteractionLocked() && canRunPipeline(item));

        ProcessIsoCueGdiCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => RunWithRequestedActionAsync(item, TaskActionCodes.ConvertToChd, processSelected),
            item => item is not null && !isQueueInteractionLocked() && canProcessIsoCueGdiOnly(item));

        ProcessArchiveCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => RunWithRequestedActionAsync(item, TaskActionCodes.StageArchiveForConversion, processSelected),
            item => item is not null && !isQueueInteractionLocked() && canProcessArchiveOnly(item));

        ProcessChdExtractCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => RunWithRequestedActionAsync(item, TaskActionCodes.RestoreDiscImageFromChd, processSelected),
            item => item is not null && !isQueueInteractionLocked() && canProcessChdExtractOnly(item));

        VerifyChdCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => RunWithRequestedActionAsync(item, TaskActionCodes.VerifyChd, verifyChd),
            item => item is not null && !isQueueInteractionLocked() && canVerifyChd(item));

        IntegrityCheckCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            integrityCheck,
            item => item is not null && !isQueueInteractionLocked() && canIntegrityCheck(item));

        OpenFolderCommand = new RelayCommand<TaskQueueItemViewModel?>(
            openFolder,
            item => item is not null && canOpenFolder(item));
    }

    public void RaiseAllCanExecuteChanged()
    {
        ProcessSelectedCommand.NotifyCanExecuteChanged();
        ProcessIsoCueGdiCommand.NotifyCanExecuteChanged();
        ProcessArchiveCommand.NotifyCanExecuteChanged();
        ProcessChdExtractCommand.NotifyCanExecuteChanged();
        VerifyChdCommand.NotifyCanExecuteChanged();
        IntegrityCheckCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    private static Task RunWithRequestedActionAsync(
        TaskQueueItemViewModel? item,
        string requestedAction,
        Func<TaskQueueItemViewModel?, Task> executeAsync)
    {
        if (item is not null)
        {
            item.RequestedAction = requestedAction;
        }

        return executeAsync(item);
    }
}