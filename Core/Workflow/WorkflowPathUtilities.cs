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

internal static class WorkflowPathUtilities
{
    private const int ArchiveExtractionFolderNameMaxLength = 24;
    private const int ArchiveExtractionSessionIdLength = 16;
    private const string ChdMediaDetectedReasonKey = "LocStatus_DetectedMediaArabic";
    private const string ChdMediaRawReasonKey = "LocWorkflow_RawMetadataConflictReason";
    private const string ChdContainerOnlyReasonKey = "LocPlatformDetect_ChdContainerOnly";
    private const string ExtractedCueBinInvalidKey = "LocWorkflow_ExtractedCueBinInvalid";
    private const int CueFileStatementRegexTimeoutMilliseconds = 100;

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
                return TryFinalizeExtractedCueBinOutput(pendingOutputPath, finalOutputPath, out failureMessageKey);
            }

            PromoteProducedFileToFinalLocation(pendingOutputPath, finalOutputPath);
            MoveExtractedCompanionFilesIfNeeded(kind, pendingOutputPath, finalOutputPath);
            return true;
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
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
        NormalizeCuePrimaryBinReference(finalPath);
    }

    public static void TryCleanupEmptyFinalOutputDirectory(string finalOutputPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath));
            DeleteDirectoryIfEmpty(directory);
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex) || IsExpectedCueReadFailure(ex))
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
            folderName = SanitizePathSegment(rawSerialRegion);
            return !string.IsNullOrWhiteSpace(folderName);
        }

        if (DiscMetadataProbe.TryResolveRegion(workingInputPath, out string metadataRegion)
            || DiscMetadataProbe.TryResolveRegion(originalPath, out metadataRegion))
        {
            folderName = SanitizePathSegment(metadataRegion);
            return !string.IsNullOrWhiteSpace(folderName);
        }

        if (NamingCorrectionEngine.TryExtractRegion(originalPath, out string region)
            || NamingCorrectionEngine.TryExtractRegion(workingInputPath, out region))
        {
            folderName = SanitizePathSegment(region);
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

    public static string BuildArchiveExtractionDirectory(string originalArchivePath)
    {
        string runtimeRoot = AppPaths.CombineProcessTemp("TempExtraction");
        string archiveName = BuildShortArchiveExtractionFolderName(originalArchivePath);
        string sessionId = Guid.NewGuid().ToString("N")[..ArchiveExtractionSessionIdLength];

        return Path.Combine(runtimeRoot, sessionId + "_" + archiveName);
    }

    public static void CopyMatchingSbiIfExists(string workingInputPath, string outputChdPath)
    {
        if (!string.Equals(Path.GetExtension(workingInputPath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sourceSbi = Path.ChangeExtension(workingInputPath, ".sbi");

        if (!File.Exists(sourceSbi))
        {
            return;
        }

        string destinationSbi = Path.ChangeExtension(outputChdPath, ".sbi");
        string? destinationDirectory = Path.GetDirectoryName(destinationSbi);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourceSbi, destinationSbi, overwrite: true);
    }

    public static string SanitizePathSegment(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "Item" : value;
    }

    private static string BuildShortArchiveExtractionFolderName(string originalArchivePath)
    {
        string archiveName = SanitizePathSegment(Path.GetFileNameWithoutExtension(originalArchivePath))
            .Replace(' ', '_');

        while (archiveName.Contains("__", StringComparison.Ordinal))
        {
            archiveName = archiveName.Replace("__", "_", StringComparison.Ordinal);
        }

        archiveName = archiveName.Trim('_', '.', ' ');

        if (archiveName.Length > ArchiveExtractionFolderNameMaxLength)
        {
            archiveName = archiveName[..ArchiveExtractionFolderNameMaxLength].Trim('_', '.', ' ');
        }

        return string.IsNullOrWhiteSpace(archiveName) ? "archive" : archiveName;
    }


    public static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public static long TryGetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static int MapProgressRange(int rawValue, int minimum, int maximum)
    {
        int clampedRaw = Math.Clamp(rawValue, 0, 100);

        if (maximum <= minimum)
        {
            return minimum;
        }

        double ratio = clampedRaw / 100.0;
        return minimum + (int)Math.Round((maximum - minimum) * ratio);
    }

    public static double MapProgressRange(int rawValue, double minimum, double maximum)
    {
        int clampedRaw = Math.Clamp(rawValue, 0, 100);

        if (maximum <= minimum)
        {
            return minimum;
        }

        double ratio = clampedRaw / 100.0;
        return minimum + ((maximum - minimum) * ratio);
    }

    public static string DetermineRequestedAction(string path) =>
        QueueItemOperationCatalog.GetInitialRequestedAction(path);

    public static async Task<PlatformDetectionResult> ApplyChdMediaDetectionAsync(
        IQueueItemStateSink sink,
        string chdPath,
        ChdInfoResult infoResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(infoResult);

        PlatformDetectionResult keywordDetection = await Task.Run(
            () => PlatformDetectionService.Detect(chdPath),
            cancellationToken).ConfigureAwait(false);

        PlatformDetectionResult result;

        if (string.Equals(infoResult.MediaType, "Raw", StringComparison.OrdinalIgnoreCase)
            || string.Equals(infoResult.MediaType, "Raw Disk", StringComparison.OrdinalIgnoreCase))
        {
            result = ChdWorkflowProfilePlanner.TryBuildRawMetadataConflictDetection(
                chdPath,
                keywordDetection,
                out PlatformDetectionResult conflictDetection)
                    ? conflictDetection
                    : PlatformDetectionResult.Create(
                        string.Empty,
                        string.Empty,
                        68,
                        ChdMediaRawReasonKey);
        }
        else if (PlatformDetectionService.IsActionablePlatformName(keywordDetection.PlatformName))
        {
            result = keywordDetection;
        }
        else
        {
            result = PlatformDetectionResult.Create(
                string.Empty,
                string.Empty,
                70,
                PlatformDetectionService.IsMediaOnlyPlatformName(infoResult.MediaType)
                    ? ChdMediaDetectedReasonKey
                    : ChdContainerOnlyReasonKey);
        }

        sink.RecordPlatformDetection(result.PlatformName, result.Reason);
        return result;
    }

    private readonly record struct CueDependency(
        int LineIndex,
        string SourcePath,
        string RelativeReference,
        string TargetPath);

    private static bool TryFinalizeExtractedCueBinOutput(
        string pendingCuePath,
        string finalCuePath,
        out string failureMessageKey)
    {
        failureMessageKey = ExtractedCueBinInvalidKey;
        var promotedTargets = new List<string>();

        try
        {
            string pendingCueFullPath = Path.GetFullPath(pendingCuePath);
            string finalCueFullPath = Path.GetFullPath(finalCuePath);
            string? pendingDirectory = Path.GetDirectoryName(pendingCueFullPath);
            string? finalDirectory = Path.GetDirectoryName(finalCueFullPath);

            if (string.IsNullOrWhiteSpace(pendingDirectory)
                || string.IsNullOrWhiteSpace(finalDirectory)
                || !string.Equals(Path.GetExtension(pendingCueFullPath), ".cue", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(pendingCueFullPath))
            {
                return false;
            }

            string[] lines = File.ReadAllLines(pendingCueFullPath, Encoding.UTF8);
            if (!TryCollectExtractedCueDependencies(lines, pendingDirectory, finalDirectory, out List<CueDependency> dependencies))
            {
                return false;
            }

            CueDependency[] uniqueDependencies =
            [
                .. dependencies
                    .GroupBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
            ];

            if (File.Exists(finalCueFullPath)
                || uniqueDependencies.Any(static item => File.Exists(item.TargetPath)))
            {
                return false;
            }

            foreach (CueDependency dependency in uniqueDependencies)
            {
                string? targetDirectory = Path.GetDirectoryName(dependency.TargetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                PromoteProducedFileToFinalLocation(dependency.SourcePath, dependency.TargetPath);
                promotedTargets.Add(dependency.TargetPath);
            }

            Directory.CreateDirectory(finalDirectory);
            PromoteProducedFileToFinalLocation(pendingCueFullPath, finalCueFullPath);
            promotedTargets.Add(finalCueFullPath);

            string[] rewrittenLines = RewriteCueFileReferences(lines, dependencies);
            File.WriteAllLines(finalCueFullPath, rewrittenLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (!AllCueFileReferencesAreUsable(finalCueFullPath))
            {
                CleanupPromotedExtractedTargets(promotedTargets, finalCueFullPath);
                return false;
            }

            failureMessageKey = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex) || IsExpectedCueReadFailure(ex))
        {
            CleanupPromotedExtractedTargets(promotedTargets, finalCuePath);
            failureMessageKey = ExtractedCueBinInvalidKey;
            return false;
        }
    }

    private static bool TryCollectExtractedCueDependencies(
        string[] lines,
        string pendingDirectory,
        string finalDirectory,
        out List<CueDependency> dependencies)
    {
        dependencies = [];

        for (int index = 0; index < lines.Length; index++)
        {
            if (!TryReadCueFileStatement(lines[index], out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    return false;
                }

                continue;
            }

            if (!TryResolveExtractedCueDependency(
                    index,
                    referencedFileName,
                    pendingDirectory,
                    finalDirectory,
                    out CueDependency dependency))
            {
                return false;
            }

            dependencies.Add(dependency);
        }

        return dependencies.Count > 0;
    }

    private static bool TryResolveExtractedCueDependency(
        int lineIndex,
        string referencedFileName,
        string pendingDirectory,
        string finalDirectory,
        out CueDependency dependency)
    {
        dependency = default;

        if (!IsSafeCueRelativeReference(referencedFileName))
        {
            return false;
        }

        try
        {
            string sourcePath = Path.GetFullPath(Path.Combine(pendingDirectory, referencedFileName));
            if (!IsSameDirectoryChild(pendingDirectory, sourcePath)
                || !File.Exists(sourcePath)
                || TryGetFileSize(sourcePath) <= 0)
            {
                return false;
            }

            string relativePath = Path.GetRelativePath(pendingDirectory, sourcePath);
            if (!IsSafeCueRelativeReference(relativePath))
            {
                return false;
            }

            string targetPath = Path.GetFullPath(Path.Combine(finalDirectory, relativePath));
            if (!IsSameDirectoryChild(finalDirectory, targetPath))
            {
                return false;
            }

            dependency = new CueDependency(
                lineIndex,
                sourcePath,
                NormalizeCueRelativeReference(relativePath),
                targetPath);

            return true;
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            return false;
        }
    }

    private static string[] RewriteCueFileReferences(
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<CueDependency> dependencies)
    {
        string[] lines = [.. sourceLines];
        Dictionary<int, CueDependency> byLine = dependencies.ToDictionary(static item => item.LineIndex);

        foreach (KeyValuePair<int, CueDependency> item in byLine)
        {
            string line = lines[item.Key];
            string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            lines[item.Key] = $"{leadingWhitespace}FILE \"{item.Value.RelativeReference}\" BINARY";
        }

        return lines;
    }

    private static bool IsSafeCueRelativeReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('\0')
            || value.Contains("%t", StringComparison.OrdinalIgnoreCase)
            || value.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(value)
            || ContainsParentTraversalSegment(value))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeCueRelativeReference(string value) =>
        string.Join(
            "/",
            value.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
                StringSplitOptions.RemoveEmptyEntries));

    private static void CleanupPromotedExtractedTargets(IEnumerable<string> promotedTargets, string finalCuePath)
    {
        foreach (string target in promotedTargets.OrderByDescending(static path => path.Length))
        {
            TryDeleteFile(target);
            TryCleanupEmptyFinalOutputDirectory(target);
        }

        TryDeleteFile(finalCuePath);
        TryCleanupEmptyFinalOutputDirectory(finalCuePath);
    }

    public static void NormalizeCuePrimaryBinReference(string cuePath)
    {
        if (string.IsNullOrWhiteSpace(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string binPath = Path.ChangeExtension(cuePath, ".bin");
        _ = TryNormalizeExtractedCueBinPair(cuePath, binPath, out _);
    }

    public static bool TryNormalizeCuePrimaryBinReference(string cuePath, out string failureMessageKey)
    {
        const string invalidCueKey = "LocFileIntegrity_SummaryCueInvalidPath";
        const string referenceProblemKey = "LocChdmanContract_InvalidCueBinDependency";

        failureMessageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(cuePath)
            || !File.Exists(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        try
        {
            if (AllCueFileReferencesAreUsable(cuePath))
            {
                return true;
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        failureMessageKey = referenceProblemKey;
        return false;
    }

    public static bool TryNormalizeExtractedCueBinPair(
        string cuePath,
        string binPath,
        out string failureMessageKey)
    {
        const string invalidCueKey = "LocFileIntegrity_SummaryCueInvalidPath";
        const string referenceProblemKey = "LocFileIntegrity_SummaryCueReferenceProblem";

        failureMessageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(cuePath) || string.IsNullOrWhiteSpace(binPath))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        string cueFullPath;
        string binFullPath;
        string? cueDirectory;

        try
        {
            cueFullPath = Path.GetFullPath(cuePath);
            binFullPath = Path.GetFullPath(binPath);
            cueDirectory = Path.GetDirectoryName(cueFullPath);
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        if (string.IsNullOrWhiteSpace(cueDirectory)
            || !File.Exists(cueFullPath)
            || !File.Exists(binFullPath)
            || TryGetFileSize(binFullPath) <= 0
            || !IsSameDirectoryChild(cueDirectory, binFullPath))
        {
            failureMessageKey = referenceProblemKey;
            return false;
        }

        string binName = Path.GetFileName(binFullPath);
        if (string.IsNullOrWhiteSpace(binName))
        {
            failureMessageKey = referenceProblemKey;
            return false;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(cueFullPath, Encoding.UTF8);
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
            failureMessageKey = invalidCueKey;
            return false;
        }

        if (!TryRewriteCueToSingleBin(cueFullPath, lines, binName)
            || !AllCueFileReferencesAreUsable(cueFullPath))
        {
            failureMessageKey = referenceProblemKey;
            return false;
        }

        return true;
    }

    private readonly record struct CueFileStatementSummary(
        int FileStatementCount,
        int UsableReferenceCount,
        int MalformedFileStatementCount)
    {
        public bool HasUsableContract =>
            FileStatementCount > 0
            && FileStatementCount == UsableReferenceCount
            && MalformedFileStatementCount == 0;
    }

    private static CueFileStatementSummary InspectCueFileStatements(
        IReadOnlyList<string> lines,
        string cueDirectory)
    {
        int fileStatementCount = 0;
        int usableReferenceCount = 0;
        int malformedFileStatementCount = 0;

        foreach (string line in lines)
        {
            if (!TryReadCueFileStatement(line, out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    fileStatementCount++;
                    malformedFileStatementCount++;
                }

                continue;
            }

            fileStatementCount++;
            if (IsCueReferenceUsable(cueDirectory, referencedFileName))
            {
                usableReferenceCount++;
            }
        }

        return new CueFileStatementSummary(
            fileStatementCount,
            usableReferenceCount,
            malformedFileStatementCount);
    }

    private static bool TryFindSingleRepairBin(string cueFullPath, out string repairBinName)
    {
        repairBinName = string.Empty;

        string? cueDirectory = Path.GetDirectoryName(cueFullPath);
        if (string.IsNullOrWhiteSpace(cueDirectory))
        {
            return false;
        }

        string primaryBinPath = Path.ChangeExtension(cueFullPath, ".bin");
        if (File.Exists(primaryBinPath) && TryGetFileSize(primaryBinPath) > 0)
        {
            repairBinName = Path.GetFileName(primaryBinPath);
            return !string.IsNullOrWhiteSpace(repairBinName);
        }

        string[] binCandidates;
        try
        {
            binCandidates =
            [
                .. Directory
                    .EnumerateFiles(cueDirectory, "*.bin", SearchOption.TopDirectoryOnly)
                    .Where(path => TryGetFileSize(path) > 0)
                    .Take(2)
            ];
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
            return false;
        }

        if (binCandidates.Length != 1)
        {
            return false;
        }

        repairBinName = Path.GetFileName(binCandidates[0]);
        return !string.IsNullOrWhiteSpace(repairBinName);
    }

    private static bool TryRewriteCueToSingleBin(
        string cueFullPath,
        IReadOnlyList<string> sourceLines,
        string binFileName)
    {
        if (string.IsNullOrWhiteSpace(binFileName)
            || binFileName.Contains('\0')
            || Path.IsPathRooted(binFileName)
            || ContainsParentTraversalSegment(binFileName))
        {
            return false;
        }

        List<string> lines = [.. sourceLines];
        bool foundFileStatement = false;
        bool changed = false;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            if (!IsCueFileStatementLine(line))
            {
                continue;
            }

            string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            string replacement = $"{leadingWhitespace}FILE \"{binFileName}\" BINARY";
            foundFileStatement = true;

            if (!string.Equals(line, replacement, StringComparison.Ordinal))
            {
                lines[index] = replacement;
                changed = true;
            }
        }

        if (!foundFileStatement)
        {
            int insertionIndex = FindFirstCueTrackLineIndex(lines);
            if (insertionIndex < 0)
            {
                insertionIndex = 0;
            }

            lines.Insert(insertionIndex, $"FILE \"{binFileName}\" BINARY");
            changed = true;
        }

        if (!changed)
        {
            return true;
        }

        File.WriteAllLines(cueFullPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static bool IsCueFileStatementLine(string line)
    {
        _ = TryReadCueFileStatement(line, out _, out bool hasFileStatement);
        return hasFileStatement;
    }

    private static int FindFirstCueTrackLineIndex(List<string> lines)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool AllCueFileReferencesAreUsable(string cuePath)
    {
        string? cueDirectory = Path.GetDirectoryName(Path.GetFullPath(cuePath));
        if (string.IsNullOrWhiteSpace(cueDirectory))
        {
            return false;
        }

        bool foundFileStatement = false;

        foreach (string line in File.ReadLines(cuePath, Encoding.UTF8))
        {
            if (!TryReadCueFileStatement(line, out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    return false;
                }

                continue;
            }

            foundFileStatement = true;

            if (!IsCueReferenceUsable(cueDirectory, referencedFileName))
            {
                return false;
            }
        }

        return foundFileStatement;
    }

    private static bool TryReadCueFileStatement(
        string line,
        out string referencedFileName,
        out bool hasFileStatement)
    {
        referencedFileName = string.Empty;
        hasFileStatement = false;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4]))
        {
            return false;
        }

        hasFileStatement = true;

        Match match;
        try
        {
            match = Regex.Match(
                trimmed,
                @"^FILE\s+(?:""(?<quoted>[^""]*)""|(?<plain>\S+))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(CueFileStatementRegexTimeoutMilliseconds));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        if (!match.Success)
        {
            return false;
        }

        referencedFileName = match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value.Trim()
            : match.Groups["plain"].Value.Trim();

        return !string.IsNullOrWhiteSpace(referencedFileName);
    }

    private static bool IsCueReferenceUsable(string cueDirectory, string referencedFileName)
    {
        if (string.IsNullOrWhiteSpace(referencedFileName)
            || referencedFileName.Contains('\0')
            || Path.IsPathRooted(referencedFileName)
            || ContainsParentTraversalSegment(referencedFileName))
        {
            return false;
        }

        try
        {
            string resolved = Path.GetFullPath(Path.Combine(cueDirectory, referencedFileName));
            string safeCueDirectory = Path.GetFullPath(cueDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!resolved.StartsWith(safeCueDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return File.Exists(resolved) && TryGetFileSize(resolved) > 0;
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            return false;
        }
    }

    private static bool IsSameDirectoryChild(string directory, string childPath)
    {
        try
        {
            string fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullChild = Path.GetFullPath(childPath);

            return fullChild.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedCuePathFailure(ex))
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
        }
    }

    private static void DeleteDirectoryIfEmpty(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            if (!enumerator.MoveNext())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch (Exception ex) when (IsExpectedCueReadFailure(ex))
        {
        }
    }

    private static bool IsExpectedCuePathFailure(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedCueReadFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or RegexMatchTimeoutException
        or System.Security.SecurityException;

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}