using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public static class ProcessingStateMapper
{
    public static ProcessingState Map(string? taskState, string? queueItemStatus)
    {
        if (!string.IsNullOrWhiteSpace(queueItemStatus))
        {
            if (string.Equals(queueItemStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessingState.Processing;
            }

            if (string.Equals(queueItemStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessingState.Queued;
            }

            if (string.Equals(queueItemStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessingState.Completed;
            }

            if (string.Equals(queueItemStatus, "Skipped", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessingState.Skipped;
            }

            if (string.Equals(queueItemStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(queueItemStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessingState.Failed;
            }
        }

        string state = string.IsNullOrWhiteSpace(taskState)
            ? TaskQueueStateCodes.Pending
            : taskState.Trim();

        if (state == TaskQueueStateCodes.Ready)
        {
            state = TaskQueueStateCodes.Pending;
        }

        return state switch
        {
            TaskQueueStateCodes.AwaitingOperationSelection => ProcessingState.AwaitingOperation,
            TaskQueueStateCodes.Pending => ProcessingState.Queued,
            TaskQueueStateCodes.Processing
                or TaskQueueStateCodes.Extracting
                or TaskQueueStateCodes.Converting
                or TaskQueueStateCodes.Verifying
                or TaskQueueStateCodes.ReadingFile => ProcessingState.Processing,
            TaskQueueStateCodes.Completed => ProcessingState.Completed,
            TaskQueueStateCodes.Skipped => ProcessingState.Skipped,
            TaskQueueStateCodes.Failed
                or TaskQueueStateCodes.PasswordRequired
                or TaskQueueStateCodes.Cancelled => ProcessingState.Failed,
            _ => ProcessingState.Queued
        };
    }
}