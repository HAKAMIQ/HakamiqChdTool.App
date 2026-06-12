using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using Serilog;

using IoPath = System.IO.Path;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private readonly record struct FooterProgressState(
        double Percent,
        bool IsIndeterminate);

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

    private string GetSessionOperationalPhaseHeadline()
    {
        return ArabicUi.Get("LocState_Processing");
    }

    private void UpdateFooterSummary(
        QueueUiSnapshot aggregate,
        bool hasTasks)
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
            string.IsNullOrWhiteSpace(currentFooterText) ||
            string.Equals(currentFooterText, idleText, StringComparison.Ordinal) ||
            string.Equals(currentFooterText, readyText, StringComparison.Ordinal);

        if (shouldRefreshNeutralStatus)
        {
            FooterStatusStrip.SetQueuedReady(
                aggregate.QueuedRunnableCount,
                aggregate.TotalCount);
        }
    }

    private FooterProgressState UpdateFooterProgress(QueueUiSnapshot aggregate)
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
        QueueProgressText.Text = string.Concat(LeftToRightMark, $"{overallProgress:0}%");
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

        return !activeRow.IsProgressActive &&
            TaskQueueStateCodes.IsActiveRunning(activeRow.CurrentState);
    }

    private static bool IsFooterSessionTerminal(QueueUiSnapshot aggregate)
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
            if (row.IsVisibleInCurrentOperationMode &&
                TaskQueueStateCodes.IsActiveRunning(row.CurrentState))
            {
                return row;
            }
        }

        return null;
    }

    private int ResolveFooterCurrentIndex(
        QueueRowData? activeRow,
        QueueUiSnapshot aggregate)
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
                    return Math.Clamp(
                        visibleIndex,
                        1,
                        Math.Max(1, aggregate.TotalCount));
                }
            }
        }

        int processedBeforeCurrent = aggregate.CompletedCount + aggregate.FailedCount + aggregate.SkippedCount;

        return Math.Clamp(
            processedBeforeCurrent + 1,
            1,
            Math.Max(1, aggregate.TotalCount));
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
            return IoPath.GetFileName(activeRow.SourcePath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(activeRow.OriginalPath))
        {
            return IoPath.GetFileName(activeRow.OriginalPath.Trim());
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

    private static string FormatFooterTechnicalProgressBytes(
        long currentBytes,
        long totalBytes)
    {
        if (totalBytes > 0 &&
            currentBytes >= 0 &&
            currentBytes <= totalBytes)
        {
            string current = DiskSpacePreflightService.FormatBytes(currentBytes);
            string total = DiskSpacePreflightService.FormatBytes(totalBytes);

            return string.Concat(
                LeftToRightMark,
                current,
                " / ",
                total,
                LeftToRightMark);
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

        return ArabicUi.Format(
            "LocQueue_RuntimeProgress_RateFormat",
            FormatFooterTechnicalSize(roundedBytes));
    }

    private static string FormatFooterElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1d
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }

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

        string resolvedMessage = string.IsNullOrWhiteSpace(message)
            ? ArabicUi.Get(MainWindowMessages.FooterIdleNoTasks)
            : ArabicUi.ResolveDisplayString(message);

        FooterStatusStrip.SetStatusMessage(resolvedMessage);
    }
}