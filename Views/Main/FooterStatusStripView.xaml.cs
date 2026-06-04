using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Views.Main;

public partial class FooterStatusStripView : UserControl
{
    public FooterStatusStripView()
    {
        InitializeComponent();
        SetReady();
    }

    public Border RootBorder => MainFooterStatusStrip;

    public TextBlock QueueSummaryTextBlock => FooterQueueSummaryText;

    public TextBlock WaitingCountTextBlock => FooterWaitingCountText;

    public TextBlock ActiveCountTextBlock => FooterActiveCountText;

    public TextBlock CompletedCountTextBlock => FooterCompletedCountText;

    public TextBlock FailedCountTextBlock => FooterFailedCountText;

    public TextBlock SkippedCountTextBlock => FooterSkippedCountText;

    public Grid ProgressStrip => FooterProgressStrip;

    public ProgressBar SessionProgressBar => QueueProgressBar;

    public TextBlock ProgressTextBlock => QueueProgressText;

    public TextBlock SessionPhaseTextBlock => FooterSessionPhaseText;

    public TextBlock SettingsTextBlock => StatusBarSettingsText;

    public TextBlock StateTextBlock => FooterStateText;

    public Border StateDotBorder => FooterStateDot;

    public void SetReady()
    {
        SetStateText("LocFooter_StateReady", "Ready");
        SetStateDot("Brush.Accent.Text");

        FooterQueueSummaryText.Text = GetText(
            "LocFooter_IdleNoTasks",
            "No tasks in the processing queue.");

        ResetProgressDisplay();
        FooterSessionPhaseText.Text = string.Empty;

        FooterProgressStrip.Visibility = Visibility.Collapsed;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;

        SetHiddenCounters(0, 0, 0, 0, 0);
    }

