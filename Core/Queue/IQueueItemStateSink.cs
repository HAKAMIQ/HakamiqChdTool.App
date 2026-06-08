using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.Conversion;

namespace HakamiqChdTool.App.Core.Queue;

public interface IQueueItemStateSink
{
    void ResetForRun();

    void ReportStage(QueueItemStage stage, string? detail);

    void ReportProgress(double percent, bool indeterminate);

    void ReportRuntimeProgress(QueueRuntimeProgressSnapshot snapshot);

    void ClearRuntimeProgress();

    void ReportTerminalSuccess(QueueItemTerminalOutcome outcome, string? detail);

    void ReportTerminalFailure(QueueItemFailureKind kind, string? detail);

    void AttachArtifact(QueueItemArtifactKind kind, string path);

    void RecordPlatformDetection(string platform, string reason);

    void RecordInputOutputBytes(long inputBytes, long outputBytes);

    void AddCleanupDeletedBytes(long deltaBytes);

    void RecordPostConversionArtifacts(PostConversionArtifactResult result);

    void RecordConversionPerformanceReport(ConversionPerformanceReport report);

    void ReportWorkingPathPromotion(string newWorkingPath, string newRequestedAction);

    void RestoreArchiveSourceState();
}
