using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void TryCleanupPendingWorkspacesAfterQueueShutdown()
    {
        try
        {
            string[] outputRoots = CollectKnownOutputRootsForPendingWorkspaceCleanup();
            if (outputRoots.Length == 0)
            {
                return;
            }

            string[] pendingRoots = BuildPendingWorkspaceRoots(outputRoots);
            if (pendingRoots.Length > 0)
            {
                OrphanedWorkItemScanResult scanResult = _orphanedScanner.Scan(
                    TimeSpan.Zero,
                    pendingRoots);

                if (scanResult.HasItems)
                {
                    _orphanedCleanup.Clean(scanResult);
                }
            }

            foreach (string outputRoot in outputRoots)
            {
                WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
            }
        }
        catch (Exception ex) when (IsExpectedPendingWorkspaceShutdownCleanupException(ex))
        {
            Log.Debug(ex, "Pending workspace shutdown cleanup was skipped after a non-fatal error.");
        }
    }

    private string[] CollectKnownOutputRootsForPendingWorkspaceCleanup()
    {
        var outputRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _queueRowStore.GetRowsSnapshot())
        {
            string originalPath = row.OriginalPath;
            string sourcePath = string.IsNullOrWhiteSpace(row.SourcePath)
                ? row.OriginalPath
                : row.SourcePath;

            TryAddOutputRootForPendingWorkspaceCleanup(
                outputRoots,
                originalPath,
                sourcePath);

            if (!string.Equals(originalPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                TryAddOutputRootForPendingWorkspaceCleanup(
                    outputRoots,
                    originalPath,
                    originalPath);
            }
        }

        return [.. outputRoots];
    }

    private void TryAddOutputRootForPendingWorkspaceCleanup(
        HashSet<string> outputRoots,
        string originalPath,
        string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(originalPath) ||
            string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        try
        {
            string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(
                originalPath,
                sourcePath,
                _settings);

            if (!string.IsNullOrWhiteSpace(outputRoot))
            {
                outputRoots.Add(Path.GetFullPath(outputRoot));
            }
        }
        catch (Exception ex) when (IsExpectedPendingWorkspaceShutdownCleanupException(ex))
        {
            Log.Debug(
                ex,
                "Failed to resolve output root for pending workspace shutdown cleanup. OriginalPath={OriginalPath}; SourcePath={SourcePath}",
                originalPath,
                sourcePath);
        }
    }

    private string[] BuildPendingWorkspaceRoots(IEnumerable<string> outputRoots)
    {
        var pendingRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string outputRoot in outputRoots)
        {
            try
            {
                string pendingRoot = PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(
                    outputRoot,
                    _settings);

                if (Directory.Exists(pendingRoot))
                {
                    pendingRoots.Add(Path.GetFullPath(pendingRoot));
                }
            }
            catch (Exception ex) when (IsExpectedPendingWorkspaceShutdownCleanupException(ex))
            {
                Log.Debug(
                    ex,
                    "Failed to resolve pending workspace root for shutdown cleanup. OutputRoot={OutputRoot}",
                    outputRoot);
            }
        }

        return [.. pendingRoots];
    }

    private static bool IsExpectedPendingWorkspaceShutdownCleanupException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or PathTooLongException
            or SecurityException;
    }
}