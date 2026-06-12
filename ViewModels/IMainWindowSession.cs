using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App.ViewModels;

public interface IMainWindowSession
{
    Window Owner { get; }

    Dispatcher UiDispatcher { get; }

    bool IsQueueInteractionLocked { get; }

    bool IsProcessing { get; }

    bool CancellationRequested { get; }

    bool IncludeSubfolders { get; }

    QueueRowStore QueueRows { get; }

    AppSettings GetSettings();

    IAppFeatureService AppFeatures { get; }

    bool RequireAppFeature(AppFeature feature);

    (bool IsCompliant, string SuggestedStandardName) AnalyzeNamingForPath(string sourcePath);

    void SetFooterStatus(string message);

    void SetFooterIntakeProgress(
        string stageText,
        int scannedCount,
        int totalCount,
        int acceptedCount,
        bool hasKnownTotal);

    void UpdateUiState();

    void RequestSelectFirstQueueRowIfNone();

    void OnQueueClearedUi();

    void ShowError(string titleResourceKey, string messageBody);

    void OpenExplorerForSelectedQueueItem(TaskQueueItemViewModel? item);

    Task OpenOperationLogForQueueItemAsync(TaskQueueItemViewModel? item);

    bool CanOpenOperationLogTarget(TaskQueueItemViewModel? item);

    Task RunRedumpIntegrityForSelectedQueueItemAsync(TaskQueueItemViewModel? item);

    bool CanRunRedumpIntegrityForSelectedQueueItem(TaskQueueItemViewModel? item);

    Task RunRedumpIntegrityForAllQueueItemsAsync();

    bool CanRunRedumpIntegrityForAnyQueueItem();

    Task ShowRedumpDetails(TaskQueueItemViewModel? item);

    Task ApplyRedumpSuggestedNameAsync(TaskQueueItemViewModel? item);

    bool CanApplyRedumpSuggestedName(TaskQueueItemViewModel? item);

    bool CanOpenExplorerTarget(TaskQueueItemViewModel? item);

    void RemoveQueueItem(TaskQueueItemViewModel? item);

    void RetryQueueItem(TaskQueueItemViewModel? item);

    void CancelQueueJob(TaskQueueItemViewModel? item);

    void OpenOptions();

    void OpenAbout();

}
