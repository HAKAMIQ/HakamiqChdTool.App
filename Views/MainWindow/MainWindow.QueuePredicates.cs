using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.IO;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private static string? ResolveQueueItemProbePath(TaskQueueItemViewModel? item)
    {
        if (item is null)
        {
            return null;
        }

        string? raw = null;

        if (!string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
        {
            raw = item.SourcePath;
        }
        else if (!string.IsNullOrWhiteSpace(item.OriginalPath) && File.Exists(item.OriginalPath))
        {
            raw = item.OriginalPath;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return FilePathExclusiveGate.NormalizePathForExclusiveLock(raw);
    }

    private bool CanQueueItemRunPipelineForSelectedMode(TaskQueueItemViewModel item) =>
        _queueRowStore.GetById(item.QueueItemId) is { } row
        && IsRowQueuedForProcessingForSelectedMode(row);

    private static bool CanQueueItemRunPipeline(TaskQueueItemViewModel item)
    {
        if (string.Equals(item.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal)
            || string.Equals(item.RequestedAction, TaskActionCodes.Unsupported, StringComparison.Ordinal))
        {
            return false;
        }

        string path = item.SourcePath;
        if (!IsQueueInputPathAvailable(path))
        {
            return false;
        }

        if (item.IsDirectChd)
        {
            return true;
        }

        return CanQueueItemConvertToChd(item);
    }

    private static string QueueModeFromRequestedAction(string? requestedAction) =>
        QueueOperationModeResolver.QueueModeFromRequestedAction(requestedAction);

    private static bool CanQueueItemConvertToChd(TaskQueueItemViewModel item)
    {
        if (item.IsDirectChd)
        {
            return false;
        }

        string path = item.SourcePath;
        if (!IsQueueInputPathAvailable(path))
        {
            return false;
        }

        return QueueConversionRules.IsDiscOrArchiveSupportedForChdConversion(path);
    }

    private static bool IsQueueInputPathAvailable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return File.Exists(path)
;
    }

    private static bool CanProcessIsoCueGdiPredicate(TaskQueueItemViewModel? item)
    {
        if (item is null || item.IsDirectChd)
        {
            return false;
        }

        if (string.Equals(item.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal))
        {
            return false;
        }

        string path = item.SourcePath;
        if (!IsQueueInputPathAvailable(path))
        {
            return false;
        }

        return QueueInputClassifier.IsConvertibleDiscImagePath(path)
            && string.Equals(item.RequestedAction, TaskActionCodes.ConvertToChd, StringComparison.Ordinal);
    }

    private static bool CanProcessArchivePredicate(TaskQueueItemViewModel? item)
    {
        if (item is null)
        {
            return false;
        }

        string path = item.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return QueueInputClassifier.IsArchiveContainerPath(path)
            && (string.Equals(item.RequestedAction, TaskActionCodes.StageArchiveForConversion, StringComparison.Ordinal)
                || string.Equals(item.RequestedAction, TaskActionCodes.ExtractArchiveThenProcess, StringComparison.Ordinal)
                || string.Equals(item.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal));
    }

    private static bool CanProcessChdExtractPredicate(TaskQueueItemViewModel? item)
    {
        if (item is null || !item.IsDirectChd)
        {
            return false;
        }

        string path = item.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return string.Equals(item.RequestedAction, TaskActionCodes.ExtractFromChd, StringComparison.Ordinal)
            || string.Equals(item.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal)
            || string.Equals(item.RequestedAction, TaskActionCodes.VerifyChd, StringComparison.Ordinal);
    }

    private static bool CanQueueItemVerifyChd(TaskQueueItemViewModel? item)
    {
        if (item is null || !item.IsDirectChd)
        {
            return false;
        }

        string path = item.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return string.Equals(item.RequestedAction, TaskActionCodes.VerifyChd, StringComparison.Ordinal)
            || string.Equals(item.RequestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal)
            || string.Equals(item.RequestedAction, TaskActionCodes.ExtractFromChd, StringComparison.Ordinal);
    }

    private bool CanQueueItemIntegrityCheck(TaskQueueItemViewModel? item) =>
        item is not null && ResolveQueueItemProbePath(item) is not null;

    private static bool ArchiveNamingRuleValidatorApplies(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        QueueInputClassification classification = QueueInputClassifier.Classify(path);
        return classification.IsChdImage || classification.IsArchiveContainer;
    }

    private static bool TryGetQueueItemSourceTarget(TaskQueueItemViewModel? item, out string? targetPath)
    {
        targetPath = null;

        if (item is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
        {
            targetPath = item.SourcePath;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.OriginalPath) && File.Exists(item.OriginalPath))
        {
            targetPath = item.OriginalPath;
            return true;
        }

        return false;
    }

    private static bool TryGetQueueItemOutputTarget(TaskQueueItemViewModel? item, out string? targetPath)
    {
        targetPath = null;

        if (item is null || string.IsNullOrWhiteSpace(item.OutputPath) || !File.Exists(item.OutputPath))
        {
            return false;
        }

        targetPath = item.OutputPath;
        return true;
    }

    private static bool IsCompletedSuccessfulQueueItem(TaskQueueItemViewModel item) =>
        IsCompletedSuccessfulFinalResult(item.FinalResult);

    private static bool IsCompletedSuccessfulRow(QueueRowData row) =>
        IsCompletedSuccessfulFinalResult(row.FinalResult);

    private static bool IsCompletedSuccessfulFinalResult(string finalResult) =>
        finalResult is TaskFinalResultCodes.Healthy
            or TaskFinalResultCodes.Moved
            or TaskFinalResultCodes.Extracted;

    private static bool IsRowQueuedForProcessing(QueueRowData row) =>
        QueueOperationModeResolver.IsRequestedActionRunnable(row)
        && ProcessingStateMapper.Map(row.CurrentState, null) == ProcessingState.Queued;

    private bool IsRowQueuedForProcessingForSelectedMode(QueueRowData row) =>
        QueueOperationModeResolver.IsWaitingRowRunnableForMode(row, GetSelectedQueueOperationMode())
        && ProcessingStateMapper.Map(row.CurrentState, null) == ProcessingState.Queued;

    private int CountQueuedRowsForSelectedOperationMode()
    {
        int count = 0;

        foreach (QueueRowData row in _queueRowStore.Rows)
        {
            if (IsRowQueuedForProcessingForSelectedMode(row))
            {
                count++;
            }
        }

        return count;
    }

    internal static bool IsRowQueuedForProcessingStatic(QueueRowData row) =>
        IsRowQueuedForProcessing(row);
}