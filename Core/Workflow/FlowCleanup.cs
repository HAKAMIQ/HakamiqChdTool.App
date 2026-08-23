using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow.Extraction;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            CleanupStats failedCleanup = string.Equals(
                    Path.GetExtension(failedOutputCandidate),
                    ".cue",
                    StringComparison.OrdinalIgnoreCase)
                ? TryDeleteFailedCueBinBundle(failedOutputCandidate)
                : _cleanup.DeleteFiles(
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


    internal static CleanupStats TryDeleteFailedCueBinBundle(string cuePath)
    {
        if (string.IsNullOrWhiteSpace(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(cuePath))
        {
            return CleanupStats.Empty;
        }

        var validator = new ExtractionOutputBundleValidator();
        if (!validator.TryValidateExistingFinal(
                ExtractionOutputKind.CueBinBundle,
                cuePath,
                out ExtractionOutputBundle bundle,
                out _)
            || !TryBuildSafeCueBinCleanupSet(cuePath, bundle.FilePaths, out string[] cleanupPaths))
        {
            return CleanupStats.Empty;
        }

        long deletedBytes = 0;
        int deletedFiles = 0;
        string fullCuePath = Path.GetFullPath(cuePath);

        foreach (string dependencyPath in cleanupPaths
                     .Where(path => !WorkflowPathUtilities.PathsEqual(path, fullCuePath))
                     .OrderByDescending(static path => path.Length))
        {
            if (!TryDeleteKnownFailedOutputFile(dependencyPath, out long length))
            {
                return new CleanupStats(deletedBytes, deletedFiles);
            }

            deletedBytes += length;
            deletedFiles++;
        }

        if (!TryDeleteKnownFailedOutputFile(fullCuePath, out long cueLength))
        {
            return new CleanupStats(deletedBytes, deletedFiles);
        }

        deletedBytes += cueLength;
        deletedFiles++;

        string sbiPath = Path.ChangeExtension(fullCuePath, ".sbi");
        string cueDirectory = Path.GetDirectoryName(fullCuePath)!;
        if (File.Exists(sbiPath)
            && IsSafeFailedOutputCleanupPath(cueDirectory, sbiPath)
            && TryDeleteKnownFailedOutputFile(sbiPath, out long sbiLength))
        {
            deletedBytes += sbiLength;
            deletedFiles++;
        }

        return new CleanupStats(deletedBytes, deletedFiles);
    }

    private static bool TryBuildSafeCueBinCleanupSet(
        string cuePath,
        IReadOnlyList<string> bundlePaths,
        out string[] cleanupPaths)
    {
        cleanupPaths = [];

        try
        {
            string fullCuePath = Path.GetFullPath(cuePath);
            string? cueDirectory = Path.GetDirectoryName(fullCuePath);
            if (string.IsNullOrWhiteSpace(cueDirectory)
                || HasReparsePointInExistingPath(cueDirectory, cueDirectory))
            {
                return false;
            }

            string[] materialized =
            [
                .. bundlePaths
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];

            if (materialized.Length < 2
                || !materialized.Any(path => WorkflowPathUtilities.PathsEqual(path, fullCuePath))
                || materialized.Any(path => !IsSafeFailedOutputCleanupPath(cueDirectory, path)))
            {
                return false;
            }

            cleanupPaths = materialized;
            return true;
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            cleanupPaths = [];
            return false;
        }
    }

    private static bool IsSafeFailedOutputCleanupPath(string cueDirectory, string candidatePath)
    {
        try
        {
            string root = Path.GetFullPath(cueDirectory);
            string candidate = Path.GetFullPath(candidatePath);
            string rootWithSeparator = Path.EndsInDirectorySeparator(root)
                ? root
                : root + Path.DirectorySeparatorChar;

            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && File.Exists(candidate)
                && !Directory.Exists(candidate)
                && !HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            return false;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);
            string current = File.Exists(candidate) || Directory.Exists(candidate)
                ? candidate
                : Path.GetDirectoryName(candidate) ?? candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if (WorkflowPathUtilities.PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent)
                    || WorkflowPathUtilities.PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            return true;
        }
    }

    private static bool TryDeleteKnownFailedOutputFile(string path, out long length)
    {
        length = 0;

        try
        {
            if (!File.Exists(path)
                || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return false;
            }

            length = new FileInfo(path).Length;
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception ex) when (IsExpectedCleanupStageException(ex))
        {
            return false;
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