    public void SetQueuedReady(int queuedCount, int totalCount)
    {
        int safeQueued = Math.Max(0, queuedCount);
        int safeTotal = Math.Max(0, totalCount);

        SetStateText("LocFooter_StateReady", "Ready");
        SetStateDot("Brush.Accent.Text");

        FooterQueueSummaryText.Text = ArabicUi.FormatText(
            GetText("LocFooterFmt_QueuedReady", "Processing queue ready: {0} runnable of {1}."),
            safeQueued,
            safeTotal);

        ResetProgressDisplay();
        FooterSessionPhaseText.Text = string.Empty;

        FooterProgressStrip.Visibility = Visibility.Collapsed;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    public void SetProcessing(
        int currentIndex,
        int totalCount,
        string? currentFileName,
        double progressPercent,
        string? phaseText,
        bool isIndeterminate)
    {
        int safeCurrent = Math.Max(0, currentIndex);
        int safeTotal = Math.Max(0, totalCount);

        string safeFileName = string.IsNullOrWhiteSpace(currentFileName)
            ? GetText("LocFooter_CurrentItemUnknown", "Current item")
            : currentFileName.Trim();

        SetStateText("LocFooter_StateProcessing", "Processing");
        SetStateDot("Brush.Accent.Text");

        FooterQueueSummaryText.Text = ArabicUi.FormatText(
            GetText("LocFooterFmt_Processing", "Processing {0} of {1}: {2}"),
            safeCurrent,
            safeTotal,
            safeFileName);

        FooterSessionPhaseText.Text = string.IsNullOrWhiteSpace(phaseText)
            ? GetText("LocFooter_PhaseProcessing", "Processing")
            : phaseText.Trim();

        SetProgressDisplay(progressPercent, isIndeterminate);

        FooterProgressStrip.Visibility = Visibility.Visible;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    public void SetIntakeProgress(
        string? stageText,
        int scannedCount,
        int totalCount,
        int acceptedCount,
        bool hasKnownTotal)
    {
        int safeScanned = Math.Max(0, scannedCount);
        int safeTotal = Math.Max(0, totalCount);
        int safeAccepted = Math.Max(0, acceptedCount);
        string safeStageText = string.IsNullOrWhiteSpace(stageText)
            ? GetText("LocQueueAdd_ScanningFiles", "Adding files...")
            : stageText.Trim();

        string addedText = ArabicUi.FormatText(
            GetText("LocQueueAdd_AddedFilesInProgressFormat", "Added {0} files."),
            safeAccepted);

        FooterQueueSummaryText.Text = string.Concat(safeStageText, " ", addedText);
        FooterSessionPhaseText.Text = GetText("LocQueueAdd_Title", "Adding files");

        SetStateText("LocQueueAdd_Title", "Adding files");
        SetStateDot("Brush.Accent.Text");

        bool isIndeterminate = !hasKnownTotal || safeTotal <= 0;
        double progressPercent = isIndeterminate
            ? 0d
            : Math.Clamp((safeScanned / (double)safeTotal) * 100d, 0d, 100d);

        SetProgressDisplay(progressPercent, isIndeterminate);

        FooterProgressStrip.Visibility = Visibility.Visible;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    public void SetStoppedByUser()
    {
        SetStateText("LocFooter_StateStopped", "Stopped");
        SetStateDot("Brush.Text.Secondary");

        FooterQueueSummaryText.Text = GetText(
            "LocFooter_StoppedByUser",
            "Processing was stopped by the user.");

        ResetProgressDisplay();
        FooterSessionPhaseText.Text = string.Empty;

        FooterProgressStrip.Visibility = Visibility.Collapsed;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    public void SetCompleted(int completedCount, int failedCount, int skippedCount)
    {
        int safeCompleted = Math.Max(0, completedCount);
        int safeFailed = Math.Max(0, failedCount);
        int safeSkipped = Math.Max(0, skippedCount);

        SetHiddenCounters(
            waiting: 0,
            active: 0,
            completed: safeCompleted,
            failed: safeFailed,
            skipped: safeSkipped);

        string summaryFormatKey = "LocFooterFmt_Completed";
        string summaryFallback = "Session completed: {0} succeeded, {1} failed, {2} skipped.";

        if (safeCompleted <= 0 && safeFailed > 0)
        {
            SetStateText("LocFooter_StateFailed", "Failed");
            SetStateDot("Brush.Text.Secondary");
            summaryFormatKey = "LocFooterFmt_Failed";
            summaryFallback = "Processing failed: {0} succeeded, {1} failed, {2} skipped.";
        }
        else if (safeFailed > 0)
        {
            SetStateText("LocFooter_StateCompletedWithErrors", "Completed with errors");
            SetStateDot("Brush.Text.Secondary");
            summaryFormatKey = "LocFooterFmt_CompletedWithErrors";
            summaryFallback = "Processing completed with errors: {0} succeeded, {1} failed, {2} skipped.";
        }
        else
        {
            SetStateText("LocFooter_StateCompleted", "Completed");
            SetStateDot("Brush.Accent.Text");
        }

        FooterQueueSummaryText.Text = ArabicUi.FormatText(
            GetText(summaryFormatKey, summaryFallback),
            safeCompleted,
            safeFailed,
            safeSkipped);

        SetProgressDisplay(100d, isIndeterminate: false);
        FooterSessionPhaseText.Text = string.Empty;

        FooterProgressStrip.Visibility = Visibility.Collapsed;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    public void SetFailed(string? message)
    {
        SetStateText("LocFooter_StateError", "Error");
        SetStateDot("Brush.Text.Secondary");

        FooterQueueSummaryText.Text = string.IsNullOrWhiteSpace(message)
            ? GetText("LocFooter_ErrorGeneric", "The operation could not be completed.")
            : message.Trim();

        ResetProgressDisplay();
        FooterProgressStrip.Visibility = Visibility.Collapsed;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    public void SetSettingsMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusBarSettingsText.Text = string.Empty;
            StatusBarSettingsText.Visibility = Visibility.Collapsed;
            return;
        }

        ResetProgressDisplay();
        FooterProgressStrip.Visibility = Visibility.Collapsed;

        StatusBarSettingsText.Text = message.Trim();
        StatusBarSettingsText.Visibility = Visibility.Visible;
    }

    public void SetHiddenCounters(
        int waiting,
        int active,
        int completed,
        int failed,
        int skipped)
    {
        FooterWaitingCountText.Text = Math.Max(0, waiting).ToString(CultureInfo.CurrentCulture);
        FooterActiveCountText.Text = Math.Max(0, active).ToString(CultureInfo.CurrentCulture);
        FooterCompletedCountText.Text = Math.Max(0, completed).ToString(CultureInfo.CurrentCulture);
        FooterFailedCountText.Text = Math.Max(0, failed).ToString(CultureInfo.CurrentCulture);
        FooterSkippedCountText.Text = Math.Max(0, skipped).ToString(CultureInfo.CurrentCulture);
    }

    private void SetProgressDisplay(double progressPercent, bool isIndeterminate)
    {
        double safePercent = Math.Clamp(progressPercent, 0d, 100d);

        QueueProgressBar.IsIndeterminate = isIndeterminate;
        QueueProgressBar.Value = isIndeterminate ? 0d : safePercent;

        if (isIndeterminate)
        {
            QueueProgressText.Text = "\u200E…";
            QueueProgressText.Visibility = Visibility.Visible;
            return;
        }

        QueueProgressText.Text = "\u200E" + safePercent.ToString("0", CultureInfo.InvariantCulture) + "%";
        QueueProgressText.Visibility = Visibility.Visible;
    }

    private void ResetProgressDisplay()
    {
        QueueProgressBar.IsIndeterminate = false;
        QueueProgressBar.Value = 0d;
        QueueProgressText.Text = "\u200E0%";
        QueueProgressText.Visibility = Visibility.Visible;
    }

    private void SetStateText(string resourceKey, string fallback)
    {
        FooterStateText.Text = GetText(resourceKey, fallback);
    }

    private void SetStateDot(string brushKey)
    {
        if (TryFindResource(brushKey) is Brush brush)
        {
            FooterStateDot.Background = brush;
        }
    }

    private string GetText(string resourceKey, string fallback)
    {
        return TryFindResource(resourceKey) as string ?? fallback;
    }
}