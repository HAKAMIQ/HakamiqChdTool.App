using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Core.Session;

public static class SessionRunMetricsCalculator
{
    public static SessionRunMetrics ComputeMetrics(IReadOnlyList<SessionRunSummaryItem> targetItems) =>
        ComputeMetrics(targetItems, PostConversionArtifactResult.Empty);

    public static SessionRunMetrics ComputeMetrics(
        IReadOnlyList<SessionRunSummaryItem> targetItems,
        PostConversionArtifactResult sessionArtifacts)
    {
        ArgumentNullException.ThrowIfNull(targetItems);
        ArgumentNullException.ThrowIfNull(sessionArtifacts);

        int total = targetItems.Count;
        int completed = targetItems.Count(t => t.IsCompleted);
        int failed = targetItems.Count(t => t.IsFailed);
        int skipped = targetItems.Count(t => t.IsSkipped);
        int cancelled = targetItems.Count(t => t.IsCancelled);
        int reverseSupported = targetItems.Count(t => t.IsReverseSupported);
        int directSupported = targetItems.Count(t => t.IsDirectSupported);
        int redumpMatched = targetItems.Count(t => t.IsRedumpMatched);
        long deletedBytes = targetItems.Sum(t => t.DeletedBytes);
        int sbiCopiedCount = targetItems.Sum(t => Math.Max(0, t.SbiCopiedCount));
        int postProcessingFailureCount = targetItems.Sum(t => Math.Max(0, t.PostProcessingFailureCount))
            + sessionArtifacts.FailedArtifactCount;
        long savedBytes = targetItems
            .Where(t => t.IsCompleted && t.InputBytes > 0 && t.OutputBytes > 0)
            .Sum(t => Math.Max(0, t.InputBytes - t.OutputBytes));

        double avgCompression = targetItems
            .Where(t => t.OutputBytes > 0 && t.InputBytes > 0)
            .Select(t => ComputeCompressionRatioPercent(t.InputBytes, t.OutputBytes))
            .DefaultIfEmpty(0)
            .Average();

        SessionRunFailedItem[] failedItems =
        [
            .. targetItems
                .Where(t => t.IsFailed)
                .Select(t => new SessionRunFailedItem(t.FileName, t.StatusDetail))
        ];

        return new SessionRunMetrics(
            total,
            completed,
            failed,
            skipped,
            cancelled,
            reverseSupported,
            directSupported,
            redumpMatched,
            deletedBytes,
            savedBytes,
            sbiCopiedCount,
            sessionArtifacts.M3uGeneratedCount,
            sessionArtifacts.M3uSkippedExistingCount,
            postProcessingFailureCount,
            avgCompression,
            failedItems);
    }

    private static double ComputeCompressionRatioPercent(long inputBytes, long outputBytes) =>
        inputBytes <= 0 ? 0 : Math.Clamp((1.0 - (outputBytes / (double)inputBytes)) * 100.0, -500, 100);
}