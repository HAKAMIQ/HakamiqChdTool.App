using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using Serilog;

namespace HakamiqChdTool.App.QueueRun;

[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class QueueRunCoordinator(
    QueueController queueController,
    IQueueRunUiPort ui) : IQueueRunCoordinator
{
    private readonly QueueController _queueController =
        queueController ?? throw new ArgumentNullException(nameof(queueController));

    private readonly IQueueRunUiPort _ui =
        ui ?? throw new ArgumentNullException(nameof(ui));

    private readonly object _runGate = new();

    private CancellationTokenSource? _cts;
    private bool _isProcessing;
    private bool _cancellationUiRequested;
    private bool _disposed;

    public bool IsProcessing
    {
        get
        {
            lock (_runGate)
            {
                return _isProcessing;
            }
        }
    }

    public bool CancellationRequested
    {
        get
        {
            lock (_runGate)
            {
                return _cancellationUiRequested;
            }
        }
    }

    public event Action? RunStateChanged;

    public void RequestCancel()
    {
        CancellationTokenSource? cts;

        lock (_runGate)
        {
            if (_disposed || _cts is null || _cts.IsCancellationRequested || _cancellationUiRequested)
            {
                return;
            }

            _cancellationUiRequested = true;
            cts = _cts;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (!ShouldNotifyUi())
        {
            return;
        }

        RaiseStateChanged();
        _ui.UpdateUiState();
    }

    public Task StartProcessingAsync()
    {
        if (IsDisposedOrQueueLocked())
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<Guid> queuedIds = _ui.EnumerateQueuedItemIds();
        if (queuedIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunQueuedItemsByIdsAsync(queuedIds, processedSelectionOnly: false);
    }

    public async Task ProcessSelectedAsync(TaskQueueItemViewModel? selected)
    {
        if (IsDisposedOrQueueLocked() || selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.SourcePath))
        {
            _ui.SetFooterStatus(MainWindowMessages.ProcessSelectedFailedFooter);
            return;
        }

        _ui.CaptureThemeIntoSettings();
        _ui.PersistSettings();

        if (!_ui.IsRowQueuedForProcessing(selected.QueueItemId))
        {
            _ui.SetFooterStatus(MainWindowMessages.SelectOperationBeforeProcessFooter);
            return;
        }

        if (ShouldRunStorageAdvisorForProcessingItem(selected)
            && !_ui.ConfirmStorageAdvisorBeforeProcessing([selected], processedSelectionOnly: true))
        {
            return;
        }

        if (!TryBeginRun(out CancellationToken runToken))
        {
            return;
        }

        bool activeBindingSet = false;

        _ui.ResetRunSummary();
        _ui.UpdateUiState();

        try
        {
            await _ui.ApplyAutoStandardizedNameIfEnabledAsync(selected).ConfigureAwait(true);

            var chd = new ChdQueueItem
            {
                Id = selected.QueueItemId,
                InputPath = selected.SourcePath,
                Mode = QueueModeFromRequestedAction(selected.RequestedAction),
                ExecutionProfile = selected.ExecutionProfile
            };

            _ui.SetHasActiveQueueBindingAndSync(selected, value: true);
            activeBindingSet = true;

            bool wasCancelled = await _queueController
                .RunBatchAsync([chd], runToken)
                .ConfigureAwait(true);

            PostConversionArtifactResult runArtifacts = PostConversionArtifactResult.Empty;
            if (!wasCancelled)
            {
                runArtifacts = await _ui.GenerateM3uPlaylistsForCompletedChdOutputsAsync(
                        CollectCompletedChdWorkflowOutputs([chd]))
                    .ConfigureAwait(true);
            }

            _ui.BuildRunSummary(
                showCompletionDialog: false,
                wasCancelled: wasCancelled,
                processedSelectionOnly: true,
                runArtifacts: runArtifacts);
        }
        catch (OperationCanceledException)
        {
            _ui.BuildRunSummary(
                showCompletionDialog: false,
                wasCancelled: true,
                processedSelectionOnly: true,
                runArtifacts: PostConversionArtifactResult.Empty);
        }
        catch (Exception ex)
        {
            _ui.SetFooterStatus(MainWindowMessages.ProcessSelectedFailedFooter);
            Log.Error(ex, "ProcessSelectedAsync failed.");
            _ui.ShowError(MainWindowMessages.ProcessingErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
        finally
        {
            if (activeBindingSet)
            {
                _ui.SetHasActiveQueueBindingAndSync(selected, value: false);
            }

            EndRun();
        }
    }

    public async Task VerifySelectedChdAsync(TaskQueueItemViewModel? selected)
    {
        if (IsDisposedOrQueueLocked() || selected is null || !selected.IsDirectChd)
        {
            return;
        }

        _ui.CaptureThemeIntoSettings();
        _ui.PersistSettings();

        if (!string.Equals(selected.RequestedAction, TaskActionCodes.VerifyChd, StringComparison.Ordinal))
        {
            if (string.Equals(selected.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal)
                || string.Equals(selected.RequestedAction, TaskActionCodes.RestoreDiscImageFromChd, StringComparison.Ordinal))
            {
                selected.RequestedAction = TaskActionCodes.VerifyChd;
            }
            else
            {
                return;
            }
        }

        if (!TryBeginRun(out CancellationToken runToken))
        {
            return;
        }

        bool activeBindingSet = false;

        _ui.ResetRunSummary();
        _ui.UpdateUiState();

        try
        {
            var chd = new ChdQueueItem
            {
                Id = selected.QueueItemId,
                InputPath = selected.SourcePath,
                Mode = QueueModeFromRequestedAction(selected.RequestedAction),
                ExecutionProfile = selected.ExecutionProfile
            };

            _ui.SetHasActiveQueueBindingAndSync(selected, value: true);
            activeBindingSet = true;

            bool wasCancelled = await _queueController
                .RunBatchAsync([chd], runToken)
                .ConfigureAwait(true);

            _ui.BuildRunSummary(
                showCompletionDialog: false,
                wasCancelled: wasCancelled,
                processedSelectionOnly: true,
                runArtifacts: PostConversionArtifactResult.Empty);
        }
        catch (OperationCanceledException)
        {
            _ui.BuildRunSummary(
                showCompletionDialog: false,
                wasCancelled: true,
                processedSelectionOnly: true,
                runArtifacts: PostConversionArtifactResult.Empty);
        }
        catch (Exception ex)
        {
            _ui.SetFooterStatus(MainWindowMessages.VerifyChdFailedFooter);
            Log.Error(ex, "VerifySelectedChdAsync failed.");
            _ui.ShowError(MainWindowMessages.VerifyChdErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
        finally
        {
            if (activeBindingSet)
            {
                _ui.SetHasActiveQueueBindingAndSync(selected, value: false);
            }

            EndRun();
        }
    }

    public async Task SelectFilesAsync()
    {
        if (IsDisposed())
        {
            return;
        }

        if (_ui.IsQueueInteractionLocked)
        {
            _ui.SetFooterStatus(MainWindowMessages.WaitForBackgroundOp);
            return;
        }

        QueueExecutionProfile profile = _ui.GetSelectedInputExecutionProfile();

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = _ui.GetSelectedInputDialogTitle(profile),
            Filter = _ui.GetSelectedInputDialogFilter(profile)
        };

        if (dialog.ShowDialog(_ui.Owner) != true)
        {
            return;
        }

        try
        {
            await IngestForSelectedModeAsync(dialog.FileNames, QueueIngestKind.FilesOnly, QueueIntakeSource.UserInitiatedAdd)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "SelectFilesAsync cancelled.");
            _ui.SetFooterStatus(MainWindowMessages.AddFilesCancelledFooter);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SelectFilesAsync failed.");
            _ui.ShowError(MainWindowMessages.AddFilesErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    public async Task SelectFolderAsync()
    {
        if (IsDisposed())
        {
            return;
        }

        if (_ui.IsQueueInteractionLocked)
        {
            _ui.SetFooterStatus(MainWindowMessages.WaitForBackgroundOp);
            return;
        }

        var dialog = new VistaFolderBrowserDialog
        {
            Description = _ui.GetSelectedFolderDialogDescription(),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(_ui.Owner) != true || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        string selectedPath = dialog.SelectedPath;

        try
        {
            await IngestForSelectedModeAsync([selectedPath], QueueIngestKind.Mixed, QueueIntakeSource.FolderImport)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "SelectFolderAsync cancelled.");
            _ui.SetFooterStatus(MainWindowMessages.AddFilesCancelledFooter);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SelectFolderAsync failed.");
            _ui.ShowError(MainWindowMessages.AddFilesErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    public Task QuickConvertAsync() => RunQuickFilePickerAsync(QueueExecutionProfile.QuickConvert);

    public Task QuickExtractAsync() => RunQuickFilePickerAsync(QueueExecutionProfile.QuickExtract);

    public Task ScanFolderQuickConvertAsync() => RunQuickFolderPickerAsync(QueueExecutionProfile.QuickConvert);

    public Task ScanFolderQuickExtractAsync() => RunQuickFolderPickerAsync(QueueExecutionProfile.QuickExtract);

    private async Task RunQuickFilePickerAsync(QueueExecutionProfile executionProfile)
    {
        if (IsDisposed())
        {
            return;
        }

        if (_ui.IsQueueInteractionLocked)
        {
            _ui.SetFooterStatus(MainWindowMessages.WaitForBackgroundOp);
            return;
        }

        string title;
        string filter;

        if (executionProfile == QueueExecutionProfile.QuickVerify)
        {
            title = ArabicUi.Get(MainWindowMessages.QuickVerifyDialogTitle);
            filter = ArabicUi.Get(MainWindowMessages.QuickChdFilesFilter);
        }
        else if (executionProfile == QueueExecutionProfile.QuickExtract)
        {
            title = ArabicUi.Get(MainWindowMessages.QuickExtractDialogTitle);
            filter = ArabicUi.Get(MainWindowMessages.QuickChdFilesFilter);
        }
        else
        {
            title = ArabicUi.Get(MainWindowMessages.QuickConvertDialogTitle);
            filter = ArabicUi.Get(MainWindowMessages.QuickConvertFilesFilter);
        }

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = title,
            Filter = filter
        };

        if (dialog.ShowDialog(_ui.Owner) != true)
        {
            return;
        }

        IReadOnlyList<Guid> added;
        try
        {
            added = await _ui.IngestQuickPathsAsync(
                    dialog.FileNames,
                    QueueIngestKind.FilesOnly,
                    executionProfile,
                    QueueIntakeSource.UserInitiatedAdd)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Quick file intake cancelled.");
            return;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Quick file intake failed.");
            _ui.ShowError(MainWindowMessages.AddFilesErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
            return;
        }

        if (added.Count == 0)
        {
            return;
        }

        await RunQueuedItemsByIdsAsync(added, processedSelectionOnly: true).ConfigureAwait(true);
    }

    private async Task RunQuickFolderPickerAsync(QueueExecutionProfile executionProfile)
    {
        if (IsDisposed())
        {
            return;
        }

        if (_ui.IsQueueInteractionLocked)
        {
            _ui.SetFooterStatus(MainWindowMessages.WaitForBackgroundOp);
            return;
        }

        var dialog = new VistaFolderBrowserDialog
        {
            Description = executionProfile == QueueExecutionProfile.QuickExtract
                ? ArabicUi.Get(MainWindowMessages.QuickExtractFolderDescription)
                : ArabicUi.Get(MainWindowMessages.QuickConvertFolderDescription),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(_ui.Owner) != true || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        IReadOnlyList<Guid> added;
        try
        {
            added = await _ui.IngestQuickPathsAsync(
                    [dialog.SelectedPath],
                    QueueIngestKind.Mixed,
                    executionProfile,
                    QueueIntakeSource.FolderImport)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Quick folder intake cancelled.");
            return;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Quick folder intake failed.");
            _ui.ShowError(MainWindowMessages.AddFilesErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
            return;
        }

        if (added.Count == 0)
        {
            return;
        }

        await RunQueuedItemsByIdsAsync(added, processedSelectionOnly: true).ConfigureAwait(true);
    }

    private async Task IngestForSelectedModeAsync(
        IEnumerable<string> paths,
        QueueIngestKind inputKind,
        QueueIntakeSource intakeSource)
    {
        QueueExecutionProfile profile = _ui.GetSelectedInputExecutionProfile();

        if (profile == QueueExecutionProfile.Standard)
        {
            await _ui.IngestPathsAsync(paths, inputKind, intakeSource).ConfigureAwait(true);
            return;
        }

        await _ui.IngestQuickPathsAsync(paths, inputKind, profile, intakeSource).ConfigureAwait(true);
    }

    private async Task RunQueuedItemsByIdsAsync(IReadOnlyList<Guid> itemIds, bool processedSelectionOnly)
    {
        if (IsDisposedOrQueueLocked() || itemIds.Count == 0)
        {
            return;
        }

        _ui.CaptureThemeIntoSettings();
        _ui.PersistSettings();

        var seenIds = new HashSet<Guid>();
        var queuedVms = new List<TaskQueueItemViewModel>(itemIds.Count);
        var activeBindings = new List<TaskQueueItemViewModel>(itemIds.Count);

        foreach (Guid id in itemIds)
        {
            if (!seenIds.Add(id))
            {
                continue;
            }

            if (!_ui.IsRowQueuedForProcessing(id))
            {
                continue;
            }

            TaskQueueItemViewModel? vm = _ui.TryMaterializeOrGet(id);
            if (vm is not null)
            {
                queuedVms.Add(vm);
            }
        }

        if (queuedVms.Count == 0)
        {
            _ui.SetFooterStatus(MainWindowMessages.NothingQueuedToStartFooter);
            return;
        }

        TaskQueueItemViewModel[] storageAdvisorItems =
        [
            .. queuedVms.Where(ShouldRunStorageAdvisorForProcessingItem)
        ];

        if (storageAdvisorItems.Length > 0
            && !_ui.ConfirmStorageAdvisorBeforeProcessing(storageAdvisorItems, processedSelectionOnly))
        {
            return;
        }

        if (!TryBeginRun(out CancellationToken runToken))
        {
            return;
        }

        _ui.ResetRunSummary();
        _ui.UpdateUiState();
        _ui.SetFooterStatus(MainWindowMessages.ProcessingStartedFooter);

        try
        {
            var batch = new List<ChdQueueItem>(queuedVms.Count);

            foreach (TaskQueueItemViewModel vm in queuedVms)
            {
                if (runToken.IsCancellationRequested)
                {
                    _ui.BuildRunSummary(
                        showCompletionDialog: false,
                        wasCancelled: true,
                        processedSelectionOnly: processedSelectionOnly,
                        runArtifacts: PostConversionArtifactResult.Empty);

                    return;
                }

                if (string.IsNullOrWhiteSpace(vm.SourcePath))
                {
                    _ui.SetHasActiveQueueBindingAndSync(vm, value: false);
                    continue;
                }

                await _ui.ApplyAutoStandardizedNameIfEnabledAsync(vm).ConfigureAwait(true);

                _ui.SetHasActiveQueueBindingAndSync(vm, value: true);
                activeBindings.Add(vm);

                batch.Add(new ChdQueueItem
                {
                    Id = vm.QueueItemId,
                    InputPath = vm.SourcePath,
                    Mode = QueueModeFromRequestedAction(vm.RequestedAction),
                    ExecutionProfile = vm.ExecutionProfile
                });
            }

            if (batch.Count == 0)
            {
                _ui.SetFooterStatus(MainWindowMessages.NothingQueuedToStartFooter);
                return;
            }

            bool wasCancelled = await _queueController.RunBatchAsync(batch, runToken).ConfigureAwait(true);

            PostConversionArtifactResult runArtifacts = PostConversionArtifactResult.Empty;
            if (!wasCancelled)
            {
                runArtifacts = await _ui.GenerateM3uPlaylistsForCompletedChdOutputsAsync(
                        CollectCompletedChdWorkflowOutputs(batch))
                    .ConfigureAwait(true);
            }

            _ui.BuildRunSummary(
                showCompletionDialog: false,
                wasCancelled: wasCancelled,
                processedSelectionOnly: processedSelectionOnly,
                runArtifacts: runArtifacts);
        }
        catch (OperationCanceledException)
        {
            _ui.BuildRunSummary(
                showCompletionDialog: false,
                wasCancelled: true,
                processedSelectionOnly: processedSelectionOnly,
                runArtifacts: PostConversionArtifactResult.Empty);
        }
        catch (Exception ex)
        {
            _ui.SetFooterStatus(MainWindowMessages.ProcessingUnexpectedErrorFooter);
            Log.Error(ex, "RunQueuedItemsByIdsAsync failed.");
            _ui.ShowError(MainWindowMessages.ProcessingErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
        finally
        {
            ClearActiveQueueBindings(activeBindings);
            EndRun();
        }
    }

    private bool TryBeginRun(out CancellationToken token)
    {
        lock (_runGate)
        {
            token = CancellationToken.None;

            if (_disposed || _isProcessing)
            {
                return false;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _isProcessing = true;
            _cancellationUiRequested = false;

            token = _cts.Token;
        }

        RaiseStateChanged();
        return true;
    }

    private void EndRun()
    {
        CancellationTokenSource? cts;
        bool shouldNotifyUi;

        lock (_runGate)
        {
            cts = _cts;
            _cts = null;

            _isProcessing = false;
            _cancellationUiRequested = false;
            shouldNotifyUi = !_disposed;
        }

        cts?.Dispose();

        if (!shouldNotifyUi)
        {
            return;
        }

        RaiseStateChanged();
        _ui.UpdateUiState();
    }

    private void ClearActiveQueueBindings(IEnumerable<TaskQueueItemViewModel> items)
    {
        foreach (TaskQueueItemViewModel item in items)
        {
            _ui.SetHasActiveQueueBindingAndSync(item, value: false);
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            RunStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "QueueRunCoordinator.RunStateChanged subscriber threw.");
        }
    }

    private bool IsDisposed()
    {
        lock (_runGate)
        {
            return _disposed;
        }
    }

    private bool ShouldNotifyUi()
    {
        lock (_runGate)
        {
            return !_disposed;
        }
    }

    private bool IsDisposedOrQueueLocked() => IsDisposed() || _ui.IsQueueInteractionLocked;

    private static bool ShouldRunStorageAdvisorForProcessingItem(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return false;
        }

        string mode = QueueModeFromRequestedAction(item.RequestedAction);
        return string.Equals(mode, "Convert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Extract", StringComparison.OrdinalIgnoreCase);
    }

    private static string QueueModeFromRequestedAction(string? requestedAction) =>
        QueueModeResolver.QueueModeFromRequestedAction(requestedAction);

    private static IReadOnlyList<string> CollectCompletedChdWorkflowOutputs(IEnumerable<ChdQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return
        [
            .. items
                .Where(static item => item is not null
                    && (string.Equals(item.Status, TaskQueueStateCodes.Completed, StringComparison.Ordinal)
                        || string.Equals(item.Status, TaskQueueStateCodes.Skipped, StringComparison.Ordinal))
                    && string.Equals(item.Mode, "Convert", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.OutputPath))
                .Select(static item => item.OutputPath!.Trim())
                .Where(static path => string.Equals(Path.GetExtension(path), ".chd", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;

        lock (_runGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            cts = _cts;
            _cts = null;

            _isProcessing = false;
            _cancellationUiRequested = false;
            RunStateChanged = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts?.Dispose();
    }
}