using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const string ExecutionLogTimestampFormat = "HH:mm:ss";
    private const char LeftToRightMark = '\u200E';

    private readonly record struct FooterProgressState(double Percent, bool IsIndeterminate);

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
        string timestamp = DateTime.Now.ToString(ExecutionLogTimestampFormat, CultureInfo.InvariantCulture);
        string line = $"[{timestamp}] {resolved}";

        _executionLogLines.Enqueue(line);

        while (_executionLogLines.Count > MaxExecutionLogLines)
        {
            _executionLogLines.Dequeue();
        }
    }

    private void AnimateQueueProgressTo(double target)
    {
        try
        {
            QueueProgressBar.Value = Math.Clamp(target, 0, 100);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Queue progress update failed.");
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
                _ = Dispatcher.InvokeAsync(UpdateUiState, DispatcherPriority.Background);
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

    private string GetSessionOperationalPhaseHeadline()
    {
        return ArabicUi.Get("LocState_Processing");
    }

    private void UpdateUiStateCore()
    {
        if (_shutdownStarted || _shutdownCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        QueueUiAggregateSnapshot aggregate = _queueUiAggregates.Capture();
        bool hasTasks = aggregate.TotalCount > 0;

        _viewModel.CanStartProcessing = CountQueuedRowsForSelectedOperationMode() > 0
            && !IsQueueInteractionLocked;

        _viewModel.IsShellEnabled = !IsQueueInteractionLocked;
        _viewModel.NotifyQueueCommandsCanExecuteChanged();

        UpdateSelectionUiState();

        UpdateFooterSummary(aggregate, hasTasks);

        FooterProgressState footerProgress = UpdateFooterProgress(aggregate);
        UpdateTaskbarProgress(aggregate, hasTasks, footerProgress);
    }

    private void UpdateFooterSummary(QueueUiAggregateSnapshot aggregate, bool hasTasks)
    {
        FooterStatusStrip.SetHiddenCounters(
            waiting: aggregate.WaitingCount,
            active: aggregate.ActiveCount,
            completed: aggregate.CompletedCount,
            failed: aggregate.FailedCount,
            skipped: aggregate.SkippedCount);

        if (!hasTasks)
        {
            FooterStatusStrip.SetReady();
            return;
        }

        if (_coordinator.IsProcessing)
        {
            return;
        }

        if (IsFooterSessionTerminal(aggregate))
        {
            FooterStatusStrip.SetCompleted(
                completedCount: aggregate.CompletedCount,
                failedCount: aggregate.FailedCount,
                skippedCount: aggregate.SkippedCount);

            return;
        }

        string currentFooterText = FooterQueueSummaryText.Text?.Trim() ?? string.Empty;
        string idleText = ArabicUi.Get(MainWindowMessages.FooterIdleNoTasks);
        string readyText = ArabicUi.Get("LocFooter_Ready");

        bool shouldRefreshNeutralStatus =
            string.IsNullOrWhiteSpace(currentFooterText)
            || string.Equals(currentFooterText, idleText, StringComparison.Ordinal)
            || string.Equals(currentFooterText, readyText, StringComparison.Ordinal);

        if (shouldRefreshNeutralStatus)
        {
            FooterStatusStrip.SetQueuedReady(aggregate.QueuedRunnableCount, aggregate.TotalCount);
        }
    }

    private FooterProgressState UpdateFooterProgress(QueueUiAggregateSnapshot aggregate)
    {
        QueueProgressSnapshot[] progressSnapshots = _queueUiAggregates.CaptureProgressSnapshots();

        double overallProgress = progressSnapshots.Length == 0
            ? 0
            : QueueSessionProgressAggregator.AverageOverallPercent(progressSnapshots);

        if (_coordinator.IsProcessing)
        {
            QueueRowData? activeRow = ResolveFooterActiveRow();
            int currentIndex = ResolveFooterCurrentIndex(activeRow, aggregate);
            string currentFileName = ResolveFooterCurrentFileName(activeRow);
            string phaseText = _coordinator.CancellationRequested
                ? ArabicUi.Get(MainWindowMessages.CancellingProcessingFooter)
                : ResolveFooterPhaseText(activeRow);
            bool isIndeterminate = ShouldUseIndeterminateFooterProgress(activeRow);

            FooterStatusStrip.SetProcessing(
                currentIndex: currentIndex,
                totalCount: aggregate.TotalCount,
                currentFileName: currentFileName,
                progressPercent: overallProgress,
                phaseText: phaseText,
                isIndeterminate: isIndeterminate);

            return new FooterProgressState(overallProgress, isIndeterminate);
        }

        if (_coordinator.CancellationRequested)
        {
            FooterStatusStrip.SetStoppedByUser();
            return new FooterProgressState(0d, false);
        }

        AnimateQueueProgressTo(overallProgress);
        QueueProgressText.Text = "\u200e" + $"{overallProgress:0}%";
        FooterSessionPhaseText.Text = string.Empty;
        FooterProgressStrip.Visibility = Visibility.Collapsed;

        return new FooterProgressState(overallProgress, false);
    }

    private static bool ShouldUseIndeterminateFooterProgress(QueueRowData? activeRow)
    {
        if (activeRow is null)
        {
            return true;
        }

        if (activeRow.IsIndeterminate)
        {
            return true;
        }

        return !activeRow.IsProgressActive
            && TaskQueueStateCodes.IsActiveRunning(activeRow.CurrentState);
    }

    private static bool IsFooterSessionTerminal(QueueUiAggregateSnapshot aggregate)
    {
        if (aggregate.TotalCount <= 0)
        {
            return false;
        }

        int terminalCount = aggregate.CompletedCount + aggregate.FailedCount + aggregate.SkippedCount;
        return terminalCount >= aggregate.TotalCount;
    }

    private QueueRowData? ResolveFooterActiveRow()
    {
        foreach (QueueRowData row in _queueRowStore.Rows)
        {
            if (row.IsVisibleInCurrentOperationMode
                && TaskQueueStateCodes.IsActiveRunning(row.CurrentState))
            {
                return row;
            }
        }

        return null;
    }

    private int ResolveFooterCurrentIndex(QueueRowData? activeRow, QueueUiAggregateSnapshot aggregate)
    {
        if (activeRow is not null)
        {
            IReadOnlyList<QueueRowData> rows = _queueRowStore.Rows;
            int visibleIndex = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                QueueRowData row = rows[i];
                if (!row.IsVisibleInCurrentOperationMode)
                {
                    continue;
                }

                visibleIndex++;

                if (row.ItemId == activeRow.ItemId)
                {
                    return Math.Clamp(visibleIndex, 1, Math.Max(1, aggregate.TotalCount));
                }
            }
        }

        int processedBeforeCurrent = aggregate.CompletedCount + aggregate.FailedCount + aggregate.SkippedCount;
        return Math.Clamp(processedBeforeCurrent + 1, 1, Math.Max(1, aggregate.TotalCount));
    }

    private string ResolveFooterCurrentFileName(QueueRowData? activeRow)
    {
        if (activeRow is null)
        {
            return ArabicUi.Get("LocFooter_CurrentItemUnknown");
        }

        if (!string.IsNullOrWhiteSpace(activeRow.FileName))
        {
            return activeRow.FileName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(activeRow.SourcePath))
        {
            return System.IO.Path.GetFileName(activeRow.SourcePath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(activeRow.OriginalPath))
        {
            return System.IO.Path.GetFileName(activeRow.OriginalPath.Trim());
        }

        return ArabicUi.Get("LocFooter_CurrentItemUnknown");
    }

    private string ResolveFooterPhaseText(QueueRowData? activeRow)
    {
        if (activeRow is null)
        {
            return GetSessionOperationalPhaseHeadline();
        }

        string runtimeDetail = BuildFooterRuntimeProgressText(activeRow);
        if (!string.IsNullOrWhiteSpace(runtimeDetail))
        {
            return runtimeDetail;
        }

        return ArabicUi.QueueRowOperationalPhaseHeadline(
            activeRow.CurrentState,
            activeRow.IntegrityState,
            activeRow.StatusDetail);
    }

    private static string BuildFooterRuntimeProgressText(QueueRowData row)
    {
        return string.Empty;
    }

    private static string FormatFooterTechnicalSize(long bytes)
    {
        string value = DiskSpacePreflightService.FormatBytes(Math.Max(0, bytes));
        return string.Concat(LeftToRightMark, value, LeftToRightMark);
    }

    private static string FormatFooterTechnicalProgressBytes(long currentBytes, long totalBytes)
    {
        if (totalBytes > 0 && currentBytes >= 0 && currentBytes <= totalBytes)
        {
            string current = DiskSpacePreflightService.FormatBytes(currentBytes);
            string total = DiskSpacePreflightService.FormatBytes(totalBytes);
            return string.Concat(LeftToRightMark, current, " / ", total, LeftToRightMark);
        }

        return FormatFooterTechnicalSize(currentBytes);
    }

    private static string FormatFooterTechnicalRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 0d)
        {
            return ArabicUi.Get("LocQueue_RuntimeProgress_RateCalculating");
        }

        long roundedBytes = (long)Math.Round(Math.Min(bytesPerSecond, long.MaxValue));
        return ArabicUi.Format("LocQueue_RuntimeProgress_RateFormat", FormatFooterTechnicalSize(roundedBytes));
    }

    private static string FormatFooterElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1d
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }

    private void UpdateTaskbarProgress(
        QueueUiAggregateSnapshot aggregate,
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

            _taskbarSessionProgress.NormalizedProgress = footerProgress.IsIndeterminate ? 0d : normalizedProgress;
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
        PostConversionArtifactResult sessionArtifacts)
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
                    new Action(() => BuildRunSummary(showCompletionDialog, wasCancelled, processedSelectionOnly, sessionArtifacts)),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling run summary build failed.");
            }

            return;
        }

        IReadOnlyList<SessionRunSummaryItem> targetItems = ResolveRunSummaryItems(processedSelectionOnly);
        SessionRunMetrics metrics = SessionRunMetricsCalculator.ComputeMetrics(targetItems, sessionArtifacts);
        string summaryText = LocalizedSessionRunSummaryFormatter.Format(metrics, wasCancelled);

        SetFooterStatus(wasCancelled
            ? MainWindowMessages.SessionCancelledFooter
            : MainWindowMessages.SessionCompletedFooter);

        TryNotifyTrayForBatchCompletion(wasCancelled, processedSelectionOnly, metrics);

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
            return Array.Empty<SessionRunSummaryItem>();
        }

        QueueRowData? selectedRow = _queueRowStore.GetById(selected.QueueItemId);
        return selectedRow is null
            ? Array.Empty<SessionRunSummaryItem>()
            : new[] { ToSessionRunSummaryItem(selectedRow) };
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
            FileName: NormalizeSummaryText(row.FileName),
            StatusDetail: NormalizeSummaryText(row.StatusDetail));
    }

    private static bool IsChdPath(string? path) =>
        string.Equals(System.IO.Path.GetExtension(path), ".chd", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectSupportedInputType(string? inputType) =>
        string.Equals(inputType, "ISO", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inputType, "CUE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inputType, "GDI", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inputType, "BIN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inputType, "ZIP", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inputType, "RAR", StringComparison.OrdinalIgnoreCase)
        || string.Equals(inputType, "7Z", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSummaryText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private void SetFooterIntakeProgress(
        string stageText,
        int scannedCount,
        int totalCount,
        int acceptedCount,
        bool hasKnownTotal)
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
                    new Action(() => SetFooterIntakeProgress(
                        stageText,
                        scannedCount,
                        totalCount,
                        acceptedCount,
                        hasKnownTotal)),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling footer intake progress update failed.");
            }

            return;
        }

        FooterStatusStrip.SetIntakeProgress(
            ArabicUi.ResolveDisplayString(stageText),
            scannedCount,
            totalCount,
            acceptedCount,
            hasKnownTotal);
    }

    private void SetFooterStatus(string message)
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
                    new Action(() => SetFooterStatus(message)),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduling footer status update failed.");
            }

            return;
        }

        FooterQueueSummaryText.Text = string.IsNullOrWhiteSpace(message)
            ? ArabicUi.Get(MainWindowMessages.FooterIdleNoTasks)
            : ArabicUi.ResolveDisplayString(message);
    }
}
