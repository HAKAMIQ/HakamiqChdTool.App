using HakamiqChdTool.App.Models;
using Serilog;
using System;

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

    public CleanupStats Clean(OrphanedWorkItemScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        CleanupStats total = CleanupStats.Empty;

        foreach (OrphanedWorkItem item in scanResult.Items)
        {
            bool isProcessTempItem =
                item.Kind is OrphanedWorkItemKind.Directory or OrphanedWorkItemKind.File
                && AppPaths.IsPathUnderProcessTempRoot(item.Path);

            bool isPendingWorkspaceItem =
                item.Kind == OrphanedWorkItemKind.PendingDirectory
                && AppPaths.IsKnownPendingWorkspaceJobDirectory(item.Path, _settings);

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
}
