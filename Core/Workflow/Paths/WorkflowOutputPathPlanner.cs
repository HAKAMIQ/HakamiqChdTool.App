using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowOutputPathPlanner
{
    private const string ExtractedCueBinInvalidKey = "LocWorkflow_ExtractedCueBinInvalid";
    public static string BuildFinalExtractOutputPath(
        string detectedPlatform,
        string originalPath,
        string chdPath,
        string outputExtension,
        AppSettings settings) =>
        WorkflowOutputPathContract.BuildFinalExtractOutputPath(
            detectedPlatform,
            originalPath,
            chdPath,
            outputExtension,
            settings);

    public static string BuildFinalChdOutputPath(
        string detectedPlatform,
        string originalPath,
        string workingInputPath,
        AppSettings settings) =>
        WorkflowOutputPathContract.BuildFinalChdOutputPath(
            detectedPlatform,
            originalPath,
            workingInputPath,
            settings);

    public static string BuildFinalVerifiedChdPath(
        string detectedPlatform,
        string originalPath,
        string chdPath,
        AppSettings settings) =>
        WorkflowOutputPathContract.BuildFinalVerifiedChdPath(
            detectedPlatform,
            originalPath,
            chdPath,
            settings);

    public static string ResolveBaseOutputRoot(
        string originalPath,
        string workingInputPath,
        AppSettings settings) =>
        WorkflowOutputPathContract.ResolveBaseOutputRoot(originalPath, workingInputPath, settings);

    public static string ResolveOutputRootDirectory(
        string originalPath,
        string workingInputPath,
        string detectedPlatform,
        AppSettings settings) =>
        WorkflowOutputPathContract.ResolveOutputRootDirectory(
            originalPath,
            workingInputPath,
            detectedPlatform,
            settings);

    public static string BuildPendingOutputPath(
        string finalOutputPath,
        string workingInputPath,
        string outputExtension,
        string resolvedOutputRoot,
        AppSettings settings) =>
        WorkflowOutputPathContract.BuildPendingOutputPath(
            finalOutputPath,
            workingInputPath,
            outputExtension,
            resolvedOutputRoot,
            settings);

    public static void PromoteProducedFileToFinalLocation(string sourcePath, string finalPath) =>
        WorkflowOutputPathContract.PromoteProducedFileToFinalLocation(sourcePath, finalPath);

    public static bool TryFinalizeExtractedDiscImageOutput(
        ChdmanExtractionKind kind,
        string pendingOutputPath,
        string finalOutputPath,
        out string failureMessageKey)
    {
        failureMessageKey = string.Empty;

        try
        {
            if (kind == ChdmanExtractionKind.ExtractCd)
            {
                return WorkflowSafePathValidator.TryFinalizeExtractedCueBinOutput(pendingOutputPath, finalOutputPath, out failureMessageKey);
            }

            PromoteProducedFileToFinalLocation(pendingOutputPath, finalOutputPath);
            MoveExtractedCompanionFilesIfNeeded(kind, pendingOutputPath, finalOutputPath);
            return true;
        }
        catch (Exception ex) when (WorkflowSafePathValidator.IsExpectedCueReadFailure(ex))
        {
            failureMessageKey = ExtractedCueBinInvalidKey;
            return false;
        }
    }

    public static void MoveCompanionFileIfExists(string sourcePath, string finalPath, string extension)
    {
        string sourceCompanion = Path.ChangeExtension(sourcePath, extension);

        if (!File.Exists(sourceCompanion))
        {
            return;
        }

        string targetCompanion = Path.ChangeExtension(finalPath, extension);
        string? directory = Path.GetDirectoryName(targetCompanion);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PromoteProducedFileToFinalLocation(sourceCompanion, targetCompanion);
    }

    public static void MoveExtractedCompanionFilesIfNeeded(
        ChdmanExtractionKind kind,
        string sourcePath,
        string finalPath)
    {
        if (kind != ChdmanExtractionKind.ExtractCd)
        {
            return;
        }

        MoveCompanionFileIfExists(sourcePath, finalPath, ".bin");
        WorkflowSafePathValidator.NormalizeCuePrimaryBinReference(finalPath);
    }

    public static void TryCleanupEmptyFinalOutputDirectory(string finalOutputPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath));
            WorkflowSafePathValidator.DeleteDirectoryIfEmpty(directory);
        }
        catch (Exception ex) when (WorkflowSafePathValidator.IsExpectedCuePathFailure(ex) || WorkflowSafePathValidator.IsExpectedCueReadFailure(ex))
        {
        }
    }

    public static bool TryBuildRegionFolderName(
        string originalPath,
        string workingInputPath,
        out string folderName)
    {
        folderName = string.Empty;

        if (DiscRawSerialProbe.TryDetectRegion(originalPath, out string rawSerialRegion)
            || DiscRawSerialProbe.TryDetectRegion(workingInputPath, out rawSerialRegion))
        {
            folderName = WorkflowPendingPathPolicy.SanitizePathSegment(rawSerialRegion);
            return !string.IsNullOrWhiteSpace(folderName);
        }

        if (DiscMetadataProbe.TryResolveRegion(workingInputPath, out string metadataRegion)
            || DiscMetadataProbe.TryResolveRegion(originalPath, out metadataRegion))
        {
            folderName = WorkflowPendingPathPolicy.SanitizePathSegment(metadataRegion);
            return !string.IsNullOrWhiteSpace(folderName);
        }

        if (NamingCorrectionEngine.TryExtractRegion(originalPath, out string region)
            || NamingCorrectionEngine.TryExtractRegion(workingInputPath, out region))
        {
            folderName = WorkflowPendingPathPolicy.SanitizePathSegment(region);
            return !string.IsNullOrWhiteSpace(folderName);
        }

        return false;
    }

    public static bool TryBuildPlatformFolderName(string? platformName, out string folderName)
    {
        folderName = string.Empty;

        if (!PlatformDetectionService.IsOrganizablePlatformName(platformName))
        {
            return false;
        }

        string normalized = platformName!
            .Replace("/", " - ", StringComparison.Ordinal)
            .Replace("\\", " - ", StringComparison.Ordinal)
            .Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        folderName = normalized;
        return true;
    }


}
