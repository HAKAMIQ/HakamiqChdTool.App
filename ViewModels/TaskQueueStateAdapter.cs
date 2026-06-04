using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;

namespace HakamiqChdTool.App.ViewModels;

public sealed class TaskQueueStateAdapter : IQueueItemStateSink
{
    private readonly Guid _recordId;
    private readonly QueueRowStore _rowStore;

    public TaskQueueStateAdapter(
        Guid recordId,
        QueueRowStore rowStore)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("Record id must not be empty.", nameof(recordId));
        }

        _recordId = recordId;
        _rowStore = rowStore ?? throw new ArgumentNullException(nameof(rowStore));
    }

    public void ResetForRun()
    {
        MutateNonTerminal(row =>
        {
            row.Progress = 0;
            row.IsProgressActive = false;
            row.IsIndeterminate = false;
            ClearRuntimeProgressFields(row);
            row.InputBytes = 0;
            row.OutputBytes = 0;
            row.CleanupDeletedBytes = 0;
            row.SbiCopiedCount = 0;
            row.PostProcessingFailureCount = 0;
        });
    }

    public void ReportStage(QueueItemStage stage, string? detail)
    {
        string stateCode = TaskQueueIntentMap.ToStateCode(stage);

        MutateNonTerminal(row =>
        {
            row.CurrentState = stateCode;

            if (detail is not null)
            {
                row.StatusDetail = detail;
            }
        });
    }

    public void ReportProgress(double percent, bool indeterminate)
    {
        MutateNonTerminal(row =>
        {
            double nextProgress = Math.Clamp(percent, 0, 99);
            if (nextProgress < row.Progress)
            {
                return;
            }

            row.Progress = nextProgress;
            row.IsProgressActive = true;
            row.IsIndeterminate = indeterminate;
        });
    }

    public void ReportRuntimeProgress(QueueRuntimeProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        MutateNonTerminal(row =>
        {
            row.RuntimeProgressKind = snapshot.Kind;
            row.RuntimeProgressPrimaryMessageKey = snapshot.PrimaryMessageKey;
            row.RuntimeProgressCurrentBytes = Math.Max(0, snapshot.CurrentBytes);
            row.RuntimeProgressTotalBytes = Math.Max(0, snapshot.TotalBytes);
            row.RuntimeProgressBytesPerSecond = double.IsFinite(snapshot.BytesPerSecond) ? Math.Max(0d, snapshot.BytesPerSecond) : 0d;
            row.RuntimeProgressPercent = double.IsFinite(snapshot.Percent) ? Math.Clamp(snapshot.Percent, 0d, 100d) : 0d;
            row.RuntimeProgressElapsedTicks = Math.Max(0, snapshot.Elapsed.Ticks);
            row.RuntimeProgressEstimatedRemainingTicks = Math.Max(0, snapshot.EstimatedRemaining.Ticks);
            row.RuntimeProgressNextStageMessageKey = snapshot.NextStageMessageKey;
            row.RuntimeProgressShowActivitySpinner = snapshot.ShowActivitySpinner;
        });
    }

    public void ClearRuntimeProgress()
    {
        MutateBoth(ClearRuntimeProgressFields);
    }

    public void ReportTerminalSuccess(QueueItemTerminalOutcome outcome, string? detail)
    {
        string stateCode = TaskQueueIntentMap.ToStateCode(outcome);
        string finalResult = TaskQueueIntentMap.ToFinalResult(outcome);

        MutateTerminal(row =>
        {
            row.CurrentState = stateCode;
            row.FinalResult = finalResult;
            row.IsProgressActive = false;
            row.IsIndeterminate = false;
            ClearRuntimeProgressFields(row);

            if (stateCode == TaskQueueStateCodes.Completed)
            {
                row.Progress = 100;
            }

            if (detail is not null)
            {
                row.StatusDetail = detail;
            }
        });
    }

    public void ReportTerminalFailure(QueueItemFailureKind kind, string? detail)
    {
        string stateCode = TaskQueueIntentMap.ToStateCode(kind);
        string finalResult = TaskQueueIntentMap.ToFinalResult(kind);

        MutateTerminal(row =>
        {
            row.CurrentState = stateCode;
            row.FinalResult = finalResult;
            row.IsProgressActive = false;
            row.IsIndeterminate = false;
            ClearRuntimeProgressFields(row);

            if (detail is not null)
            {
                row.StatusDetail = detail;
            }
        });
    }

    public void AttachArtifact(QueueItemArtifactKind kind, string path)
    {
        MutateBoth(row =>
        {
            switch (kind)
            {
                case QueueItemArtifactKind.OutputFile:
                    row.OutputPath = path;
                    break;

                case QueueItemArtifactKind.LogFile:
                    row.LogPath = path;
                    break;

                case QueueItemArtifactKind.TempWorkingDirectory:
                    row.TempWorkingDirectory = path;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown queue artifact kind.");
            }
        });
    }

    public void RecordPlatformDetection(string platform, string reason)
    {
        MutateNonTerminal(row =>
        {
            row.DetectedPlatform = platform;
            row.DetectionReason = reason;
        });
    }

    public void RecordInputOutputBytes(long inputBytes, long outputBytes)
    {
        MutateNonTerminal(row =>
        {
            row.InputBytes = Math.Max(0, inputBytes);
            row.OutputBytes = Math.Max(0, outputBytes);
        });
    }

    public void AddCleanupDeletedBytes(long deltaBytes)
    {
        if (deltaBytes <= 0)
        {
            return;
        }

        MutateNonTerminal(row =>
        {
            row.CleanupDeletedBytes += deltaBytes;
        });
    }

    public void RecordPostConversionArtifacts(PostConversionArtifactResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.SbiCopiedCount <= 0 && result.FailedArtifactCount <= 0)
        {
            return;
        }

        MutateNonTerminal(row =>
        {
            row.SbiCopiedCount += Math.Max(0, result.SbiCopiedCount);
            row.PostProcessingFailureCount += Math.Max(0, result.FailedArtifactCount);
        });
    }

    public void ReportWorkingPathPromotion(string newWorkingPath, string newRequestedAction)
    {
        MutateNonTerminal(row =>
        {
            row.SourcePath = newWorkingPath;
            row.RequestedAction = newRequestedAction;
        });
    }

    public void RestoreArchiveSourceState()
    {
        MutateBoth(row =>
        {
            row.SourcePath = string.Empty;
            row.RequestedAction = TaskActionCodes.StageArchiveForConversion;
            row.TempWorkingDirectory = string.Empty;
        });
    }

    private static void ClearRuntimeProgressFields(QueueRowData row)
    {
        row.RuntimeProgressKind = QueueRuntimeProgressKind.None;
        row.RuntimeProgressPrimaryMessageKey = string.Empty;
        row.RuntimeProgressCurrentBytes = 0;
        row.RuntimeProgressTotalBytes = 0;
        row.RuntimeProgressBytesPerSecond = 0d;
        row.RuntimeProgressPercent = 0d;
        row.RuntimeProgressElapsedTicks = 0;
        row.RuntimeProgressEstimatedRemainingTicks = 0;
        row.RuntimeProgressNextStageMessageKey = string.Empty;
        row.RuntimeProgressShowActivitySpinner = false;
    }

    private void MutateNonTerminal(Action<QueueRowData> rowPatch)
    {
        ArgumentNullException.ThrowIfNull(rowPatch);

        _rowStore.Mutate(_recordId, row =>
        {
            if (TaskQueueStateCodes.IsTerminal(row.CurrentState))
            {
                return;
            }

            rowPatch(row);
        });
    }

    private void MutateTerminal(Action<QueueRowData> rowPatch)
    {
        ArgumentNullException.ThrowIfNull(rowPatch);

        _rowStore.Mutate(_recordId, row =>
        {
            if (TaskQueueStateCodes.IsTerminal(row.CurrentState))
            {
                return;
            }

            rowPatch(row);
        });
    }

    private void MutateBoth(Action<QueueRowData> rowPatch)
    {
        ArgumentNullException.ThrowIfNull(rowPatch);
        _rowStore.Mutate(_recordId, rowPatch);
    }
}