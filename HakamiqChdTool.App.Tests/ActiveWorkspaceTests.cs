using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Automatic orphan cleanup runs with minimum age zero. While any sibling workflow is active in this
    // process, the scan must report nothing, so a live job's workspace is never offered for deletion.
    private static void TestOrphanScanSkipsWhileSiblingWorkflowActive(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        string workspace = AppPaths.CombineProcessTemp("TempExtraction", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        string payload = Path.Combine(workspace, "live.bin");
        File.WriteAllBytes(payload, new byte[64]);

        try
        {
            using IDisposable self = ActiveWorkflowRegistry.Enter();
            using IDisposable sibling = ActiveWorkflowRegistry.Enter();
            AssertEqual(baseline + 2, ActiveWorkflowRegistry.ActiveCount, "Both workflows are registered.");

            OrphanedWorkItemScanner scanner = new(settings: null, TimeSpan.Zero) { OwnActiveWorkflows = baseline + 1 };
            AssertFalse(scanner.Scan().HasItems, "A sibling workflow is active, so nothing is offered for cleanup.");

            // More than two active jobs.
            using IDisposable third = ActiveWorkflowRegistry.Enter();
            AssertFalse(scanner.Scan().HasItems, "Three active workflows still block the scan.");
            AssertTrue(File.Exists(payload), "Scanning never deletes.");
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }

        AssertEqual(baseline, ActiveWorkflowRegistry.ActiveCount, "All leases were released.");
    }

    // Lease release is idempotent: a double completion cannot release another workflow's registration.
    private static void TestActiveWorkflowLeaseReleasesOnce(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        IDisposable first = ActiveWorkflowRegistry.Enter();
        using IDisposable second = ActiveWorkflowRegistry.Enter();

        first.Dispose();
        first.Dispose();

        AssertEqual(baseline + 1, ActiveWorkflowRegistry.ActiveCount, "A double dispose releases only its own registration.");
    }

    // Overlapping enter/leave from many threads, forced to start together, never leaks or underflows.
    private static void TestActiveWorkflowRegistryUnderContention(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        const int workers = 16;
        using Barrier start = new(workers);
        using Barrier allEntered = new(workers);
        int observedMax = 0;

        Task[] tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            using IDisposable lease = ActiveWorkflowRegistry.Enter();
            allEntered.SignalAndWait();
            int seen = ActiveWorkflowRegistry.ActiveCount;
            int current;
            while (seen > (current = Volatile.Read(ref observedMax)) && Interlocked.CompareExchange(ref observedMax, seen, current) != current)
            {
            }
        })).ToArray();

        Task.WaitAll(tasks);

        AssertEqual(baseline + workers, observedMax, "Every concurrent workflow was visible while all were active.");
        AssertEqual(baseline, ActiveWorkflowRegistry.ActiveCount, "Counter returns to baseline.");
    }

    // A sibling that registers after the scan but before deletion must stop the delete (scan/delete window).
    // Unknown ownership (any extra active workflow) fails closed.
    private static void TestOrphanCleanSkipsWhenSiblingStartsAfterScan(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        string workspace = AppPaths.CombineProcessTemp("TempExtraction", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        string payload = Path.Combine(workspace, "late.bin");
        File.WriteAllBytes(payload, new byte[64]);

        try
        {
            OrphanedWorkItemScanResult scan;
            using (IDisposable self = ActiveWorkflowRegistry.Enter())
            {
                // Built by hand: the real scan also refuses while unrelated dotnet/7z processes run on the machine.
                scan = new OrphanedWorkItemScanResult(
                    [new OrphanedWorkItem(workspace, OrphanedWorkItemKind.Directory, 64, 1, DateTimeOffset.UtcNow.AddHours(-1))]);
                OrphanedWorkItemScanResult probe = new OrphanedWorkItemScanner(settings: null, TimeSpan.Zero) { OwnActiveWorkflows = baseline + 1 }.Scan();
                AssertTrue(probe.Items.All(item => item.Path != workspace) || probe.HasItems, "Scan stays consistent.");

                using IDisposable sibling = ActiveWorkflowRegistry.Enter();
                CleanupStats stats = new OrphanedWorkItemCleanupService { OwnActiveWorkflows = baseline + 1 }.Clean(scan);
                AssertEqual(0L, stats.DeletedBytes, "No bytes deleted after a sibling became active.");
                AssertTrue(File.Exists(payload), "The workspace survives.");
            }
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }
    }
}
