using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IoPath = System.IO.Path;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const string ExecutionLogTimestampFormat = "HH:mm:ss";

    private void AppendExecutionLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
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
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
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

                    if (_shutdownStarted ||
                        _shutdownCompleted ||
                        Dispatcher.HasShutdownStarted ||
                        Dispatcher.HasShutdownFinished)
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
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
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

    private DateTime? _runStartedUtc;
    private string? _lastRunSummaryText;
    private bool _lastRunSummaryCancelled;

    private void UpdateUiStateCore()
    {
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
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
        ScheduleLaunchDrain();
    }

    private void ResetRunSummary()
    {
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
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
        _runStartedUtc = DateTime.UtcNow;
        QueueWorkspace.HideRunSummary();
    }

    private void RunAfterRunAction(HakamiqChdTool.App.Services.Power.AfterRunAction action)
    {
        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        var dialog = new HakamiqChdTool.App.Views.AfterRunCountdownDialog(action, HakamiqChdTool.App.Services.Power.AfterRunPower.Countdown);
        if (IsVisible)
        {
            dialog.Owner = this;
        }

        if (dialog.ShowDialog() == true && !HakamiqChdTool.App.Services.Power.AfterRunPower.TryExecute(action))
        {
            SetFooterStatus(ArabicUi.Get("LocAfterRun_Failed"));
        }
    }

    private void QueueWorkspace_RunSummaryDetailsRequested(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastRunSummaryText))
        {
            return;
        }

        // The report is already localized and mixes in technical terms (GB, M3U), so it is shown as is
        // instead of through ShowNoticeDialog, whose resolver replaces mixed-script text.
        var dialog = new HakamiqChdTool.App.Views.RedumpNoticeDialog(
            ArabicUi.Get(_lastRunSummaryCancelled
                ? MainWindowMessages.SummaryCancelledTitle
                : MainWindowMessages.SummaryCompletedTitle),
            _lastRunSummaryText)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();
    }

    private void BuildRunSummary(
        bool showCompletionDialog,
        bool wasCancelled,
        bool processedSelectionOnly,
        PostConversionArtifactResult runArtifacts)
    {
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
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

        _lastRunSummaryText = summaryText;
        _lastRunSummaryCancelled = wasCancelled;
        TimeSpan elapsed = _runStartedUtc is DateTime started ? DateTime.UtcNow - started : TimeSpan.Zero;
        RunSummaryCard runSummaryCard = RunSummaryCardBuilder.Build(metrics, wasCancelled, elapsed, ArabicUi.Get);
        QueueWorkspace.ShowRunSummary(runSummaryCard);

        HakamiqChdTool.App.Services.Power.AfterRunAction afterRun = QueueWorkspace.SelectedAfterRunAction;
        if (HakamiqChdTool.App.Services.Power.AfterRunPower.ShouldRun(afterRun, wasCancelled, processedSelectionOnly))
        {
            QueueWorkspace.ResetAfterRunAction();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RunAfterRunAction(afterRun)));
        }
        else if (ExitWhenDone && !processedSelectionOnly)
        {
            int exitCode = HakamiqChdTool.App.Startup.LaunchArgs.ExitCodeFor(wasCancelled, metrics.Failed);
            Log.Information("Run finished for a launch with --exit-when-done. Exiting with code {ExitCode}.", exitCode);
            RequestLaunchExit(exitCode);
        }

        SetFooterStatus(wasCancelled
            ? MainWindowMessages.SessionCancelledFooter
            : MainWindowMessages.SessionCompletedFooter);

        TryNotifyTrayForBatchCompletion(
            processedSelectionOnly,
            runSummaryCard);

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

    private static SessionRunSummaryItem ToSessionRunSummaryItem(QueueRowData row) =>
        HakamiqChdTool.App.QueueRun.RunSummaryItems.FromRow(row);
}
