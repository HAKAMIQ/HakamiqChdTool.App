using System;
using System.Threading.Tasks;
using HakamiqChdTool.App.ViewModels;

namespace HakamiqChdTool.App.QueueRun;

public interface IQueueRunCoordinator : IDisposable
{
    bool IsProcessing { get; }

    bool CancellationRequested { get; }

    event Action? RunStateChanged;

    Task StartProcessingAsync();

    Task ProcessSelectedAsync(TaskQueueItemViewModel? selected);

    Task VerifySelectedChdAsync(TaskQueueItemViewModel? selected);

    Task SelectFilesAsync();

    Task SelectFolderAsync();

    Task QuickConvertAsync();

    Task QuickExtractAsync();

    Task ScanFolderQuickConvertAsync();

    Task ScanFolderQuickExtractAsync();

    void RequestCancel();
}