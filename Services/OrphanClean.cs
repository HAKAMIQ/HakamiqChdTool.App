using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

internal sealed class OrphanedWorkItemCleanupService
{
    private static readonly ILogger Logger = Log.ForContext<OrphanedWorkItemCleanupService>();

    private readonly AppSettings? _settings;
    private readonly CleanupService _cleanup = new();

    public OrphanedWorkItemCleanupService()
        : this(null)
    {
    }

    public OrphanedWorkItemCleanupService(AppSettings? settings)
    {
        _settings = settings;
    }

    /// <summary>Workflows of the calling job; any additional active workflow blocks deletion (re-checked at delete time).</summary>
    public int OwnActiveWorkflows { get; init; }

    /// <summary>Test seam: runs before each item is authorized, after the previous one was deleted.</summary>
    internal Action<OrphanedWorkItem>? BeforeItemAuthorization { get; init; }

    public CleanupStats Clean(OrphanedWorkItemScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        if (ActiveWorkflowRegistry.ActiveCount > OwnActiveWorkflows)
        {
            Logger.Debug("Orphaned work item cleanup skipped because another workflow became active after the scan.");
            return CleanupStats.Empty;
        }

        CleanupStats total = CleanupStats.Empty;

        foreach (OrphanedWorkItem item in scanResult.Items)
        {
            BeforeItemAuthorization?.Invoke(item);

            // Authorization is per item, immediately before its deletion. A workflow that became active since the
            // scan, or an item that changed since the scan (replaced or written to), is left alone.
            if (ActiveWorkflowRegistry.ActiveCount > OwnActiveWorkflows)
            {
                Logger.Debug("Orphaned work item cleanup stopped because another workflow became active.");
                break;
            }

            if (ChangedSinceScan(item))
            {
                Logger.Debug("Skipped orphaned work item that changed after the scan. Path={Path}", item.Path);
                continue;
            }

            bool isProcessTempItem =
                item.Kind is OrphanedWorkItemKind.Directory or OrphanedWorkItemKind.File
                && AppPaths.IsPathUnderProcessTempRoot(item.Path);

            bool isPendingWorkspaceItem =
                item.Kind == OrphanedWorkItemKind.PendingDirectory
                && AppPaths.IsKnownPendingWorkspaceJobDirectory(item.Path, _settings)
                && PendingWorkspacePathPolicy.HasJobMarker(item.Path);

            if (!isProcessTempItem && !isPendingWorkspaceItem)
            {
                Logger.Warning("Skipped orphaned work item cleanup outside approved cleanup roots. Path={Path}", item.Path);
                continue;
            }

            CleanupStats itemStats = item.Kind switch
            {
                OrphanedWorkItemKind.Directory => _cleanup.DeleteDirectoryTree(item.Path),
                OrphanedWorkItemKind.File => _cleanup.DeleteFiles(item.Path),
                OrphanedWorkItemKind.PendingDirectory => _cleanup.DeletePendingWorkspaceDirectoryTree(item.Path, _settings),
                _ => CleanupStats.Empty
            };

            total += itemStats;
        }

        Logger.Information(
            "Orphaned work item cleanup completed. Bytes={Bytes}, Files={Files}",
            total.DeletedBytes,
            total.DeletedFiles);

        return total;
    }

    private static bool ChangedSinceScan(OrphanedWorkItem item)
    {
        try
        {
            DateTime lastWriteUtc = item.Kind == OrphanedWorkItemKind.File
                ? File.GetLastWriteTimeUtc(item.Path)
                : Directory.GetLastWriteTimeUtc(item.Path);

            // A missing path reads as year 1601; nothing left to delete is not "changed".
            return lastWriteUtc.Year > 1700 && lastWriteUtc > item.LastWriteUtc.UtcDateTime.AddSeconds(2);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }
}
