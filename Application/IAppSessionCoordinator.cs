using HakamiqChdTool.App.ViewModels;
using System;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Coordination;

public interface IAppSessionCoordinator : IDisposable
{
    bool IsProcessing { get; }

    bool CancellationRequested { get; }

    event Action? SessionStateChanged;

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