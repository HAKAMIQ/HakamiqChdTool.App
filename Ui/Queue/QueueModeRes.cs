using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App.Ui.Queue;

internal readonly record struct QueueModeResolution(
    string RequestedAction,
    bool HasAnySupportedOperation,
    bool IsSupportedForSelectedMode,
    bool HasSingleSupportedOperation);

internal static class QueueModeResolver
{
    public static QueueOperationMode FromExecutionProfile(QueueExecutionProfile executionProfile) =>
        QueueOperationModeProjection.FromExecutionProfile(executionProfile);

    public static string QueueModeFromRequestedAction(string? requestedAction) =>
        QueueOperationModeProjection.QueueModeFromRequestedAction(requestedAction);

    public static string ResolveInitialRequestedAction(string? path, QueueExecutionProfile executionProfile) =>
        QueueOperationModeProjection.ResolveInitialRequestedAction(path, executionProfile);

    public static bool IsPathVisibleForExecutionProfile(string? path, QueueExecutionProfile executionProfile) =>
        QueueOperationModeProjection.IsPathVisibleForExecutionProfile(path, executionProfile);

    public static bool IsPathVisibleForMode(string? path, QueueOperationMode selectedMode) =>
        QueueOperationModeProjection.IsPathVisibleForMode(path, selectedMode);

    public static QueueModeResolution ResolveRequestedActionForMode(
        string? path,
        QueueOperationMode selectedMode)
    {
        QueueOperationModeProjectionResult projection = QueueOperationModeProjection.ProjectPath(path, selectedMode);

        return new QueueModeResolution(
            projection.RequestedAction,
            projection.HasAnySupportedOperation,
            projection.IsRunnableInSelectedMode,
            projection.HasSingleSupportedOperation);
    }

    public static bool IsWaitingRowRunnableForMode(QueueRowData row, QueueOperationMode selectedMode)
    {
        if (!row.IsVisibleInCurrentOperationMode)
        {
            return false;
        }

        if (selectedMode == QueueOperationMode.None)
        {
            return IsRequestedActionRunnable(row);
        }

        if (row.HasActiveQueueBinding || !TaskQueueStateCodes.IsWaiting(row.CurrentState))
        {
            return false;
        }

        string operationPath = ResolveRunnableOperationPath(row, selectedMode);

        if (BlocksArchivePreviewProcessing(row, selectedMode))
        {
            return false;
        }

        QueueModeResolution resolution = ResolveRequestedActionForMode(operationPath, selectedMode);

        return resolution.IsSupportedForSelectedMode
            && string.Equals(row.RequestedAction, resolution.RequestedAction, StringComparison.Ordinal);
    }

    public static bool IsRequestedActionRunnable(QueueRowData row)
    {
        if (!row.IsVisibleInCurrentOperationMode || row.HasActiveQueueBinding)
        {
            return false;
        }

        QueueOperationMode operationMode = QueueOperationCapabilityService.GetOperationMode(row.RequestedAction);
        string operationPath = ResolveRunnableOperationPath(row, operationMode);

        if (BlocksArchivePreviewProcessing(row, operationMode))
        {
            return false;
        }

        if (string.Equals(row.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal)
            || string.Equals(row.RequestedAction, TaskActionCodes.Unsupported, StringComparison.Ordinal))
        {
            return false;
        }

        return TaskQueueStateCodes.IsWaiting(row.CurrentState)
            && QueueOperationCapabilityService.IsOperationAllowed(operationPath, row.RequestedAction);
    }

    private static string ResolveRunnableOperationPath(QueueRowData row, QueueOperationMode operationMode)
    {
        if (operationMode == QueueOperationMode.Verify
            && !string.IsNullOrWhiteSpace(row.SourcePath))
        {
            return row.SourcePath;
        }

        return row.OriginalPath;
    }

    private static bool BlocksArchivePreviewProcessing(QueueRowData row, QueueOperationMode operationMode) =>
        operationMode != QueueOperationMode.Verify
        && ArchivePreviewIntakePolicy.BlocksQueuedArchiveProcessing(row.OriginalPath, row.IntakeSource);
}
