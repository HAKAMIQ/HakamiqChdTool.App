using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

internal readonly record struct QueueOperationModeProjectionResult(
    string RequestedAction,
    bool HasAnySupportedOperation,
    bool IsVisibleInSelectedMode,
    bool IsRunnableInSelectedMode,
    bool HasSingleSupportedOperation);

internal static class QueueOperationModeProjection
{
    public static QueueOperationMode FromExecutionProfile(QueueExecutionProfile executionProfile) =>
        executionProfile switch
        {
            QueueExecutionProfile.QuickConvert => QueueOperationMode.Convert,
            QueueExecutionProfile.QuickExtract => QueueOperationMode.Extract,
            QueueExecutionProfile.QuickVerify => QueueOperationMode.Verify,
            _ => QueueOperationMode.None
        };

    public static string QueueModeFromRequestedAction(string? requestedAction) =>
        QueueOperationCapabilityService.GetOperationMode(requestedAction) switch
        {
            QueueOperationMode.Verify => "Verify",
            QueueOperationMode.Extract => "Extract",
            _ => "Convert"
        };

    public static string ResolveInitialRequestedAction(string? path, QueueExecutionProfile executionProfile)
    {
        QueueOperationMode selectedMode = FromExecutionProfile(executionProfile);
        if (selectedMode != QueueOperationMode.None)
        {
            QueueOperationModeProjectionResult projection = ProjectPath(path, selectedMode);
            if (projection.IsRunnableInSelectedMode)
            {
                return projection.RequestedAction;
            }
        }

        IReadOnlyList<string> supportedOperations = QueueOperationCapabilityService.GetSupportedOperationCodes(path);
        return supportedOperations.Count switch
        {
            0 => TaskActionCodes.Unsupported,
            1 => supportedOperations[0],
            _ => TaskActionCodes.PendingSelection
        };
    }

    public static bool IsPathVisibleForExecutionProfile(string? path, QueueExecutionProfile executionProfile)
    {
        QueueOperationMode selectedMode = FromExecutionProfile(executionProfile);
        return IsPathVisibleForMode(path, selectedMode);
    }

    public static bool IsPathVisibleForMode(string? path, QueueOperationMode selectedMode) =>
        ProjectPath(path, selectedMode).IsVisibleInSelectedMode;

    public static QueueOperationModeProjectionResult ProjectPath(string? path, QueueOperationMode selectedMode)
    {
        IReadOnlyList<string> supportedOperations = QueueOperationCapabilityService.GetSupportedOperationCodes(path);
        bool hasAnySupportedOperation = supportedOperations.Count > 0;
        bool hasSingleSupportedOperation = supportedOperations.Count == 1;

        if (selectedMode == QueueOperationMode.None)
        {
            string requestedAction = supportedOperations.Count switch
            {
                0 => TaskActionCodes.Unsupported,
                1 => supportedOperations[0],
                _ => TaskActionCodes.PendingSelection
            };

            return new QueueOperationModeProjectionResult(
                requestedAction,
                hasAnySupportedOperation,
                hasAnySupportedOperation,
                hasSingleSupportedOperation,
                hasSingleSupportedOperation);
        }

        string selectedModeAction = ResolveActionForMode(supportedOperations, selectedMode);
        bool isRunnable = selectedModeAction != TaskActionCodes.PendingSelection
            && selectedModeAction != TaskActionCodes.Unsupported;

        return new QueueOperationModeProjectionResult(
            selectedModeAction,
            hasAnySupportedOperation,
            isRunnable,
            isRunnable,
            hasSingleSupportedOperation);
    }

    private static string ResolveActionForMode(
        IReadOnlyList<string> supportedOperations,
        QueueOperationMode selectedMode)
    {
        for (int index = 0; index < supportedOperations.Count; index++)
        {
            string action = supportedOperations[index];
            if (QueueOperationCapabilityService.GetOperationMode(action) == selectedMode)
            {
                return action;
            }
        }

        return supportedOperations.Count == 0
            ? TaskActionCodes.Unsupported
            : TaskActionCodes.PendingSelection;
    }
}