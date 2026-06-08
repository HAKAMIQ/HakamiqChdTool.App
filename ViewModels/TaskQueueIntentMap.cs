using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using System;

namespace HakamiqChdTool.App.ViewModels;

public static class TaskQueueIntentMap
{
    public static string ToStateCode(QueueItemStage stage)
    {
        return stage switch
        {
            QueueItemStage.Pending => TaskQueueStateCodes.Pending,
            QueueItemStage.ReadingFile => TaskQueueStateCodes.ReadingFile,
            QueueItemStage.Extracting => TaskQueueStateCodes.Extracting,
            QueueItemStage.Converting => TaskQueueStateCodes.Converting,
            QueueItemStage.Verifying => TaskQueueStateCodes.Verifying,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown QueueItemStage value."),
        };
    }

    public static string ToStateCode(QueueItemTerminalOutcome outcome)
    {
        return outcome switch
        {
            QueueItemTerminalOutcome.Healthy => TaskQueueStateCodes.Completed,
            QueueItemTerminalOutcome.Extracted => TaskQueueStateCodes.Completed,
            QueueItemTerminalOutcome.Moved => TaskQueueStateCodes.Completed,
            QueueItemTerminalOutcome.SkippedExists => TaskQueueStateCodes.Skipped,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown QueueItemTerminalOutcome value."),
        };
    }

    public static string ToStateCode(QueueItemFailureKind kind)
    {
        return kind switch
        {
            QueueItemFailureKind.Failed => TaskQueueStateCodes.Failed,
            QueueItemFailureKind.Cancelled => TaskQueueStateCodes.Cancelled,
            QueueItemFailureKind.PasswordRequired => TaskQueueStateCodes.PasswordRequired,
            QueueItemFailureKind.FailedConvert => TaskQueueStateCodes.Failed,
            QueueItemFailureKind.FailedVerify => TaskQueueStateCodes.Failed,
            QueueItemFailureKind.FailedExtract => TaskQueueStateCodes.Failed,
            QueueItemFailureKind.Unsupported => TaskQueueStateCodes.Skipped,
            QueueItemFailureKind.SourceUnreadable => TaskQueueStateCodes.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown QueueItemFailureKind value."),
        };
    }

    public static string ToFinalResult(QueueItemTerminalOutcome outcome)
    {
        return outcome switch
        {
            QueueItemTerminalOutcome.Healthy => TaskFinalResultCodes.Healthy,
            QueueItemTerminalOutcome.Extracted => TaskFinalResultCodes.Extracted,
            QueueItemTerminalOutcome.Moved => TaskFinalResultCodes.Moved,
            QueueItemTerminalOutcome.SkippedExists => TaskFinalResultCodes.SkippedExists,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown QueueItemTerminalOutcome value."),
        };
    }

    public static string ToFinalResult(QueueItemFailureKind kind)
    {
        return kind switch
        {
            QueueItemFailureKind.Failed => TaskFinalResultCodes.Failed,
            QueueItemFailureKind.Cancelled => TaskFinalResultCodes.Cancelled,
            QueueItemFailureKind.PasswordRequired => TaskFinalResultCodes.PasswordRequired,
            QueueItemFailureKind.FailedConvert => TaskFinalResultCodes.FailedConvert,
            QueueItemFailureKind.FailedVerify => TaskFinalResultCodes.FailedVerify,
            QueueItemFailureKind.FailedExtract => TaskFinalResultCodes.FailedExtract,
            QueueItemFailureKind.Unsupported => TaskFinalResultCodes.Unsupported,
            QueueItemFailureKind.SourceUnreadable => TaskFinalResultCodes.SourceUnreadable,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown QueueItemFailureKind value."),
        };
    }
}
