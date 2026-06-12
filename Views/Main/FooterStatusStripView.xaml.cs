using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Views.Main;

public partial class FooterStatusStripView : UserControl
{
    private const string LeftToRightPrefix = "\u200E";
    private string _stateDotBrushKey = "Brush.Accent.Text";

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
        SetFooterState("LocFooter_StateReady", "Ready", "Brush.Accent.Text");

        FooterQueueSummaryText.Text = GetText(
            "LocFooter_IdleNoTasks",
            "No tasks in the processing queue.");

        ResetProgressDisplay();
        ClearSessionPhase();
        HideProgressAndSettings();
        SetHiddenCounters(0, 0, 0, 0, 0);
    }

    public void SetQueuedReady(int queuedCount, int totalCount)
    {
        int safeQueued = NonNegative(queuedCount);
        int safeTotal = NonNegative(totalCount);

        SetFooterState("LocFooter_StateReady", "Ready", "Brush.Accent.Text");

        FooterQueueSummaryText.Text = ArabicUi.FormatText(
            GetText("LocFooterFmt_QueuedReady", "Processing queue ready: {0} runnable of {1}."),
            safeQueued,
            safeTotal);

        ResetProgressDisplay();
        ClearSessionPhase();
        HideProgressAndSettings();
    }

    public void SetProcessing(
        int currentIndex,
        int totalCount,
        string? currentFileName,
        double progressPercent,
        string? phaseText,
        bool isIndeterminate)
    {
        int safeCurrent = NonNegative(currentIndex);
        int safeTotal = NonNegative(totalCount);
        string safeFileName = CleanText(
            currentFileName,
            GetText("LocFooter_CurrentItemUnknown", "Current item"));

        SetFooterState("LocFooter_StateProcessing", "Processing", "Brush.Accent.Text");

        FooterQueueSummaryText.Text = ArabicUi.FormatText(
            GetText("LocFooterFmt_Processing", "Processing {0} of {1}: {2}"),
            safeCurrent,
            safeTotal,
            safeFileName);

        FooterSessionPhaseText.Text = CleanText(
            phaseText,
            GetText("LocFooter_PhaseProcessing", "Processing"));

        SetProgressDisplay(progressPercent, isIndeterminate);
        ShowProgressOnly();
    }

    public void SetIntakeProgress(
        string? stageText,
        int scannedCount,
        int totalCount,
        int acceptedCount,
        bool hasKnownTotal)
    {
        int safeScanned = NonNegative(scannedCount);
        int safeTotal = NonNegative(totalCount);
        int safeAccepted = NonNegative(acceptedCount);

        string safeStageText = CleanText(
            stageText,
            GetText("LocQueueAdd_ScanningFiles", "Adding files..."));

        string addedText = ArabicUi.FormatText(
            GetText("LocQueueAdd_AddedFilesInProgressFormat", "Added {0} files."),
            safeAccepted);

        FooterQueueSummaryText.Text = string.Concat(safeStageText, " ", addedText);
        FooterSessionPhaseText.Text = GetText("LocQueueAdd_Title", "Adding files");

        SetFooterState("LocQueueAdd_Title", "Adding files", "Brush.Accent.Text");

        bool isIndeterminate = !hasKnownTotal || safeTotal <= 0;
        double progressPercent = isIndeterminate
            ? 0d
            : Math.Clamp((safeScanned / (double)safeTotal) * 100d, 0d, 100d);

        SetProgressDisplay(progressPercent, isIndeterminate);
        ShowProgressOnly();
    }

    public void SetStoppedByUser()
    {
        SetFooterState("LocFooter_StateStopped", "Stopped", "Brush.Text.Secondary");

        FooterQueueSummaryText.Text = GetText(
            "LocFooter_StoppedByUser",
            "Processing was stopped by the user.");

        ResetProgressDisplay();
        ClearSessionPhase();
        HideProgressAndSettings();
    }

    public void SetCompleted(int completedCount, int failedCount, int skippedCount)
    {
        int safeCompleted = NonNegative(completedCount);
        int safeFailed = NonNegative(failedCount);
        int safeSkipped = NonNegative(skippedCount);

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
            SetFooterState("LocFooter_StateFailed", "Failed", "Brush.Text.Secondary");
            summaryFormatKey = "LocFooterFmt_Failed";
            summaryFallback = "Processing failed: {0} succeeded, {1} failed, {2} skipped.";
        }
        else if (safeFailed > 0)
        {
            SetFooterState("LocFooter_StateCompletedWithErrors", "Completed with errors", "Brush.Text.Secondary");
            summaryFormatKey = "LocFooterFmt_CompletedWithErrors";
            summaryFallback = "Processing completed with errors: {0} succeeded, {1} failed, {2} skipped.";
        }
        else
        {
            SetFooterState("LocFooter_StateCompleted", "Completed", "Brush.Accent.Text");
        }

        FooterQueueSummaryText.Text = ArabicUi.FormatText(
            GetText(summaryFormatKey, summaryFallback),
            safeCompleted,
            safeFailed,
            safeSkipped);

        SetProgressDisplay(100d, isIndeterminate: false);
        ClearSessionPhase();
        HideProgressAndSettings();
    }

    public void SetStatusMessage(string? message)
    {
        SetFooterState("LocFooter_StateReady", "Ready", "Brush.Accent.Text");

        FooterQueueSummaryText.Text = CleanText(
            message,
            GetText("LocFooter_IdleNoTasks", "No tasks in the processing queue."));

        ResetProgressDisplay();
        ClearSessionPhase();
        HideProgressAndSettings();
    }

    public void SetFailed(string? message)
    {
        SetFooterState("LocFooter_StateError", "Error", "Brush.Text.Secondary");

        FooterQueueSummaryText.Text = CleanText(
            message,
            GetText("LocFooter_ErrorGeneric", "The operation could not be completed."));

        ResetProgressDisplay();
        HideProgressAndSettings();
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
        FooterWaitingCountText.Text = FormatCount(waiting);
        FooterActiveCountText.Text = FormatCount(active);
        FooterCompletedCountText.Text = FormatCount(completed);
        FooterFailedCountText.Text = FormatCount(failed);
        FooterSkippedCountText.Text = FormatCount(skipped);
    }

    public void RefreshThemeBrushes()
    {
        SetStateDot(_stateDotBrushKey);
    }

    private static int NonNegative(int value)
    {
        return Math.Max(0, value);
    }

    private static string FormatCount(int value)
    {
        return NonNegative(value).ToString(CultureInfo.CurrentCulture);
    }

    private static string CleanText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private void SetFooterState(string resourceKey, string fallback, string brushKey)
    {
        FooterStateText.Text = GetText(resourceKey, fallback);
        SetStateDot(brushKey);
    }

    private void SetProgressDisplay(double progressPercent, bool isIndeterminate)
    {
        double safePercent = Math.Clamp(progressPercent, 0d, 100d);

        QueueProgressBar.IsIndeterminate = isIndeterminate;
        QueueProgressBar.Value = isIndeterminate ? 0d : safePercent;

        QueueProgressText.Text = isIndeterminate
            ? LeftToRightPrefix + "..."
            : LeftToRightPrefix + safePercent.ToString("0", CultureInfo.InvariantCulture) + "%";

        QueueProgressText.Visibility = Visibility.Visible;
    }

    private void ResetProgressDisplay()
    {
        QueueProgressBar.IsIndeterminate = false;
        QueueProgressBar.Value = 0d;
        QueueProgressText.Text = LeftToRightPrefix + "0%";
        QueueProgressText.Visibility = Visibility.Visible;
    }

    private void ShowProgressOnly()
    {
        FooterProgressStrip.Visibility = Visibility.Visible;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    private void HideProgressAndSettings()
    {
        FooterProgressStrip.Visibility = Visibility.Collapsed;
        StatusBarSettingsText.Visibility = Visibility.Collapsed;
    }

    private void ClearSessionPhase()
    {
        FooterSessionPhaseText.Text = string.Empty;
    }

    private void SetStateDot(string brushKey)
    {
        if (!string.IsNullOrWhiteSpace(brushKey))
        {
            _stateDotBrushKey = brushKey;
        }

        if (TryFindResource(_stateDotBrushKey) is Brush brush)
        {
            FooterStateDot.Background = brush;
        }
    }

    private string GetText(string resourceKey, string fallback)
    {
        return TryFindResource(resourceKey) as string ?? fallback;
    }
}