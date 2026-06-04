using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public sealed class CleanupService
{
    private static readonly ILogger Logger = global::Serilog.Log.ForContext<CleanupService>();
    private const int DirectoryDeleteAttemptCount = 6;
    private static readonly TimeSpan DirectoryDeleteRetryDelay = TimeSpan.FromMilliseconds(150);

    public CleanupStats DeleteDirectoryTree(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return CleanupStats.Empty;
        }

        string fullDir;
        try
        {
            fullDir = Path.GetFullPath(directoryPath);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Cleanup: could not normalize directory path. Path={Path}", directoryPath);
            return CleanupStats.Empty;
        }

        if (!Directory.Exists(fullDir))
        {
            return CleanupStats.Empty;
        }

        if (!AppPaths.IsPathUnderProcessTempRoot(fullDir))
        {
            Logger.Debug("Cleanup: skipped directory delete; path is not under the isolated process temp root. Path={Path}", fullDir);
            return CleanupStats.Empty;
        }

        CleanupStats stats = CollectDirectoryDeletionStats(fullDir);
        return TryDeleteDirectoryRecursive(fullDir) ? stats : CleanupStats.Empty;
    }

    public CleanupStats DeletePendingWorkspaceDirectoryTree(string? directoryPath, AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return CleanupStats.Empty;
        }

        string fullDir;
        try
        {
            fullDir = Path.GetFullPath(directoryPath);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Cleanup: could not normalize pending workspace path. Path={Path}", directoryPath);
            return CleanupStats.Empty;
        }

        if (!Directory.Exists(fullDir))
        {
            return CleanupStats.Empty;
        }

        if (!AppPaths.IsKnownPendingWorkspaceJobDirectory(fullDir, settings))
        {
            Logger.Warning(
                "Cleanup: refusing pending workspace delete; path is not a known pending job directory. Path={Path}",
                fullDir);
            return CleanupStats.Empty;
        }

        if (ContainsReparsePoint(fullDir))
        {
            Logger.Warning(
                "Cleanup: refusing pending workspace delete because the job directory contains a reparse point. Path={Path}",
                fullDir);
            return CleanupStats.Empty;
        }

        CleanupStats stats = CollectDirectoryDeletionStats(fullDir);
        if (!TryDeleteDirectoryRecursive(fullDir))
        {
            return CleanupStats.Empty;
        }

        TryCleanupPendingWorkspaceParentsForJobDirectory(fullDir, settings);
        return stats;
    }

    public CleanupStats DeleteFiles(params string?[] filePaths)
    {
        long bytes = 0;
        int files = 0;

        foreach (string? filePath in filePaths)
        {
            if (!TryResolveDeletableFile(filePath, out string fullPath))
            {
                continue;
            }

            try
            {
                long length = TryGetFileLength(fullPath);
                File.Delete(fullPath);
                bytes += length;
                files++;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Cleanup: failed to delete file. File={File}", fullPath);
            }
        }

        return new CleanupStats(bytes, files);
    }

    private static CleanupStats CollectDirectoryDeletionStats(string fullDirectoryPath)
    {
        long bytes = 0;
#if DEBUG
        int fileCount = 0;
#endif

        try
        {
            foreach (string file in Directory.EnumerateFiles(fullDirectoryPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
#if DEBUG
                    fileCount++;
#endif
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Cleanup: could not read file size during pre-delete enumeration. File={File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(
                ex,
                "Cleanup: directory enumeration for size stats failed; deletion will still be attempted. Path={Path}",
                fullDirectoryPath);
        }

#if DEBUG
        return new CleanupStats(bytes, fileCount);
#else
        return new CleanupStats(bytes, DeletedFiles: 0);
#endif
    }

    private static long TryGetFileLength(string fullPath)
    {
        try
        {
            return new FileInfo(fullPath).Length;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Cleanup: could not read file size before delete. File={File}", fullPath);
            return 0;
        }
    }

    private static bool TryDeleteDirectoryRecursive(string fullDirectoryPath)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= DirectoryDeleteAttemptCount; attempt++)
        {
            try
            {
                if (Directory.Exists(fullDirectoryPath))
                {
                    Directory.Delete(fullDirectoryPath, recursive: true);
                }

                if (!Directory.Exists(fullDirectoryPath))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt < DirectoryDeleteAttemptCount)
            {
                Thread.Sleep(DirectoryDeleteRetryDelay);
            }
        }

        if (lastError is not null)
        {
            Logger.Error(lastError, "Cleanup: failed to delete directory tree. Path={Path}", fullDirectoryPath);
        }
        else
        {
            Logger.Warning("Cleanup: directory tree still exists after delete attempts. Path={Path}", fullDirectoryPath);
        }

        return false;
    }

    private static void TryCleanupPendingWorkspaceParentsForJobDirectory(string jobDirectory, AppSettings? settings)
    {
        try
        {
            string? pendingRoot = Directory.GetParent(jobDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(pendingRoot)
                && !IsCustomPendingWorkspaceRoot(pendingRoot, settings))
            {
                DeleteDirectoryIfEmpty(pendingRoot);
            }

            string? appRoot = string.IsNullOrWhiteSpace(pendingRoot)
                ? null
                : Directory.GetParent(pendingRoot)?.FullName;

            if (!string.IsNullOrWhiteSpace(appRoot)
                && PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(Path.GetFileName(appRoot)))
            {
                DeleteDirectoryIfEmpty(appRoot);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Cleanup: failed to clean pending workspace parent directories. JobDirectory={JobDirectory}", jobDirectory);
        }
    }

    private static bool IsCustomPendingWorkspaceRoot(string pendingRoot, AppSettings? settings)
    {
        if (settings is null
            || !settings.UseCustomPendingWorkspace
            || settings.PendingWorkspaceMode != PendingWorkspaceMode.Custom
            || string.IsNullOrWhiteSpace(settings.PendingWorkspaceCustomRoot))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(pendingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(settings.PendingWorkspaceCustomRoot.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Cleanup: failed to compare custom pending workspace root. PendingRoot={PendingRoot}", pendingRoot);
            return false;
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
        catch (Exception ex)
        {
            Logger.Debug(ex, "Cleanup: failed to delete empty pending workspace parent directory. Directory={Directory}", directory);
        }
    }

    private static bool ContainsReparsePoint(string directory)
    {
        if (HasReparsePoint(directory))
        {
            return true;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        };

        foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
        {
            if (HasReparsePoint(entry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Cleanup: treating unreadable path as unsafe during reparse-point scan. Path={Path}", path);
            return true;
        }
    }

    private static bool TryResolveDeletableFile(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Cleanup: could not normalize file path. Path={Path}", path);
            return false;
        }

        if (!AppPaths.IsPathUnderProcessTempRoot(fullPath))
        {
            Logger.Debug("Cleanup: skipped file delete; path is not under the isolated process temp root. Path={Path}", fullPath);
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            FileAttributes attrs = File.GetAttributes(fullPath);
            if ((attrs & FileAttributes.Directory) != 0)
            {
                Logger.Warning("Cleanup: refusing delete; path is a directory, not a file. Path={Path}", fullPath);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Cleanup: could not verify file attributes before delete. Path={Path}", fullPath);
            return false;
        }

        return true;
    }
}

public readonly record struct CleanupStats(long DeletedBytes, int DeletedFiles)
{
    public static CleanupStats Empty => new(0, 0);

    public static CleanupStats operator +(CleanupStats left, CleanupStats right) =>
        new(left.DeletedBytes + right.DeletedBytes, left.DeletedFiles + right.DeletedFiles);
}
