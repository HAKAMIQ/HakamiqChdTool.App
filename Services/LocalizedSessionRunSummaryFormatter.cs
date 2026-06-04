using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using System;
using System.Text;

namespace HakamiqChdTool.App.Services;

public static class LocalizedSessionRunSummaryFormatter
{
    public static string Format(SessionRunMetrics metrics, bool wasCancelled)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var builder = new StringBuilder();

        builder.AppendLine(wasCancelled
            ? ArabicUi.Get(MainWindowMessages.SessionEndedUserCancel)
            : ArabicUi.Get(MainWindowMessages.SessionEnded));

        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionTotalItems) + metrics.Total);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionCompleted) + metrics.Completed);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionFailed) + metrics.Failed);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionSkipped) + metrics.Skipped);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionCancelled) + metrics.Cancelled);
        builder.AppendLine();
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionFeatureReport));
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionDirectSupported) + metrics.DirectSupported);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionReverseSupported) + metrics.ReverseSupported);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionAvgCompression) + "\u200e" + $"{metrics.AvgCompressionPercent:0.0}%");
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionRedumpMatches) + metrics.RedumpMatched);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionSavedGb)
            + $"{metrics.SavedBytes / (1024d * 1024d * 1024d):0.00}"
            + ArabicUi.Get(MainWindowMessages.SessionGbUnit));
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionCleanupGb)
            + $"{metrics.DeletedBytes / (1024d * 1024d * 1024d):0.00}"
            + ArabicUi.Get(MainWindowMessages.SessionGbUnit));
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionSbiCopied) + metrics.SbiCopiedCount);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionM3uGenerated) + metrics.M3uGeneratedCount);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionM3uSkippedExisting) + metrics.M3uSkippedExistingCount);
        builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionPostProcessingWarnings) + metrics.PostProcessingFailureCount);

        if (metrics.FailedItems.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionItemsNeedReview));

            foreach (SessionRunFailedItem failedItem in metrics.FailedItems)
            {
                builder.Append("- ");
                builder.Append(FormatFileName(failedItem.FileName));
                builder.Append(": ");
                builder.AppendLine(FormatStatusDetailDisplay(failedItem.StatusDetail));
            }
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine(ArabicUi.Get(MainWindowMessages.SessionNoFailedItems));
        }

        return builder.ToString().Trim();
    }

    private static string FormatFileName(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? "-" : fileName.Trim();

    private static string FormatStatusDetailDisplay(string? statusDetail) =>
        string.IsNullOrWhiteSpace(statusDetail) ? "-" : ArabicUi.ResolveDisplayString(statusDetail.Trim());
}
