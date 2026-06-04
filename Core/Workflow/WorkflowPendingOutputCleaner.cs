using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowPendingOutputCleaner
{
    private static readonly ILogger Logger = Log.ForContext(typeof(WorkflowPendingOutputCleaner));
    private const int DirectoryDeleteAttemptCount = 6;
    private static readonly TimeSpan DirectoryDeleteRetryDelay = TimeSpan.FromMilliseconds(150);

    public static void MarkPendingRootHidden(string? pendingOutputPath)
    {
        TryHideWorkspaceForPendingFile(pendingOutputPath);
    }

    public static void CleanupPendingRootIfEmpty(string? pendingOutputPath)
    {
        TryCleanupWorkspaceForPendingFile(pendingOutputPath);
    }

    public static void TryDeletePendingWorkspaceJobTree(string? pendingOutputPath, AppSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(pendingOutputPath))
        {
            return;
        }

        try
        {
            string? jobDirectory = Path.GetDirectoryName(Path.GetFullPath(pendingOutputPath));
            if (string.IsNullOrWhiteSpace(jobDirectory)
                || !AppPaths.IsKnownPendingWorkspaceJobDirectory(jobDirectory, settings))
            {
                return;
            }

            DeleteKnownPendingJobDirectoryTree(jobDirectory);
            CleanupWorkspaceParentsForJobDirectory(jobDirectory);
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Debug(ex, "Failed to delete pending workspace job directory. PendingOutputPath={PendingOutputPath}", pendingOutputPath);
        }
    }

    public static void TryHideWorkspaceForPendingFile(string? pendingOutputPath)
    {
        if (string.IsNullOrWhiteSpace(pendingOutputPath))
        {
            return;
        }

        try
        {
            string? jobDirectory = Path.GetDirectoryName(Path.GetFullPath(pendingOutputPath));
            if (string.IsNullOrWhiteSpace(jobDirectory))
            {
                return;
            }

            TryMarkHidden(jobDirectory);

            string? pendingRoot = Path.GetDirectoryName(jobDirectory);
            TryMarkHidden(pendingRoot);

            string? appRoot = string.IsNullOrWhiteSpace(pendingRoot) ? null : Path.GetDirectoryName(pendingRoot);
            if (!string.IsNullOrWhiteSpace(appRoot)
                && PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(Path.GetFileName(appRoot)))
            {
                TryMarkHidden(appRoot);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to hide pending workspace directories. PendingOutputPath={PendingOutputPath}", pendingOutputPath);
        }
    }

    public static void TryCleanupWorkspaceForPendingFile(string? pendingOutputPath)
    {
        if (string.IsNullOrWhiteSpace(pendingOutputPath))
        {
            return;
        }

        try
        {
            string? jobDirectory = Path.GetDirectoryName(Path.GetFullPath(pendingOutputPath));
            if (string.IsNullOrWhiteSpace(jobDirectory))
            {
                return;
            }

            DeleteDirectoryIfEmpty(jobDirectory);
            CleanupWorkspaceParentsForJobDirectory(jobDirectory);
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Debug(ex, "Failed to clean pending workspace directories. PendingOutputPath={PendingOutputPath}", pendingOutputPath);
        }
    }

    public static void TryCleanupLegacyOutputRootPending(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return;
        }

        try
        {
            string fullOutputRoot = Path.GetFullPath(outputRoot);
            DeleteDirectoryIfEmpty(Path.Combine(fullOutputRoot, PendingWorkspacePathPolicy.LegacyOutputPendingDirectoryName));
            DeleteDirectoryIfEmpty(Path.Combine(fullOutputRoot, PendingWorkspacePathPolicy.WorkspaceDirectoryName, PendingWorkspacePathPolicy.WorkspaceOperationsDirectoryName));
            DeleteDirectoryIfEmpty(Path.Combine(fullOutputRoot, PendingWorkspacePathPolicy.WorkspaceDirectoryName));
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Debug(ex, "Failed to clean legacy pending workspace. OutputRoot={OutputRoot}", outputRoot);
        }
    }

    private static void TryMarkHidden(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.Hidden) == 0)
            {
                File.SetAttributes(directory, attributes | FileAttributes.Hidden | FileAttributes.NotContentIndexed);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to mark pending workspace directory as hidden. Directory={Directory}", directory);
        }
    }

    private static void CleanupWorkspaceParentsForJobDirectory(string jobDirectory)
    {
        string? pendingRoot = Path.GetDirectoryName(jobDirectory);
        DeleteDirectoryIfEmpty(pendingRoot);

        string? appRoot = string.IsNullOrWhiteSpace(pendingRoot) ? null : Path.GetDirectoryName(pendingRoot);
        if (!string.IsNullOrWhiteSpace(appRoot)
            && PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(Path.GetFileName(appRoot)))
        {
            DeleteDirectoryIfEmpty(appRoot);
        }
    }

    private static void DeleteKnownPendingJobDirectoryTree(string jobDirectory)
    {
        if (!Directory.Exists(jobDirectory))
        {
            return;
        }

        if (ContainsReparsePoint(jobDirectory))
        {
            Logger.Warning(
                "Pending workspace cleanup skipped because the job directory contains a reparse point. Directory={Directory}",
                jobDirectory);
            return;
        }

        Exception? lastError = null;

        for (int attempt = 1; attempt <= DirectoryDeleteAttemptCount; attempt++)
        {
            try
            {
                if (Directory.Exists(jobDirectory))
                {
                    Directory.Delete(jobDirectory, recursive: true);
                }

                if (!Directory.Exists(jobDirectory))
                {
                    return;
                }
            }
            catch (Exception ex) when (IsExpectedCleanupException(ex))
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
            Logger.Debug(lastError, "Failed to delete pending workspace job directory after retries. Directory={Directory}", jobDirectory);
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
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            return true;
        }
    }

    private static bool IsExpectedCleanupException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

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
            Logger.Debug(ex, "Failed to delete pending workspace directory if empty. Directory={Directory}", directory);
        }
    }
}