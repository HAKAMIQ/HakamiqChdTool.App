using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowPathUtilities
{
    public static void RaiseProgress(ChdTaskRequest request, double percent)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.OnProgress?.Invoke(Math.Clamp(percent, 0, 100));
    }

    public static (ChdmanExtractionKind Kind, string OutputExtension) GetChdExtractRoute(string mediaType)
    {
        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanExtractionFromChdMediaType(mediaType);
        return plan.IsSupported ? (plan.ExtractionKind, plan.OutputExtension) : (ChdmanExtractionKind.None, string.Empty);
    }

    public static string BuildExtractionStageLine(ChdmanExtractionKind kind) =>
        ChdWorkflowProfilePlanner.PlanExtractionByKind(kind).StatusLine is { Length: > 0 } line
            ? line
            : ChdWorkflowProfilePlanner.UnknownChdExtractionMessageKey;

    public static bool IsUnknownChdMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
        || string.Equals(mediaType, "Unknown", StringComparison.OrdinalIgnoreCase);

    public static (ChdmanExtractionKind Kind, string Ext) GetOppositeExtractRoute(ChdmanExtractionKind kind) =>
        kind switch
        {
            ChdmanExtractionKind.ExtractCd => (ChdmanExtractionKind.ExtractDvd, ".iso"),
            ChdmanExtractionKind.ExtractDvd => (ChdmanExtractionKind.ExtractCd, ".cue"),
            ChdmanExtractionKind.ExtractRaw => (ChdmanExtractionKind.ExtractHd, ".img"),
            ChdmanExtractionKind.ExtractHd => (ChdmanExtractionKind.ExtractRaw, ".raw"),
            _ => (ChdmanExtractionKind.ExtractCd, ".cue")
        };

    public static Task<PlatformDetectionResult> ApplyChdMediaDetectionAsync(
        IQueueItemStateSink sink,
        string chdPath,
        ChdInfoResult infoResult,
        CancellationToken cancellationToken) =>
        WorkflowSourcePathResolver.ApplyChdMediaDetectionAsync(sink, chdPath, infoResult, cancellationToken);

    public static string BuildFinalExtractOutputPath(string detectedPlatform, string originalPath, string chdPath, string outputExtension, AppSettings settings) =>
        WorkflowOutputPathPlanner.BuildFinalExtractOutputPath(detectedPlatform, originalPath, chdPath, outputExtension, settings);

    public static string BuildFinalChdOutputPath(string detectedPlatform, string originalPath, string workingInputPath, AppSettings settings) =>
        WorkflowOutputPathPlanner.BuildFinalChdOutputPath(detectedPlatform, originalPath, workingInputPath, settings);

    public static string BuildFinalVerifiedChdPath(string detectedPlatform, string originalPath, string chdPath, AppSettings settings) =>
        WorkflowOutputPathPlanner.BuildFinalVerifiedChdPath(detectedPlatform, originalPath, chdPath, settings);

    public static string ResolveBaseOutputRoot(string originalPath, string workingInputPath, AppSettings settings) =>
        WorkflowOutputPathPlanner.ResolveBaseOutputRoot(originalPath, workingInputPath, settings);

    public static string ResolveOutputRootDirectory(string originalPath, string workingInputPath, string detectedPlatform, AppSettings settings) =>
        WorkflowOutputPathPlanner.ResolveOutputRootDirectory(originalPath, workingInputPath, detectedPlatform, settings);

    public static string BuildPendingOutputPath(string finalOutputPath, string workingInputPath, string outputExtension, string resolvedOutputRoot, AppSettings settings) =>
        WorkflowOutputPathPlanner.BuildPendingOutputPath(finalOutputPath, workingInputPath, outputExtension, resolvedOutputRoot, settings);

    public static void PromoteProducedFileToFinalLocation(string sourcePath, string finalPath) =>
        WorkflowOutputPathPlanner.PromoteProducedFileToFinalLocation(sourcePath, finalPath);

    public static bool TryFinalizeExtractedDiscImageOutput(ChdmanExtractionKind kind, string pendingOutputPath, string finalOutputPath, out string failureMessageKey) =>
        WorkflowOutputPathPlanner.TryFinalizeExtractedDiscImageOutput(kind, pendingOutputPath, finalOutputPath, out failureMessageKey);

    public static void MoveCompanionFileIfExists(string sourcePath, string finalPath, string extension) =>
        WorkflowOutputPathPlanner.MoveCompanionFileIfExists(sourcePath, finalPath, extension);

    public static void MoveExtractedCompanionFilesIfNeeded(ChdmanExtractionKind kind, string sourcePath, string finalPath) =>
        WorkflowOutputPathPlanner.MoveExtractedCompanionFilesIfNeeded(kind, sourcePath, finalPath);

    public static void TryCleanupEmptyFinalOutputDirectory(string finalOutputPath) =>
        WorkflowOutputPathPlanner.TryCleanupEmptyFinalOutputDirectory(finalOutputPath);

    public static bool TryBuildRegionFolderName(string originalPath, string workingInputPath, out string folderName) =>
        WorkflowOutputPathPlanner.TryBuildRegionFolderName(originalPath, workingInputPath, out folderName);

    public static bool TryBuildPlatformFolderName(string? platformName, out string folderName) =>
        WorkflowOutputPathPlanner.TryBuildPlatformFolderName(platformName, out folderName);

    public static string BuildArchiveExtractionDirectory(string originalArchivePath) =>
        WorkflowPendingPathPolicy.BuildArchiveExtractionDirectory(originalArchivePath);

    public static void CopyMatchingSbiIfExists(string workingInputPath, string outputChdPath) =>
        WorkflowPendingPathPolicy.CopyMatchingSbiIfExists(workingInputPath, outputChdPath);

    public static string SanitizePathSegment(string value) =>
        WorkflowPendingPathPolicy.SanitizePathSegment(value);

    public static bool PathsEqual(string left, string right) =>
        WorkflowPendingPathPolicy.PathsEqual(left, right);

    public static long TryGetFileSize(string path) =>
        WorkflowPendingPathPolicy.TryGetFileSize(path);

    public static int MapProgressRange(int rawValue, int minimum, int maximum) =>
        WorkflowPendingPathPolicy.MapProgressRange(rawValue, minimum, maximum);

    public static double MapProgressRange(int rawValue, double minimum, double maximum) =>
        WorkflowPendingPathPolicy.MapProgressRange(rawValue, minimum, maximum);

    public static string DetermineRequestedAction(string path) =>
        WorkflowPendingPathPolicy.DetermineRequestedAction(path);

    public static void NormalizeCuePrimaryBinReference(string cuePath) =>
        WorkflowSafePathValidator.NormalizeCuePrimaryBinReference(cuePath);

    public static bool TryNormalizeCuePrimaryBinReference(string cuePath, out string failureMessageKey) =>
        WorkflowSafePathValidator.TryNormalizeCuePrimaryBinReference(cuePath, out failureMessageKey);

    public static bool TryNormalizeCuePrimaryBinReference(
        string cuePath,
        bool allowConstrainedAbsoluteBinReference,
        out string failureMessageKey) =>
        WorkflowSafePathValidator.TryNormalizeCuePrimaryBinReference(
            cuePath,
            allowConstrainedAbsoluteBinReference,
            out failureMessageKey);

    public static bool TryNormalizeExtractedCueBinPair(string pendingCuePath, string finalCuePath, out string failureMessageKey) =>
        WorkflowSafePathValidator.TryNormalizeExtractedCueBinPair(pendingCuePath, finalCuePath, out failureMessageKey);
}
