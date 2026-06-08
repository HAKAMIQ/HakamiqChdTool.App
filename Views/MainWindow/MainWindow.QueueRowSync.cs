using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using IoPath = System.IO.Path;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const string RedumpNamingSuggestionLogFormatKey = "LocRedump_NamingSuggestionLogFormat";

    private void OnQueueRowStoreCollectionChanged(QueueRowStoreCollectionDelta delta)
    {
        switch (delta)
        {
            case QueueRowStoreAppended appended:
                for (int i = 0; i < appended.Count; i++)
                {
                    QueueRowData? row = _queueRowStore.GetByIndex(appended.StartIndex + i);
                    if (row is null)
                    {
                        continue;
                    }

                    _sinkIndex[row.ItemId] = new TaskQueueStateAdapter(
                        row.ItemId,
                        _queueRowStore);

                    _queueUiAggregates.Upsert(row);
                }

                break;

            case QueueRowStoreRemoved removed:
                _sinkIndex.TryRemove(removed.RemovedRow.ItemId, out _);
                _queueUiAggregates.Remove(removed.RemovedRow);
                break;

            case QueueRowStoreReset:
                _sinkIndex.Clear();
                _queueUiAggregates.Rebuild(_queueRowStore.Rows);
                break;
        }

        _queueRowsPresenterCache = null;
        UpdateQueueViewportWindow();
        RequestUiStateRefresh();
    }

    private void OnQueueRowStoreRowMutated(Guid rowId)
    {
        QueueRowData? row = _queueRowStore.GetById(rowId);
        if (row is null)
        {
            return;
        }

        _queueUiAggregates.Upsert(row);
        RequestUiStateRefresh();
    }

    private void OnVmMaterialized(Guid id, TaskQueueItemViewModel vm)
    {
        vm.PropertyChanged -= TaskItem_PropertyChanged;
        vm.PropertyChanged += TaskItem_PropertyChanged;
    }

    private void OnVmReleased(Guid id, TaskQueueItemViewModel vm)
    {
        vm.PropertyChanged -= TaskItem_PropertyChanged;
    }

    private void ApplyIntegrityAndSync(
        TaskQueueItemViewModel item,
        IntegrityValidationState state,
        string statusMessage,
        string detailTooltip)
    {
        _queueRowStore.Mutate(item.QueueItemId, row =>
        {
            row.IntegrityState = state;
            row.IntegrityMessage = statusMessage;
        });

        item.SetIntegrityPresentation(state, statusMessage, detailTooltip);
    }

    private void ApplyRedumpResultAndSync(TaskQueueItemViewModel item, DeepHashAnalysisResult result)
    {
        DeepHashAnalysisPresentation presentation = DeepHashAnalysisPresenter.Format(result);
        ApplyIntegrityAndSync(item, result.State, presentation.StatusMessage, presentation.DetailTooltip);

        if (result.State == IntegrityValidationState.Verified && !string.IsNullOrWhiteSpace(result.SuggestedStandardName))
        {
            string currentFileName = string.IsNullOrWhiteSpace(item.SourcePath)
                ? string.Empty
                : IoPath.GetFileName(item.SourcePath);

            item.SuggestedStandardName = result.SuggestedStandardName;
            item.IsNamingCompliant = string.Equals(currentFileName, result.SuggestedStandardName, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            item.SuggestedStandardName = string.Empty;
            item.IsNamingCompliant = true;
        }

        _queueRowStore.Mutate(item.QueueItemId, row =>
        {
            row.IsNamingCompliant = item.IsNamingCompliant;
            row.SuggestedStandardName = item.SuggestedStandardName;
        });

        _viewModel.NotifyQueueCommandsCanExecuteChanged();
    }

    private void ApplyNamingAnalysisAndSync(TaskQueueItemViewModel item)
    {
        QueueNamingAnalysis.Apply(item, _settings);

        _queueRowStore.Mutate(item.QueueItemId, row =>
        {
            row.IsNamingCompliant = item.IsNamingCompliant;
            row.SuggestedStandardName = item.SuggestedStandardName;
        });
    }

    private void SetHasActiveQueueBindingAndSync(Guid rowId, bool value)
    {
        _queueRowStore.Mutate(rowId, row => row.HasActiveQueueBinding = value);

        TaskQueueItemViewModel? vm = _viewport.TryGetMaterialized(rowId);
        if (vm is not null)
        {
            vm.HasActiveQueueBinding = value;
        }
    }

    private void SetHasActiveQueueBindingAndSync(TaskQueueItemViewModel item, bool value) =>
        SetHasActiveQueueBindingAndSync(item.QueueItemId, value);

    private void ApplyPathResetAndSync(TaskQueueItemViewModel item, string newPath)
    {
        item.InitializeFromPath(newPath, item.RequestedAction, item.DetectedPlatform);
        SyncRowFromViewModel(item);
    }

    private void SyncRowFromViewModel(TaskQueueItemViewModel vm)
    {
        _queueRowStore.Mutate(vm.QueueItemId, row =>
        {
            row.OriginalPath = vm.OriginalPath;
            row.SourcePath = vm.SourcePath;
            row.InputType = vm.InputType;
            row.FileName = vm.FileName;
            row.DetectedPlatform = vm.DetectedPlatform;
            row.DetectionReason = vm.DetectionReason;
            row.RequestedAction = vm.RequestedAction;
            row.CurrentState = vm.CurrentState;
            row.FinalResult = vm.FinalResult;
            row.StatusDetail = vm.StatusDetail;
            row.Progress = vm.ProgressValue;
            row.IsIndeterminate = vm.IsIndeterminate;
            row.IsProgressActive = vm.IsProgressActive;
            row.OutputPath = vm.OutputPath;
            row.LogPath = vm.LogPath;
            row.TempWorkingDirectory = vm.TempWorkingDirectory;
            row.InputBytes = vm.InputBytes;
            row.OutputBytes = vm.OutputBytes;
            row.CleanupDeletedBytes = vm.CleanupDeletedBytes;
            row.SbiCopiedCount = vm.SbiCopiedCount;
            row.PostProcessingFailureCount = vm.PostProcessingFailureCount;
            row.IntegrityState = vm.IntegrityState;
            row.IntegrityMessage = vm.IntegrityStatusMessage;
            row.IsNamingCompliant = vm.IsNamingCompliant;
            row.SuggestedStandardName = vm.SuggestedStandardName;
            row.HasActiveQueueBinding = vm.HasActiveQueueBinding;
        });
    }

    private void TaskItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        void Handle()
        {
            TaskQueueItemViewModel? item = sender as TaskQueueItemViewModel;

            if (item is not null && e.PropertyName is nameof(TaskQueueItemViewModel.CurrentState) or nameof(TaskQueueItemViewModel.FinalResult))
            {
                string signature = $"{item.OriginalPath}|{item.CurrentState}|{item.FinalResult}|{item.StatusDetailDisplay}";
                if (_loggedExecutionSignatures.Count >= MaxExecutionSignatures)
                {
                    _loggedExecutionSignatures.Clear();
                }

                if (_loggedExecutionSignatures.Add(signature))
                {
                    string headline = ArabicUi.ProcessingPhaseHeadline(item.Pipeline.ProcessingState);
                    string detail = item.StatusDetailDisplay;

                    if (item.CurrentState is TaskQueueStateCodes.Failed or TaskQueueStateCodes.Cancelled &&
                        !string.IsNullOrWhiteSpace(detail) &&
                        detail != "-")
                    {
                        AppendExecutionLog($"{item.FileName}: {headline} — {detail}");
                    }
                    else
                    {
                        AppendExecutionLog($"{item.FileName}: {headline}");
                    }
                }
            }

            if (item is not null &&
                e.PropertyName is nameof(TaskQueueItemViewModel.RequestedAction)
                    or nameof(TaskQueueItemViewModel.CurrentState)
                    or nameof(TaskQueueItemViewModel.StatusDetail))
            {
                SyncRowFromViewModel(item);
            }

            if (ShouldRefreshUiStateForProperty(e.PropertyName))
            {
                RequestUiStateRefresh();
            }
        }

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            Handle();
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Handle();
        }, DispatcherPriority.DataBind);
    }

    private Task ApplyAutoStandardizedNameIfEnabled(TaskQueueItemViewModel item)
    {
        if (item.UsesQuickProfile ||
            !_settings.EnableDeepIntegrityCheck ||
            !_settings.ApplyStandardNamingBasedOnHash ||
            !_appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion))
        {
            return Task.CompletedTask;
        }

        ApplyNamingAnalysisAndSync(item);

        if (!item.IsNamingCompliant && !string.IsNullOrWhiteSpace(item.SuggestedStandardName))
        {
            AppendExecutionLog(ArabicUi.Format(
                RedumpNamingSuggestionLogFormatKey,
                item.FileName,
                item.SuggestedStandardName));
        }

        return Task.CompletedTask;
    }
}
