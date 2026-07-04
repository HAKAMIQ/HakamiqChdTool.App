using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowCleanupStage(
    CleanupService cleanup,
    ILogger log)
{
    private readonly CleanupService _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
    private readonly WorkflowSourceCleanupPipeline _sourceCleanup = new(log ?? throw new ArgumentNullException(nameof(log)));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));

    public void Run(
        ChdWorkflowTaskContext ctx,
        string originalPath,
        AppSettings settings,
        string? failedOutputCandidate,
        string? tempDirectoryToCleanup,
        bool alwaysCleanupTempDirectory,
        WorkflowExecutionResult? result,
        bool cancelled,
        bool sourceDeletionWasVerified)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentNullException.ThrowIfNull(settings);

        IQueueItemStateSink sink = ctx.Sink;
        bool isTerminal = result is not null || cancelled;
        bool isFailureOrCancelled = cancelled
            || result?.Outcome is WorkflowExecutionOutcome.Failure or WorkflowExecutionOutcome.Cancelled;

        TryCleanupFailedOutput(
            sink,
            originalPath,
            settings,
            failedOutputCandidate,
            isFailureOrCancelled);

        TryCleanupTemporaryExtraction(
            sink,
            settings,
            tempDirectoryToCleanup,
            alwaysCleanupTempDirectory,
            isTerminal);

        TryRunSourceCleanupPipeline(
            sink,
            originalPath,
            settings,
            result,
            sourceDeletionWasVerified);

        if (WorkflowPathHelpers.IsArchivePath(originalPath))
        {
            TryRestoreArchiveSourceState(sink, originalPath);
        }

        TryRefreshUi(ctx);
    }

    private void TryCleanupFailedOutput(
        IQueueItemStateSink sink,
        string originalPath,
        AppSettings settings,
        string? failedOutputCandidate,
        bool isFailureOrCancelled)
    {
        if (!isFailureOrCancelled
            || string.IsNullOrWhiteSpace(failedOutputCandidate)
            || WorkflowPathUtilities.PathsEqual(failedOutputCandidate, originalPath))
        {
            return;
        }

        try
        {
            if (AppPaths.IsPathUnderKnownPendingWorkspace(failedOutputCandidate, settings))
            {
                CleanupStats pendingCleanup = _cleanup.DeletePendingWorkspaceDirectoryTree(
                    Path.GetDirectoryName(failedOutputCandidate),
                    settings);

                if (pendingCleanup.DeletedBytes > 0)
                {
                    sink.AddCleanupDeletedBytes(pendingCleanup.DeletedBytes);
                }

                _log.Debug(
                    "Cleanup: pending workspace job delete stats Bytes={Bytes}, Files={Files}",
                    pendingCleanup.DeletedBytes,
                    pendingCleanup.DeletedFiles);

                return;
            }

            if (!settings.DeleteFailedOutput || !File.Exists(failedOutputCandidate))
            {
                return;
            }

            CleanupStats failedCleanup = _cleanup.DeleteFiles(
                failedOutputCandidate,
                Path.ChangeExtension(failedOutputCandidate, ".sbi"));

            sink.AddCleanupDeletedBytes(failedCleanup.DeletedBytes);

            _log.Debug(
                "Cleanup: failed-output delete stats Bytes={Bytes}, Files={Files}",
                failedCleanup.DeletedBytes,
                failedCleanup.DeletedFiles);
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            _log.Warning(
                ex,
                "Cleanup: failed-output cleanup was skipped after a non-fatal cleanup error. FailedOutputCandidate={FailedOutputCandidate}",
                failedOutputCandidate);
        }
    }

    private void TryCleanupTemporaryExtraction(
        IQueueItemStateSink sink,
        AppSettings settings,
        string? tempDirectoryToCleanup,
        bool alwaysCleanupTempDirectory,
        bool isTerminal)
    {
        if ((!settings.DeleteTemporaryExtraction && !alwaysCleanupTempDirectory)
            || !isTerminal
            || string.IsNullOrWhiteSpace(tempDirectoryToCleanup))
        {
            return;
        }

        bool isProcessTempPath = AppPaths.IsPathUnderProcessTempRoot(tempDirectoryToCleanup);
        bool isPendingWorkspacePath = alwaysCleanupTempDirectory
            && AppPaths.IsKnownPendingWorkspaceJobDirectory(tempDirectoryToCleanup, settings);

        if (!isProcessTempPath && !isPendingWorkspacePath)
        {
            return;
        }

        try
        {
            CleanupStats tempCleanup = isPendingWorkspacePath
                ? _cleanup.DeletePendingWorkspaceDirectoryTree(tempDirectoryToCleanup, settings)
                : _cleanup.DeleteDirectoryTree(tempDirectoryToCleanup);
            sink.AddCleanupDeletedBytes(tempCleanup.DeletedBytes);

            _log.Debug(
                "Cleanup: temp extraction tree done; Bytes={Bytes}, Files={Files}",
                tempCleanup.DeletedBytes,
                tempCleanup.DeletedFiles);
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            _log.Warning(
                ex,
                "Cleanup: temporary extraction cleanup was skipped after a non-fatal cleanup error. TempDirectory={TempDirectory}",
                tempDirectoryToCleanup);
        }
    }

    private void TryRunSourceCleanupPipeline(
        IQueueItemStateSink sink,
        string originalPath,
        AppSettings settings,
        WorkflowExecutionResult? result,
        bool sourceDeletionWasVerified)
    {
        try
        {
            RunSourceCleanupPipeline(
                sink,
                originalPath,
                settings,
                result,
                sourceDeletionWasVerified);
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            _log.Warning(
                ex,
                "Cleanup: source cleanup pipeline failed after workflow completion. Base workflow result remains unchanged. OriginalPath={OriginalPath} OutputPath={OutputPath}",
                originalPath,
                result?.OutputPath);
        }
    }

    private void RunSourceCleanupPipeline(
        IQueueItemStateSink sink,
        string originalPath,
        AppSettings settings,
        WorkflowExecutionResult? result,
        bool sourceDeletionWasVerified)
    {
        if (result is null
            || result.Outcome != WorkflowExecutionOutcome.Success
            || string.IsNullOrWhiteSpace(result.OutputPath))
        {
            return;
        }

        WorkflowSourceCleanupMode? mode = ResolveSourceCleanupMode(originalPath, result.OutputPath);
        if (mode is null)
        {
            return;
        }

        bool isEnabled = mode.Value switch
        {
            WorkflowSourceCleanupMode.VerifiedConversion => settings.DeleteSourceAfterVerifiedConversion,
            WorkflowSourceCleanupMode.VerifiedExtraction => settings.DeleteSourceAfterVerifiedExtraction,
            _ => false
        };

        if (!isEnabled)
        {
            return;
        }

        bool isVerified = mode.Value switch
        {
            WorkflowSourceCleanupMode.VerifiedConversion => sourceDeletionWasVerified
                && result.TerminalSuccessOutcome == QueueItemTerminalOutcome.Healthy,
            WorkflowSourceCleanupMode.VerifiedExtraction => sourceDeletionWasVerified
                && result.TerminalSuccessOutcome == QueueItemTerminalOutcome.Extracted,
            _ => false
        };

        WorkflowSourceCleanupResult cleanupResult = _sourceCleanup.Run(new WorkflowSourceCleanupRequest(
            originalPath,
            result.OutputPath,
            mode.Value,
            isVerified,
            isEnabled));

        if (cleanupResult.DeletedBytes > 0)
        {
            sink.AddCleanupDeletedBytes(cleanupResult.DeletedBytes);
        }
    }

    private static void TryRestoreArchiveSourceState(
        IQueueItemStateSink sink,
        string originalPath)
    {
        try
        {
            sink.RestoreArchiveSourceState();
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            Log.ForContext<WorkflowCleanupStage>().Warning(
                ex,
                "Cleanup: archive source state restore failed after workflow completion. OriginalPath={OriginalPath}",
                originalPath);
        }
    }

    private void TryRefreshUi(ChdWorkflowTaskContext ctx)
    {
        try
        {
            ctx.OnUiRefresh?.Invoke();
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            _log.Debug(
                ex,
                "Cleanup: UI refresh callback failed after cleanup. Workflow result remains unchanged.");
        }
    }

    private static WorkflowSourceCleanupMode? ResolveSourceCleanupMode(string originalPath, string outputPath)
    {
        string sourceExtension = Path.GetExtension(originalPath);
        string outputExtension = Path.GetExtension(outputPath);

        if (sourceExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
            && !outputExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowSourceCleanupMode.VerifiedExtraction;
        }

        if (!sourceExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
            && outputExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowSourceCleanupMode.VerifiedConversion;
        }

        return null;
    }

    private static bool IsExpectedCleanupStageException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}