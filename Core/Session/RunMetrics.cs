using HakamiqChdTool.App.Models;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Core.Session;

public sealed record SessionRunFailedItem(
    string FileName,
    string StatusDetail);

public sealed record SessionRunMetrics(
    int Total,
    int Completed,
    int Failed,
    int Skipped,
    int Cancelled,
    int ReverseSupported,
    int DirectSupported,
    int RedumpMatched,
    long DeletedBytes,
    long SavedBytes,
    int SbiCopiedCount,
    int M3uGeneratedCount,
    int M3uSkippedExistingCount,
    int PostProcessingFailureCount,
    double AvgCompressionPercent,
    IReadOnlyList<ConversionPerformanceReport> ConversionReports,
    IReadOnlyList<SessionRunFailedItem> FailedItems);
