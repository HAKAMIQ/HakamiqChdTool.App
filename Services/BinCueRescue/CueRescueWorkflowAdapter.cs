using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed class CueRescueWorkflowAdapter : IDisposable
{
    private const string BinInputMissingDetailKey = "LocStatus_BinInputDoesNotExist";
    private const string AdjacentCueMissingDetailKey = "LocStatus_AdjacentCueDoesNotExist";
    private const string GeneratedCueMissingDetailKey = "LocStatus_GeneratedCueDoesNotExist";
    private const string RescueCuePreparationFailedDetailKey = "LocStatus_RescueCuePreparationFailed";
    private const string BinCueRescuePrepareFailedDetailKey = "LocBinCueRescue_PrepareFailed";

    private readonly object _syncRoot = new();
    private readonly List<CleanupDirectoryRegistration> _tempDirectoriesToCleanup = [];

    private int _disposed;

    internal CueRescueWorkflowPrepareResult TryPrepare(
        string? inputPath,
        string? processTempRoot = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return CueRescueWorkflowPrepareResult.NotApplicable(inputPath);
        }

        string fullInputPath;
        try
        {
            fullInputPath = NormalizeFullPath(inputPath);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return CueRescueWorkflowPrepareResult.Failed(
                inputPath,
                BinCueRescuePrepareFailedDetailKey);
        }

        if (!string.Equals(Path.GetExtension(fullInputPath), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            return CueRescueWorkflowPrepareResult.NotApplicable(fullInputPath);
        }

        if (!File.Exists(fullInputPath) || HasReparsePointInExistingPathFromVolumeRoot(fullInputPath))
        {
            return CueRescueWorkflowPrepareResult.Failed(
                fullInputPath,
                BinInputMissingDetailKey);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string leaderCueWriteTarget = Path.ChangeExtension(fullInputPath, ".cue");

        BinCueRescuePlan plan;
        try
        {
            plan = MultiBinDiscAssembler.AssembleForBin(
                fullInputPath,
                leaderCueWriteTarget);
        }
        catch (Exception ex) when (IsExpectedAssemblerFailure(ex))
        {
            return CueRescueWorkflowPrepareResult.Failed(
                fullInputPath,
                BinCueRescuePrepareFailedDetailKey);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (plan.CanUseAdjacentCue)
        {
            string adjacentCuePath;
            try
            {
                adjacentCuePath = NormalizeFullPath(plan.AdjacentCuePath!);
            }
            catch (Exception ex) when (IsPathFailure(ex))
            {
                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    BinCueRescuePrepareFailedDetailKey);
            }

            if (!File.Exists(adjacentCuePath))
            {
                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    AdjacentCueMissingDetailKey);
            }

            if (!IsAdjacentCueInSameDirectory(fullInputPath, adjacentCuePath)
                || HasReparsePointInExistingPathFromVolumeRoot(adjacentCuePath))
            {
                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    BinCueRescuePrepareFailedDetailKey);
            }

            return CueRescueWorkflowPrepareResult.Prepared(
                fullInputPath,
                adjacentCuePath,
                null,
                null);
        }

        if (plan.CanGenerateTempCue)
        {
            string effectiveProcessTempRoot = string.IsNullOrWhiteSpace(processTempRoot)
                ? AppPaths.ProcessTempRoot
                : processTempRoot;

            CueRescueWriteResult writeResult = CueRescueWriter.Write(
                plan,
                effectiveProcessTempRoot,
                cancellationToken);

            if (!writeResult.Succeeded || string.IsNullOrWhiteSpace(writeResult.CuePath))
            {
                TryCleanupPreparedTempDirectory(writeResult.TempDirectoryToCleanup, effectiveProcessTempRoot);

                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    RescueCuePreparationFailedDetailKey);
            }

            string generatedCuePath;
            string fullTempDirectory;
            string fullProcessTempRoot;

            try
            {
                generatedCuePath = NormalizeFullPath(writeResult.CuePath);
                fullTempDirectory = NormalizeFullPath(writeResult.TempDirectoryToCleanup!);
                fullProcessTempRoot = NormalizeFullPath(effectiveProcessTempRoot);
            }
            catch (Exception ex) when (IsPathFailure(ex))
            {
                TryCleanupPreparedTempDirectory(writeResult.TempDirectoryToCleanup, effectiveProcessTempRoot);

                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    RescueCuePreparationFailedDetailKey);
            }

            if (!File.Exists(generatedCuePath))
            {
                TryCleanupPreparedTempDirectory(fullTempDirectory, fullProcessTempRoot);

                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    GeneratedCueMissingDetailKey);
            }

            if (!IsSafeCleanupDirectory(fullTempDirectory, fullProcessTempRoot))
            {
                TryCleanupPreparedTempDirectory(fullTempDirectory, fullProcessTempRoot);

                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    RescueCuePreparationFailedDetailKey);
            }

            if (!IsPathInsideDirectory(generatedCuePath, fullTempDirectory)
                || HasReparsePointInExistingPath(generatedCuePath, fullTempDirectory))
            {
                TryCleanupPreparedTempDirectory(fullTempDirectory, fullProcessTempRoot);

                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    RescueCuePreparationFailedDetailKey);
            }

            try
            {
                RegisterTempDirectoryForCleanup(
                    fullTempDirectory,
                    fullProcessTempRoot);
            }
            catch (ObjectDisposedException)
            {
                TryCleanupPreparedTempDirectory(fullTempDirectory, fullProcessTempRoot);

                return CueRescueWorkflowPrepareResult.Failed(
                    fullInputPath,
                    RescueCuePreparationFailedDetailKey);
            }

            return CueRescueWorkflowPrepareResult.Prepared(
                fullInputPath,
                generatedCuePath,
                fullTempDirectory,
                null);
        }

        if (plan.IsRefused || plan.IsAmbiguous)
        {
            return CueRescueWorkflowPrepareResult.Failed(
                fullInputPath,
                BinCueRescuePrepareFailedDetailKey);
        }

        return CueRescueWorkflowPrepareResult.Failed(
            fullInputPath,
            BinCueRescuePrepareFailedDetailKey);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<CleanupDirectoryRegistration> cleanupRegistrations;

        lock (_syncRoot)
        {
            cleanupRegistrations = [.. _tempDirectoriesToCleanup];
            _tempDirectoriesToCleanup.Clear();
        }

        for (int i = cleanupRegistrations.Count - 1; i >= 0; i--)
        {
            CleanupDirectoryRegistration cleanup = cleanupRegistrations[i];

            if (!IsSafeCleanupDirectory(cleanup.Directory, cleanup.ProcessTempRoot))
            {
                continue;
            }

            TryDeleteDirectoryTree(cleanup.Directory, cleanup.ProcessTempRoot);
        }
    }

    private void RegisterTempDirectoryForCleanup(
        string directory,
        string processTempRoot)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            _tempDirectoriesToCleanup.Add(
                new CleanupDirectoryRegistration(
                    directory,
                    processTempRoot));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static void TryCleanupPreparedTempDirectory(
        string? directory,
        string? processTempRoot)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(processTempRoot)
            || !IsSafeCleanupDirectory(directory, processTempRoot))
        {
            return;
        }

        TryDeleteDirectoryTree(directory, processTempRoot);
    }

    private static bool IsAdjacentCueInSameDirectory(
        string binPath,
        string cuePath)
    {
        try
        {
            string? binDirectory = Path.GetDirectoryName(NormalizeFullPath(binPath));
            string? cueDirectory = Path.GetDirectoryName(NormalizeFullPath(cuePath));

            return !string.IsNullOrWhiteSpace(binDirectory)
                   && !string.IsNullOrWhiteSpace(cueDirectory)
                   && string.Equals(binDirectory, cueDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }
    }

    private static bool IsSafeCleanupDirectory(
        string directory,
        string processTempRoot)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(processTempRoot))
        {
            return false;
        }

        string fullDirectory;
        string fullProcessTempRoot;

        try
        {
            fullDirectory = NormalizeFullPath(directory);
            fullProcessTempRoot = NormalizeFullPath(processTempRoot);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }

        return !PathsEqual(fullDirectory, fullProcessTempRoot)
               && IsSamePathOrChild(fullDirectory, fullProcessTempRoot);
    }

    private static bool IsPathInsideDirectory(
        string candidatePath,
        string directoryPath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string directory = NormalizeFullPath(directoryPath);

            return !PathsEqual(candidate, directory)
                   && candidate.StartsWith(
                       EnsureDirectorySeparatorSuffix(directory),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }
    }

    private static void TryDeleteDirectoryTree(
        string directory,
        string processTempRoot)
    {
        try
        {
            if (!IsSafeCleanupDirectory(directory, processTempRoot) || !Directory.Exists(directory))
            {
                return;
            }

            if (IsExistingPathReparsePoint(directory))
            {
                TryDeleteDirectoryLinkOnly(directory);
                return;
            }

            if (HasReparsePointInExistingPath(directory, processTempRoot))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (!IsPathInsideDirectory(file, directory))
                {
                    continue;
                }

                TryDeleteFile(file);
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directory))
            {
                if (!IsPathInsideDirectory(childDirectory, directory))
                {
                    continue;
                }

                if (IsExistingPathReparsePoint(childDirectory))
                {
                    TryDeleteDirectoryLinkOnly(childDirectory);
                    continue;
                }

                TryDeleteDirectoryTree(childDirectory, processTempRoot);
            }

            ClearBlockingDirectoryAttributes(directory);
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception ex) when (IsCleanupFailure(ex))
        {
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return;
            }

            File.Delete(file);
        }
        catch (Exception ex) when (IsCleanupFailure(ex))
        {
        }
    }

    private static void TryDeleteDirectoryLinkOnly(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            Directory.Delete(directory, recursive: false);
        }
        catch (Exception ex) when (IsCleanupFailure(ex))
        {
        }
    }

    private static void ClearBlockingDirectoryAttributes(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            if (IsExistingPathReparsePoint(directory))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(directory);
            FileAttributes cleaned = attributes
                & ~FileAttributes.ReadOnly
                & ~FileAttributes.Hidden
                & ~FileAttributes.System;

            if ((cleaned & FileAttributes.Directory) != FileAttributes.Directory)
            {
                cleaned |= FileAttributes.Directory;
            }

            if (cleaned != attributes)
            {
                File.SetAttributes(directory, cleaned);
            }
        }
        catch (Exception ex) when (IsCleanupFailure(ex))
        {
        }
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
        catch (Exception ex) when (IsCleanupFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(
        string candidatePath,
        string rootPath)
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

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsCleanupFailure(ex))
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
        catch (Exception ex) when (IsCleanupFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(
        string candidatePath,
        string rootPath)
    {
        string candidate = NormalizeFullPath(candidatePath);
        string root = NormalizeFullPath(rootPath);

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
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

    private static bool IsExpectedAssemblerFailure(Exception ex)
    {
        return ex is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }

    private static bool IsPathFailure(Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsCleanupFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }

    private sealed record CleanupDirectoryRegistration(
        string Directory,
        string ProcessTempRoot);
}

internal sealed record CueRescueWorkflowPrepareResult
{
    private CueRescueWorkflowPrepareResult(
        bool applied,
        bool isFailed,
        string? originalInputPath,
        string? effectiveInputPath,
        string? tempDirectoryToCleanup,
        string? detail)
    {
        if (applied && isFailed)
        {
            throw new ArgumentException("Preparation result cannot be both applied and failed.");
        }

        if (applied)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(originalInputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(effectiveInputPath);
        }

        if (isFailed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(originalInputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        }

        Applied = applied;
        IsFailed = isFailed;
        OriginalInputPath = originalInputPath;
        EffectiveInputPath = effectiveInputPath;
        TempDirectoryToCleanup = tempDirectoryToCleanup;
        Detail = detail;
    }

    internal bool Applied { get; }

    internal bool IsFailed { get; }

    internal string? OriginalInputPath { get; }

    internal string? EffectiveInputPath { get; }

    internal string? TempDirectoryToCleanup { get; }

    internal string? Detail { get; }

    internal static CueRescueWorkflowPrepareResult NotApplicable(string? inputPath)
    {
        return new CueRescueWorkflowPrepareResult(
            false,
            false,
            inputPath,
            inputPath,
            null,
            null);
    }

    internal static CueRescueWorkflowPrepareResult Prepared(
        string originalInputPath,
        string effectiveInputPath,
        string? tempDirectoryToCleanup,
        string? detail)
    {
        return new CueRescueWorkflowPrepareResult(
            true,
            false,
            originalInputPath,
            effectiveInputPath,
            tempDirectoryToCleanup,
            detail);
    }

    internal static CueRescueWorkflowPrepareResult Failed(
        string originalInputPath,
        string? detail)
    {
        return new CueRescueWorkflowPrepareResult(
            false,
            true,
            originalInputPath,
            originalInputPath,
            null,
            detail);
    }
}