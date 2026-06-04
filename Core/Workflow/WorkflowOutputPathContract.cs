using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Globalization;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowOutputPathContract
{
    private const int PendingWorkspaceDiagnosticStemLimit = 24;
    private const int PendingWorkspaceGuidLength = 12;
    private const string InvalidOutputPathKey = "LocConversion_InvalidOutputPath";
    private const string OutputDirectoryMissingKey = "LocConversion_OutputDirectoryMissing";
    private const string CustomOutputRequiredKey = "LocAdv_ErrorCustomOutputRequired";
    private const string PendingWorkspaceValidationFailedKey = "LocAdv_ErrorPendingWorkspaceValidationFailed";
    private const string InputFileNotFoundKey = "LocWorkflow_InputFileNotFound";

    public static string BuildFinalExtractOutputPath(
        string detectedPlatform,
        string originalPath,
        string chdPath,
        string outputExtension,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string rootDirectory = ResolveOutputRootDirectory(originalPath, chdPath, detectedPlatform, settings);
        string fileName = BuildSafeFileName(Path.GetFileNameWithoutExtension(chdPath), outputExtension);

        return ValidateFinalOutputPath(Path.Combine(rootDirectory, fileName), rootDirectory);
    }

    public static string BuildFinalChdOutputPath(
        string detectedPlatform,
        string originalPath,
        string workingInputPath,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string rootDirectory = ResolveOutputRootDirectory(originalPath, workingInputPath, detectedPlatform, settings);
        string namingPath = ResolveOutputNamingPath(originalPath, workingInputPath);
        string fileName = BuildSafeFileName(Path.GetFileNameWithoutExtension(namingPath), ".chd");

        return ValidateFinalOutputPath(Path.Combine(rootDirectory, fileName), rootDirectory);
    }

    public static string BuildFinalVerifiedChdPath(
        string detectedPlatform,
        string originalPath,
        string chdPath,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.UseCustomOutputRoot && !settings.OrganizeByPlatform && !settings.OrganizeByRegion)
        {
            return NormalizeFullPath(chdPath);
        }

        string rootDirectory = ResolveOutputRootDirectory(originalPath, chdPath, detectedPlatform, settings);
        string fileName = BuildSafeFileName(Path.GetFileNameWithoutExtension(chdPath), Path.GetExtension(chdPath));

        return ValidateFinalOutputPath(Path.Combine(rootDirectory, fileName), rootDirectory);
    }

    public static string ResolveBaseOutputRoot(
        string originalPath,
        string _,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.UseCustomOutputRoot)
        {
            if (string.IsNullOrWhiteSpace(settings.CustomOutputRoot))
            {
                throw new InvalidOperationException(CustomOutputRequiredKey);
            }

            return NormalizeOutputDirectory(settings.CustomOutputRoot, requireFullyQualifiedPath: true);
        }

        string originalFullPath = NormalizeCandidatePath(originalPath);
        string? outputAnchorDirectory = Path.GetDirectoryName(originalFullPath);

        return NormalizeOutputDirectory(outputAnchorDirectory ?? AppContext.BaseDirectory, requireFullyQualifiedPath: false);
    }

    public static string ResolveOutputRootDirectory(
        string originalPath,
        string workingInputPath,
        string detectedPlatform,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string rootDirectory = ResolveBaseOutputRoot(originalPath, workingInputPath, settings);

        string platformFolderName = string.Empty;
        string regionFolderName = string.Empty;

        bool hasPlatformFolder = settings.OrganizeByPlatform
            && WorkflowPathUtilities.TryBuildPlatformFolderName(detectedPlatform, out platformFolderName);

        bool hasRegionFolder = settings.OrganizeByRegion
            && WorkflowPathUtilities.TryBuildRegionFolderName(originalPath, workingInputPath, out regionFolderName);

        if (hasPlatformFolder
            && hasRegionFolder
            && EndsWithPathSegments(rootDirectory, platformFolderName, regionFolderName))
        {
            return NormalizeOutputDirectory(rootDirectory, requireFullyQualifiedPath: false);
        }

        if (hasPlatformFolder && !EndsWithPathSegment(rootDirectory, platformFolderName))
        {
            rootDirectory = Path.Combine(rootDirectory, platformFolderName);
        }

        if (hasRegionFolder && !EndsWithPathSegment(rootDirectory, regionFolderName))
        {
            rootDirectory = Path.Combine(rootDirectory, regionFolderName);
        }

        return NormalizeOutputDirectory(rootDirectory, requireFullyQualifiedPath: false);
    }

    public static string BuildPendingOutputPath(
        string _,
        string workingInputPath,
        string outputExtension,
        string resolvedOutputRoot,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string workspaceRoot = ResolveSmartPendingWorkspaceRoot(resolvedOutputRoot, settings);
        string jobDirectory = Path.Combine(workspaceRoot, BuildPendingWorkspaceJobFolderName(workingInputPath));

        if (!IsSamePathOrChild(jobDirectory, workspaceRoot))
        {
            throw new InvalidOperationException(PendingWorkspaceValidationFailedKey);
        }

        Directory.CreateDirectory(jobDirectory);

        if (HasReparsePointInExistingPathFromVolumeRoot(jobDirectory))
        {
            throw new InvalidOperationException(PendingWorkspaceValidationFailedKey);
        }

        TryMarkPendingWorkspaceTreeHidden(jobDirectory, workspaceRoot);

        string fileName = BuildExternalToolSafePendingFileName(outputExtension);

        return ValidatePendingWorkspacePath(Path.Combine(jobDirectory, fileName), jobDirectory);
    }

    public static void PromoteProducedFileToFinalLocation(string pendingOutputPath, string finalOutputPath)
    {
        if (string.IsNullOrWhiteSpace(pendingOutputPath))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        if (string.IsNullOrWhiteSpace(finalOutputPath))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        string sourcePath = NormalizeFullPath(pendingOutputPath);
        string targetPath = NormalizeFullPath(finalOutputPath);

        if (PathsEqualInternal(sourcePath, targetPath))
        {
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(InputFileNotFoundKey, sourcePath);
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(sourcePath)
            || HasReparsePointInExistingPathFromVolumeRoot(targetPath))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        string finalDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(OutputDirectoryMissingKey);

        Directory.CreateDirectory(finalDirectory);

        if (HasReparsePointInExistingPathFromVolumeRoot(finalDirectory))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        string? backupPath = null;
        bool hadExistingFinal = File.Exists(targetPath);
        bool finalWasBackedUp = false;
        bool promoted = false;

        try
        {
            if (hadExistingFinal)
            {
                backupPath = BuildRollbackBackupPath(targetPath);
                File.Move(targetPath, backupPath);
                finalWasBackedUp = true;
            }

            MoveFileWithCrossVolumeFallback(sourcePath, targetPath);
            promoted = true;
        }
        catch
        {
            if (!promoted)
            {
                if (finalWasBackedUp || !hadExistingFinal)
                {
                    TryDeleteFile(targetPath);
                }

                if (finalWasBackedUp
                    && backupPath is not null
                    && File.Exists(backupPath)
                    && !File.Exists(targetPath))
                {
                    try
                    {
                        File.Move(backupPath, targetPath);
                        finalWasBackedUp = false;
                    }
                    catch
                    {
                    }
                }
            }

            throw;
        }
        finally
        {
            if (promoted && backupPath is not null && File.Exists(backupPath))
            {
                TryDeleteFile(backupPath);
            }
        }
    }

    private static string ResolveSmartPendingWorkspaceRoot(string resolvedOutputRoot, AppSettings settings)
    {
        string workspaceRoot = NormalizeFullPath(PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(resolvedOutputRoot, settings));

        if (HasReparsePointInExistingPathFromVolumeRoot(workspaceRoot))
        {
            throw new InvalidOperationException(PendingWorkspaceValidationFailedKey);
        }

        return workspaceRoot;
    }

    private static string BuildPendingWorkspaceJobFolderName(string workingInputPath)
    {
        string stem = SanitizePendingOutputFileStemForIsolation(Path.GetFileNameWithoutExtension(workingInputPath));

        if (stem.Length > PendingWorkspaceDiagnosticStemLimit)
        {
            stem = stem[..PendingWorkspaceDiagnosticStemLimit].Trim('_', '.', ' ');
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "output";
        }

        string shortGuid = Guid.NewGuid().ToString("N")[..PendingWorkspaceGuidLength];

        return PendingWorkspacePathPolicy.OperationFolderPrefix
            + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
            + "_"
            + stem
            + "_"
            + shortGuid;
    }

    private static string BuildExternalToolSafePendingFileName(string outputExtension) =>
        "output" + NormalizeOutputExtension(outputExtension);

    private static string ValidatePendingWorkspacePath(string candidatePath, string requiredRoot)
    {
        string fullRoot = NormalizeFullPath(requiredRoot);
        string fullCandidate = NormalizeFullPath(candidatePath);

        if (!IsSamePathOrChild(fullCandidate, fullRoot)
            || PathsEqualInternal(fullCandidate, fullRoot)
            || HasReparsePointInExistingPath(fullCandidate, fullRoot))
        {
            throw new InvalidOperationException(PendingWorkspaceValidationFailedKey);
        }

        return fullCandidate;
    }

    private static bool IsSameVolumeForPendingWorkspace(string leftPath, string rightPath)
    {
        try
        {
            string? leftRoot = Path.GetPathRoot(NormalizeFullPath(leftPath));
            string? rightRoot = Path.GetPathRoot(NormalizeFullPath(rightPath));

            return !string.IsNullOrWhiteSpace(leftRoot)
                && !string.IsNullOrWhiteSpace(rightRoot)
                && string.Equals(
                    leftRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    rightRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return false;
        }
    }

    private static void TryMarkPendingWorkspaceTreeHidden(string jobDirectory, string workspaceRoot)
    {
        TryMarkPendingWorkspaceDirectoryHidden(jobDirectory);
        TryMarkPendingWorkspaceDirectoryHidden(workspaceRoot);

        try
        {
            string? appRoot = Directory.GetParent(workspaceRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(appRoot)
                && PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(Path.GetFileName(appRoot)))
            {
                TryMarkPendingWorkspaceDirectoryHidden(appRoot);
            }
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
        }
    }

    private static void TryMarkPendingWorkspaceDirectoryHidden(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            if (HasReparsePointInExistingPathFromVolumeRoot(directory))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(directory);

            if ((attributes & FileAttributes.Hidden) == 0)
            {
                File.SetAttributes(directory, attributes | FileAttributes.Hidden | FileAttributes.NotContentIndexed);
            }
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
        }
    }

    private static void MoveFileWithCrossVolumeFallback(string sourcePath, string targetPath)
    {
        try
        {
            File.Move(sourcePath, targetPath);
        }
        catch (IOException) when (!IsSameVolumeForPendingWorkspace(sourcePath, targetPath))
        {
            File.Copy(sourcePath, targetPath, overwrite: false);
            File.Delete(sourcePath);
        }
    }

    private static string BuildRollbackBackupPath(string finalOutputPath)
    {
        string directory = Path.GetDirectoryName(finalOutputPath)
            ?? throw new InvalidOperationException(OutputDirectoryMissingKey);

        if (HasReparsePointInExistingPathFromVolumeRoot(directory))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        string fileName = Path.GetFileName(finalOutputPath);

        for (int i = 0; i < 32; i++)
        {
            string candidate = Path.Combine(directory, fileName + ".hakamiq-backup-" + Guid.NewGuid().ToString("N"));

            if (!File.Exists(candidate) && IsSamePathOrChild(candidate, directory))
            {
                return candidate;
            }
        }

        throw new IOException(InvalidOutputPathKey);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path) && !HasReparsePointInExistingPathFromVolumeRoot(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
        }
    }

    private static string ValidateFinalOutputPath(string finalPath, string outputRoot)
    {
        string fullOutputRoot = NormalizeOutputDirectory(outputRoot, requireFullyQualifiedPath: false);
        string fullFinalPath = NormalizeFullPath(finalPath);
        string? finalDirectory = Path.GetDirectoryName(fullFinalPath);

        if (string.IsNullOrWhiteSpace(finalDirectory))
        {
            throw new InvalidOperationException(OutputDirectoryMissingKey);
        }

        string normalizedFinalDirectory = NormalizeOutputDirectory(finalDirectory, requireFullyQualifiedPath: false);

        EnsureDirectoryIsNotPendingStore(normalizedFinalDirectory, PendingWorkspaceValidationFailedKey);

        if (!IsSamePathOrChild(normalizedFinalDirectory, fullOutputRoot))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        Directory.CreateDirectory(normalizedFinalDirectory);

        if (HasReparsePointInExistingPathFromVolumeRoot(normalizedFinalDirectory)
            || HasReparsePointInExistingPathFromVolumeRoot(fullFinalPath))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        return fullFinalPath;
    }

    private static string ResolveOutputNamingPath(string originalPath, string workingInputPath)
    {
        string originalFullPath = NormalizeCandidatePath(originalPath);
        string workingFullPath = NormalizeCandidatePath(workingInputPath);

        return AppPaths.IsPathUnderProcessTempRoot(workingFullPath)
            ? originalFullPath
            : workingFullPath;
    }

    private static string NormalizeCandidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return AppContext.BaseDirectory;
        }

        return NormalizeFullPath(path.Trim());
    }

    private static string NormalizeOutputDirectory(string path, bool requireFullyQualifiedPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        string trimmed = path.Trim();

        if (requireFullyQualifiedPath && !Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        string fullPath = NormalizeFullPath(trimmed);

        EnsureDirectoryIsNotPendingStore(fullPath, PendingWorkspaceValidationFailedKey);

        if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        return fullPath;
    }

    private static string BuildSafeFileName(string? baseName, string extension)
    {
        string safeBaseName = WorkflowPathUtilities.SanitizePathSegment(baseName ?? string.Empty);
        string safeExtension = NormalizeOutputExtension(extension);

        if (string.IsNullOrWhiteSpace(safeBaseName))
        {
            safeBaseName = "output";
        }

        return safeBaseName + safeExtension;
    }

    private static string NormalizeOutputExtension(string extension)
    {
        string normalized = string.IsNullOrWhiteSpace(extension)
            ? ".tmp"
            : extension.Trim();

        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        if (normalized.Contains(Path.DirectorySeparatorChar)
            || normalized.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException(InvalidOutputPathKey);
        }

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            if (normalized.Contains(invalidChar))
            {
                throw new InvalidOperationException(InvalidOutputPathKey);
            }
        }

        return normalized;
    }

    private static void EnsureDirectoryIsNotPendingStore(string directoryPath, string message)
    {
        string fullPath = NormalizeFullPath(directoryPath);

        if (PendingWorkspacePathPolicy.IsReservedWorkspacePath(fullPath))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = NormalizeFullPath(candidatePath);
        string root = NormalizeFullPath(rootPath);

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndsWithPathSegments(string path, params string[] segments)
    {
        if (segments.Length == 0)
        {
            return false;
        }

        string current = NormalizeFullPath(path);

        for (int index = segments.Length - 1; index >= 0; index--)
        {
            string expected = segments[index];
            if (string.IsNullOrWhiteSpace(expected)
                || !string.Equals(Path.GetFileName(current), expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return index == 0;
            }

            current = NormalizeFullPath(parent);
        }

        return true;
    }

    private static bool EndsWithPathSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileName(NormalizeFullPath(path)),
            segment,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizePendingOutputFileStemForIsolation(string? value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? "output" : value.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }

        result = result.Replace(' ', '_');

        while (result.Contains("__", StringComparison.Ordinal))
        {
            result = result.Replace("__", "_", StringComparison.Ordinal);
        }

        result = result.Trim('_', '.', ' ');

        return string.IsNullOrWhiteSpace(result) ? "output" : result;
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string root = NormalizeFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsExistingPathReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqualInternal(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqualInternal(parent, current))
                {
                    return true;
                }

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static string NormalizeFullPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool PathsEqualInternal(string left, string right) =>
        string.Equals(NormalizeFullPath(left), NormalizeFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsIoOrPathFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}