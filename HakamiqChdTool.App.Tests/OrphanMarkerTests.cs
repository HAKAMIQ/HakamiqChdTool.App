using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // With a custom pending-workspace root, a user folder that happens to be named "Operation_*" looks like a job
    // directory by name alone. Only directories carrying the app's marker may be deleted as orphans.
    private static void TestOrphanCleanRequiresAppOwnershipMarker(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        string customRoot = Path.Combine(workDirectory, "custom-workspace-" + Guid.NewGuid().ToString("N"));
        string userFolder = Path.Combine(customRoot, "Operation_Overlord_notes");
        Directory.CreateDirectory(userFolder);
        string userFile = Path.Combine(userFolder, "plan.txt");
        File.WriteAllText(userFile, "user data");
        Directory.SetLastWriteTimeUtc(userFolder, DateTime.UtcNow.AddHours(-3));

        AppSettings settings = AppSettings.CreateSafeDefaults();
        settings.UseCustomPendingWorkspace = true;
        settings.PendingWorkspaceMode = PendingWorkspaceMode.Custom;
        settings.PendingWorkspaceCustomRoot = customRoot;

        AssertTrue(AppPaths.IsKnownPendingWorkspaceJobDirectory(userFolder, settings),
            "Precondition: by name and location alone the user folder looks like a job directory.");
        AssertFalse(PendingWorkspacePathPolicy.HasJobMarker(userFolder), "The user folder has no ownership marker.");

        try
        {
            var item = new OrphanedWorkItem(userFolder, OrphanedWorkItemKind.PendingDirectory, 9, 1,
                new DateTimeOffset(Directory.GetLastWriteTimeUtc(userFolder), TimeSpan.Zero));

            using IDisposable self = ActiveWorkflowRegistry.Enter();
            new OrphanedWorkItemCleanupService(settings) { OwnActiveWorkflows = baseline + 1 }
                .Clean(new OrphanedWorkItemScanResult([item]));
            AssertTrue(File.Exists(userFile), "An unmarked folder is never deleted as an orphan.");

            // The same folder once the app has claimed it with its marker is deletable.
            PendingWorkspacePathPolicy.WriteJobMarker(userFolder);
            Directory.SetLastWriteTimeUtc(userFolder, DateTime.UtcNow.AddHours(-3));
            var marked = new OrphanedWorkItem(userFolder, OrphanedWorkItemKind.PendingDirectory, 9, 2,
                new DateTimeOffset(Directory.GetLastWriteTimeUtc(userFolder), TimeSpan.Zero));
            new OrphanedWorkItemCleanupService(settings) { OwnActiveWorkflows = baseline + 1 }
                .Clean(new OrphanedWorkItemScanResult([marked]));
            AssertFalse(Directory.Exists(userFolder), "A marked job directory is cleaned.");
        }
        finally
        {
            try { Directory.Delete(customRoot, recursive: true); } catch (IOException) { }
        }
    }

    // New job directories are created with the marker, so the app's own workspaces stay eligible.
    private static void TestPendingJobDirectoryIsCreatedWithMarker(string workDirectory)
    {
        string outputRoot = Path.Combine(workDirectory, "marker-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        AppSettings settings = AppSettings.CreateSafeDefaults();

        string pending = WorkflowOutputPathContract.BuildPendingOutputPath(
            Path.Combine(outputRoot, "Game.chd"), Path.Combine(outputRoot, "Game.iso"), ".chd", outputRoot, settings);

        string jobDirectory = Path.GetDirectoryName(pending)!;
        AssertTrue(PendingWorkspacePathPolicy.HasJobMarker(jobDirectory), "The job directory carries the ownership marker.");

        Directory.Delete(outputRoot, recursive: true);
    }

    // A marker proves ownership of the directory it was written into, not of any folder that carries a copy of it.
    private static void TestJobMarkerIsBoundToItsDirectory(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "marker-bind-" + Guid.NewGuid().ToString("N"));
        string original = Path.Combine(root, "Operation_a1");
        string copy = Path.Combine(root, "Operation_copy");
        string forged = Path.Combine(root, "Operation_forged");
        string linked = Path.Combine(root, "Operation_linked");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(copy);
        Directory.CreateDirectory(forged);
        Directory.CreateDirectory(linked);

        try
        {
            PendingWorkspacePathPolicy.WriteJobMarker(original);
            AssertTrue(PendingWorkspacePathPolicy.HasJobMarker(original), "The marker validates in its own directory.");

            string markerName = PendingWorkspacePathPolicy.JobMarkerFileName;
            File.Copy(Path.Combine(original, markerName), Path.Combine(copy, markerName));
            AssertFalse(PendingWorkspacePathPolicy.HasJobMarker(copy), "A marker copied into another folder does not qualify it.");

            File.WriteAllText(Path.Combine(forged, markerName), "HakamiqChdTool job workspace");
            AssertFalse(PendingWorkspacePathPolicy.HasJobMarker(forged), "The old constant-only marker is refused.");

            File.WriteAllText(Path.Combine(linked, markerName), new string('x', 4096));
            AssertFalse(PendingWorkspacePathPolicy.HasJobMarker(linked), "An oversized marker is refused.");

            Directory.Move(original, Path.Combine(root, "Operation_renamed"));
            AssertFalse(PendingWorkspacePathPolicy.HasJobMarker(Path.Combine(root, "Operation_renamed")), "A renamed directory loses its marker.");
            AssertFalse(PendingWorkspacePathPolicy.HasJobMarker(Path.Combine(root, "Operation_missing")), "A missing directory has no marker.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
