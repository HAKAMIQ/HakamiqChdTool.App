using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Core.Session;

public sealed record SessionRunSummaryItem(
    bool IsCompleted,
    bool IsFailed,
    bool IsSkipped,
    bool IsCancelled,
    bool IsReverseSupported,
    bool IsDirectSupported,
    bool IsRedumpMatched,
    long DeletedBytes,
    int SbiCopiedCount,
    int PostProcessingFailureCount,
    long InputBytes,
    long OutputBytes,
    ConversionPerformanceReport? ConversionPerformanceReport,
    string FileName,
    string StatusDetail);
