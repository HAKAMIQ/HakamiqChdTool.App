using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowPreflightStage
{
    private const double DirectConversionOutputMultiplier = 1.10d;
    private const double ArchiveExtractionTempMultiplier = 3.00d;
    private const double ArchiveConversionOutputMultiplier = 1.50d;
    private const double ChdExtractionOutputMultiplier = 3.00d;
    private const long MinimumEstimatedOperationBytes = 512L * 1024L * 1024L;
    private const long MinimumAbsoluteReserveBytes = 2L * 1024L * 1024L * 1024L;
    private const double MinimumPercentReserve = 0.10d;
    private const string CheckingDiskSpaceStageKey = "LocWorkflowPreflight_CheckingDiskSpace";
    private const string DiskSpaceBlockerMessageKey = "LocWorkflowPreflight_DiskSpaceBlockerFormat";
    private const string DiskSpaceWarningMessageKey = "LocWorkflowPreflight_DiskSpaceWarningFormat";
    private const string DriveUnavailableMessageKey = "LocWorkflowPreflight_DriveUnavailableFormat";
    private const string PurposeOutputKey = "LocWorkflowPreflight_PurposeOutput";
    private const string PurposeTempKey = "LocWorkflowPreflight_PurposeTemp";
    private const string PurposePendingKey = "LocWorkflowPreflight_PurposePending";

    private static readonly TimeSpan AutomaticOrphanedCleanupMinimumAge = TimeSpan.Zero;

    private readonly ILogger _log;

    public WorkflowPreflightStage(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public WorkflowPreflightResult Run(ChdTaskRequest request, ChdWorkflowTaskContext ctx)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!ctx.Settings.EnableDiskSpaceGuard)
        {
            _log.Information("Workflow preflight skipped because disk space guard is disabled. Input={Input}", request.InputPath);
            return WorkflowPreflightResult.Success();
        }

        ctx.Sink.ReportStage(QueueItemStage.ReadingFile, CheckingDiskSpaceStageKey);
        ctx.Sink.ReportProgress(2, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 2);

        try
        {
            DiskSpaceEstimate estimate = BuildEstimate(ctx);
            WorkflowPreflightResult result = EvaluateEstimate(estimate);

            if (result.FirstBlocker is not null)
            {
                CleanupStats cleanupStats = TryCleanupOrphanedWorkspacesAfterBlocker(request, ctx);
                if (cleanupStats.DeletedBytes > 0 || cleanupStats.DeletedFiles > 0)
                {
                    result = EvaluateEstimate(estimate);
                }
            }

            LogResult(request, result);
            return result;
        }
        catch (Exception ex) when (IsExpectedPreflightException(ex))
        {
            string driveRoot = TryResolveSourceDrive(ctx.Snapshot.SourcePath);
            long availableBytes = TryGetAvailableBytes(driveRoot);

            _log.Warning(ex, "Workflow preflight failed before execution. Input={Input}", request.InputPath);

            return WorkflowPreflightResult.WithIssues([
                new WorkflowPreflightIssue(
                    WorkflowPreflightSeverity.Blocker,
                    DriveUnavailableMessageKey,
                    driveRoot,
                    0,
                    availableBytes,
                    string.Empty)
            ]);
        }
    }


    private WorkflowPreflightResult EvaluateEstimate(DiskSpaceEstimate estimate)
    {
        IReadOnlyDictionary<string, long> requirementsByDrive = estimate.GetAggregatedRequirementsByDrive();

        if (requirementsByDrive.Count == 0)
        {
            return WorkflowPreflightResult.Success();
        }

        List<WorkflowPreflightIssue> issues = [];

        foreach ((string driveRoot, long requiredBytes) in requirementsByDrive)
        {
            long availableBytes = DiskSpacePreflightService.GetAvailableFreeBytes(driveRoot);
            long totalBytes = DiskSpacePreflightService.GetTotalSizeBytes(driveRoot);
            long reserveBytes = CalculateReserveBytes(totalBytes);

            if (availableBytes < requiredBytes)
            {
                issues.Add(new WorkflowPreflightIssue(
                    WorkflowPreflightSeverity.Blocker,
                    DiskSpaceBlockerMessageKey,
                    driveRoot,
                    requiredBytes,
                    availableBytes,
                    string.Empty));

                continue;
            }

            long freeAfterOperation = Math.Max(0L, availableBytes - requiredBytes);
            if (freeAfterOperation < reserveBytes)
            {
                issues.Add(new WorkflowPreflightIssue(
                    WorkflowPreflightSeverity.Warning,
                    DiskSpaceWarningMessageKey,
                    driveRoot,
                    requiredBytes,
                    availableBytes,
                    string.Empty));
            }
        }

        return WorkflowPreflightResult.WithIssues(issues);
    }

    private CleanupStats TryCleanupOrphanedWorkspacesAfterBlocker(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx)
    {
        try
        {
            OrphanedWorkItemScanResult scanResult = new OrphanedWorkItemScanner(
                    ctx.Settings,
                    AutomaticOrphanedCleanupMinimumAge,
                    CollectPendingWorkspaceRoots(ctx))
                .Scan();

            if (!scanResult.HasItems)
            {
                return CleanupStats.Empty;
            }

            ctx.Sink.ReportStage(QueueItemStage.ReadingFile, "LocOrphanedCleanup_CleaningFooter");

            CleanupStats cleanupStats = new OrphanedWorkItemCleanupService(ctx.Settings)
                .Clean(scanResult);

            if (cleanupStats.DeletedBytes > 0)
            {
                ctx.Sink.AddCleanupDeletedBytes(cleanupStats.DeletedBytes);
            }

            _log.Information(
                "Workflow preflight attempted orphaned workspace cleanup after disk-space blocker. Input={Input} CandidateBytes={CandidateBytes} DeletedBytes={DeletedBytes} DeletedFiles={DeletedFiles}",
                request.InputPath,
                scanResult.TotalBytes,
                cleanupStats.DeletedBytes,
                cleanupStats.DeletedFiles);

            return cleanupStats;
        }
        catch (Exception ex) when (IsExpectedPreflightException(ex))
        {
            _log.Debug(ex, "Workflow preflight orphaned workspace cleanup was skipped. Input={Input}", request.InputPath);
            return CleanupStats.Empty;
        }
    }

    public static string BuildUserDetail(WorkflowPreflightIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        string required = DiskSpacePreflightService.FormatBytes(issue.RequiredBytes);
        string available = DiskSpacePreflightService.FormatBytes(issue.AvailableBytes);
        string drive = string.IsNullOrWhiteSpace(issue.DriveRoot)
            ? "?"
            : issue.DriveRoot;

        return ArabicUi.Format(issue.MessageCode, drive, required, available);
    }

    private static DiskSpaceEstimate BuildEstimate(ChdWorkflowTaskContext ctx)
    {
        QueueItemSnapshot snap = ctx.Snapshot;
        AppSettings settings = ctx.Settings;
        string sourcePath = string.IsNullOrWhiteSpace(snap.SourcePath)
            ? snap.OriginalPath
            : snap.SourcePath;

        QueueInputClassification classification = QueueInputClassifier.Classify(sourcePath);
        DiskSpaceEstimate estimate = new();

        if (string.Equals(snap.RequestedAction, TaskActionCodes.StageArchiveForConversion, StringComparison.Ordinal)
            && classification.IsArchiveContainer)
        {
            AddArchiveConversionRequirements(estimate, sourcePath, snap, settings);
            return estimate;
        }

        if (string.Equals(snap.RequestedAction, TaskActionCodes.ConvertToChd, StringComparison.Ordinal)
            && classification.IsConvertibleDiscImage)
        {
            AddDirectConversionRequirements(estimate, sourcePath, snap, settings);
            return estimate;
        }

        if (string.Equals(snap.RequestedAction, TaskActionCodes.RestoreDiscImageFromChd, StringComparison.Ordinal)
            && classification.IsChdImage)
        {
            AddExtractionRequirements(estimate, sourcePath, snap, settings);
            return estimate;
        }

        return estimate;
    }
    private static void AddArchiveConversionRequirements(
        DiskSpaceEstimate estimate,
        string sourcePath,
        QueueItemSnapshot snap,
        AppSettings settings)
    {
        long sourceBytes = DiskSpacePreflightService.EstimateInputBytesForWorkflow(sourcePath);
        string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(snap.OriginalPath, sourcePath, settings);
        string pendingRoot = ResolvePendingWorkspaceRoot(outputRoot, settings);
        string tempRoot = AppPaths.ProcessTempRoot;

        long tempRequired = DiskSpacePreflightService.EstimateWorkflowRequiredBytes(
            sourceBytes,
            ArchiveExtractionTempMultiplier,
            MinimumEstimatedOperationBytes);

        long outputRequired = DiskSpacePreflightService.EstimateWorkflowRequiredBytes(
            sourceBytes,
            ArchiveConversionOutputMultiplier,
            MinimumEstimatedOperationBytes);

        estimate.AddRequirement(DiskSpacePreflightService.CreateRequirementForPath(
            tempRoot,
            tempRequired,
            PurposeTempKey));

        AddPendingAndFinalOutputRequirements(estimate, pendingRoot, outputRoot, outputRequired);
    }

    private static void AddDirectConversionRequirements(
        DiskSpaceEstimate estimate,
        string sourcePath,
        QueueItemSnapshot snap,
        AppSettings settings)
    {
        long sourceBytes = DiskSpacePreflightService.EstimateInputBytesForWorkflow(sourcePath);
        string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(snap.OriginalPath, sourcePath, settings);
        string pendingRoot = ResolvePendingWorkspaceRoot(outputRoot, settings);

        long outputRequired = DiskSpacePreflightService.EstimateWorkflowRequiredBytes(
            sourceBytes,
            DirectConversionOutputMultiplier,
            MinimumEstimatedOperationBytes);

        AddPendingAndFinalOutputRequirements(estimate, pendingRoot, outputRoot, outputRequired);
    }
    private static void AddExtractionRequirements(
        DiskSpaceEstimate estimate,
        string sourcePath,
        QueueItemSnapshot snap,
        AppSettings settings)
    {
        long sourceBytes = DiskSpacePreflightService.EstimateInputBytesForWorkflow(sourcePath);
        string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(snap.OriginalPath, sourcePath, settings);
        string pendingRoot = ResolvePendingWorkspaceRoot(outputRoot, settings);

        long outputRequired = DiskSpacePreflightService.EstimateWorkflowRequiredBytes(
            sourceBytes,
            ChdExtractionOutputMultiplier,
            MinimumEstimatedOperationBytes);

        AddPendingAndFinalOutputRequirements(estimate, pendingRoot, outputRoot, outputRequired);
    }

    private static void AddPendingAndFinalOutputRequirements(
        DiskSpaceEstimate estimate,
        string pendingRoot,
        string outputRoot,
        long requiredBytes)
    {
        string pendingDrive = DiskSpacePreflightService.GetDriveRootForPath(pendingRoot);
        string outputDrive = DiskSpacePreflightService.GetDriveRootForPath(outputRoot);

        estimate.AddRequirement(new DiskSpaceRequirement(pendingDrive, requiredBytes, PurposePendingKey));

        if (!string.Equals(NormalizeDriveRoot(pendingDrive), NormalizeDriveRoot(outputDrive), StringComparison.OrdinalIgnoreCase))
        {
            estimate.AddRequirement(new DiskSpaceRequirement(outputDrive, requiredBytes, PurposeOutputKey));
        }
    }

    private static string ResolvePendingWorkspaceRoot(string outputRoot, AppSettings settings)
    {
        return PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(outputRoot, settings);
    }

    private static IReadOnlyList<string> CollectPendingWorkspaceRoots(ChdWorkflowTaskContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        QueueItemSnapshot snap = ctx.Snapshot;
        AppSettings settings = ctx.Settings;
        string sourcePath = string.IsNullOrWhiteSpace(snap.SourcePath)
            ? snap.OriginalPath
            : snap.SourcePath;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return [];
        }

        try
        {
            string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(snap.OriginalPath, sourcePath, settings);
            string pendingRoot = ResolvePendingWorkspaceRoot(outputRoot, settings);

            return PendingWorkspacePathPolicy.IsKnownWorkspaceRoot(pendingRoot, settings)
                ? [Path.GetFullPath(pendingRoot)]
                : [];
        }
        catch (Exception ex) when (IsExpectedPreflightException(ex))
        {
            return [];
        }
    }

    private static long CalculateReserveBytes(long totalBytes)
    {
        double percentReserve = Math.Ceiling(Math.Max(0L, totalBytes) * MinimumPercentReserve);
        long percentReserveBytes = percentReserve >= long.MaxValue ? long.MaxValue : (long)percentReserve;

        return Math.Max(MinimumAbsoluteReserveBytes, percentReserveBytes);
    }

    private void LogResult(ChdTaskRequest request, WorkflowPreflightResult result)
    {
        if (!result.IsBlocker && !result.HasWarnings)
        {
            _log.Information("Workflow preflight passed. Input={Input}", request.InputPath);
            return;
        }

        foreach (WorkflowPreflightIssue issue in result.Issues)
        {
            if (issue.Severity == WorkflowPreflightSeverity.Blocker)
            {
                _log.Warning(
                    "Workflow preflight blocked execution. Input={Input} Drive={Drive} RequiredBytes={RequiredBytes} AvailableBytes={AvailableBytes}",
                    request.InputPath,
                    issue.DriveRoot,
                    issue.RequiredBytes,
                    issue.AvailableBytes);
            }
            else if (issue.Severity == WorkflowPreflightSeverity.Warning)
            {
                _log.Warning(
                    "Workflow preflight warning. Input={Input} Drive={Drive} RequiredBytes={RequiredBytes} AvailableBytes={AvailableBytes}",
                    request.InputPath,
                    issue.DriveRoot,
                    issue.RequiredBytes,
                    issue.AvailableBytes);
            }
        }
    }

    private static string TryResolveSourceDrive(string sourcePath)
    {
        try
        {
            return string.IsNullOrWhiteSpace(sourcePath)
                ? string.Empty
                : DiskSpacePreflightService.GetDriveRootForPath(sourcePath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long TryGetAvailableBytes(string driveRoot)
    {
        try
        {
            return string.IsNullOrWhiteSpace(driveRoot)
                ? 0L
                : DiskSpacePreflightService.GetAvailableFreeBytes(driveRoot);
        }
        catch
        {
            return 0L;
        }
    }

    private static string NormalizeDriveRoot(string driveRoot) =>
        driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();

    private static bool IsExpectedPreflightException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or PathTooLongException;
}
