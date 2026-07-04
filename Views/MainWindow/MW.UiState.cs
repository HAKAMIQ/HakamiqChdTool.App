using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;

using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using Serilog;

using IoPath = System.IO.Path;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const string ExecutionLogTimestampFormat = "HH:mm:ss";
    private const char LeftToRightMark = '\u200E';

    private void AppendExecutionLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(() => AppendExecutionLog(message)),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling execution log append failed.");
            }

            return;
        }

        string resolved = ArabicUi.ResolveExecutionLogLine(message.Trim());
        string timestamp = DateTime.Now.ToString(
            ExecutionLogTimestampFormat,
            CultureInfo.InvariantCulture);

        string line = $"[{timestamp}] {resolved}";

        _executionLogLines.Enqueue(line);

        while (_executionLogLines.Count > MaxExecutionLogLines)
        {
            _executionLogLines.Dequeue();
        }
    }

    private static bool ShouldRefreshUiStateForProperty(string? propertyName)
    {
        return propertyName is null
            or nameof(TaskQueueItemViewModel.CurrentState)
            or nameof(TaskQueueItemViewModel.FinalResult)
            or nameof(TaskQueueItemViewModel.HasActiveQueueBinding)
            or nameof(TaskQueueItemViewModel.RequestedAction)
            or nameof(TaskQueueItemViewModel.OutputPath);
    }

    private void RequestUiStateRefresh()
    {
        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (Interlocked.Exchange(ref _pendingUiStateRefresh, 1) == 1)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    Interlocked.Exchange(ref _pendingUiStateRefresh, 0);

                    if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
                    {
                        return;
                    }

                    try
                    {
                        UpdateUiState();
                        _viewModel.NotifyQueueCommandsCanExecuteChanged();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Deferred UI state refresh failed.");
                    }
                }),
                DispatcherPriority.ContextIdle);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _pendingUiStateRefresh, 0);
            Log.Debug(ex, "Scheduling deferred UI state refresh failed.");
        }
    }

    private void UpdateSelectionUiState()
    {
        try
        {
            TaskQueueItemViewModel? selectedItem = TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

            _viewModel.SelectedTask = selectedItem;
            _queueContextMenuViewModel.RaiseAllCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Selection UI state update failed.");
        }
    }

    private void UpdateUiState()
    {
        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                _ = Dispatcher.InvokeAsync(
                    UpdateUiState,
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling UI state update failed.");
            }

            return;
        }

        try
        {
            UpdateUiStateCore();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UI state update failed.");
        }
    }

    private void UpdateUiStateCore()
    {
        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        QueueUiSnapshot aggregate = _queueUiAggregates.Capture();
        bool hasTasks = aggregate.TotalCount > 0;

        _viewModel.CanStartProcessing =
            CountQueuedRowsForSelectedOperationMode() > 0 &&
            !IsQueueInteractionLocked;

        _viewModel.IsShellEnabled = !IsQueueInteractionLocked;
        _viewModel.NotifyQueueCommandsCanExecuteChanged();

        UpdateSelectionUiState();
        UpdateFooterSummary(aggregate, hasTasks);

        FooterProgressState footerProgress = UpdateFooterProgress(aggregate);
        UpdateTaskbarProgress(aggregate, hasTasks, footerProgress);
    }

    private void ResetRunSummary()
    {
        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(ResetRunSummary),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling run summary reset failed.");
            }

            return;
        }

        _loggedExecutionSignatures.Clear();
        _executionLogLines.Clear();
    }

    private void BuildRunSummary(
        bool showCompletionDialog,
        bool wasCancelled,
        bool processedSelectionOnly,
        PostConversionArtifactResult runArtifacts)
    {
        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(() => BuildRunSummary(
                        showCompletionDialog,
                        wasCancelled,
                        processedSelectionOnly,
                        runArtifacts)),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling run summary build failed.");
            }

            return;
        }

        IReadOnlyList<SessionRunSummaryItem> targetItems = ResolveRunSummaryItems(processedSelectionOnly);
        SessionRunMetrics metrics = SessionRunMetricsCalculator.ComputeMetrics(
            targetItems,
            runArtifacts);

        string summaryText = LocalizedSessionRunSummaryFormatter.Format(
            metrics,
            wasCancelled);

        SetFooterStatus(wasCancelled
            ? MainWindowMessages.SessionCancelledFooter
            : MainWindowMessages.SessionCompletedFooter);

        TryNotifyTrayForBatchCompletion(
            wasCancelled,
            processedSelectionOnly,
            metrics);

        if (showCompletionDialog)
        {
            ShowNoticeDialog(
                wasCancelled
                    ? MainWindowMessages.SummaryCancelledTitle
                    : MainWindowMessages.SummaryCompletedTitle,
                summaryText);
        }
    }

    private IReadOnlyList<SessionRunSummaryItem> ResolveRunSummaryItems(bool processedSelectionOnly)
    {
        if (!processedSelectionOnly)
        {
            return _queueRowStore.Rows
                .Select(ToSessionRunSummaryItem)
                .ToArray();
        }

        if (TasksDataGrid.SelectedItem is not TaskQueueItemViewModel selected)
        {
            return [];
        }

        QueueRowData? selectedRow = _queueRowStore.GetById(selected.QueueItemId);

        return selectedRow is null
            ? []
            : [ToSessionRunSummaryItem(selectedRow)];
    }

    private static SessionRunSummaryItem ToSessionRunSummaryItem(QueueRowData row)
    {
        string currentState = row.CurrentState;

        return new SessionRunSummaryItem(
            IsCompleted: currentState == TaskQueueStateCodes.Completed,
            IsFailed: currentState is TaskQueueStateCodes.Failed or TaskQueueStateCodes.PasswordRequired,
            IsSkipped: currentState == TaskQueueStateCodes.Skipped,
            IsCancelled: currentState == TaskQueueStateCodes.Cancelled,
            IsReverseSupported: IsChdPath(row.SourcePath),
            IsDirectSupported: IsDirectSupportedInputType(row.InputType),
            IsRedumpMatched: row.IntegrityState == IntegrityValidationState.Verified,
            DeletedBytes: row.CleanupDeletedBytes,
            SbiCopiedCount: row.SbiCopiedCount,
            PostProcessingFailureCount: row.PostProcessingFailureCount,
            InputBytes: row.InputBytes,
            OutputBytes: row.OutputBytes,
            ConversionPerformanceReport: row.ConversionPerformanceReport,
            FileName: NormalizeSummaryText(row.FileName),
            StatusDetail: NormalizeSummaryText(row.StatusDetail));
    }

    private static bool IsChdPath(string? path)
    {
        return string.Equals(
            IoPath.GetExtension(path),
            ".chd",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectSupportedInputType(string? inputType)
    {
        return string.Equals(inputType, "ISO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "CUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "GDI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "BIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "ZIP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "RAR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(inputType, "7Z", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSummaryText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}