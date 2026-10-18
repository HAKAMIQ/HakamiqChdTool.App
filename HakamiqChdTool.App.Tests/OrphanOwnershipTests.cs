using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static string NewOrphanWorkspace(string name, out string payload)
    {
        string dir = AppPaths.CombineProcessTemp("TempExtraction", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        payload = Path.Combine(dir, "data.bin");
        File.WriteAllBytes(payload, new byte[128]);
        // The scan records the directory time; make it old so "changed since the scan" is unambiguous.
        Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddHours(-2));
        return dir;
    }

    private static OrphanedWorkItem OrphanItemFor(string dir) =>
        new(dir, OrphanedWorkItemKind.Directory, 128, 1, new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero));

    // Deterministic interleaving: a sibling registers after the first item was deleted and before the second is
    // authorized. The first deletion stands; the second and third must survive.
    private static void TestOrphanCleanAuthorizesEachItemAgainstTheRegistry(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        string a = NewOrphanWorkspace("own-a", out string pa);
        string b = NewOrphanWorkspace("own-b", out string pb);
        string c = NewOrphanWorkspace("own-c", out string pc);
        IDisposable? sibling = null;

        try
        {
            using IDisposable self = ActiveWorkflowRegistry.Enter();
            var scan = new OrphanedWorkItemScanResult([OrphanItemFor(a), OrphanItemFor(b), OrphanItemFor(c)]);
            int seen = 0;

            var service = new OrphanedWorkItemCleanupService
            {
                OwnActiveWorkflows = baseline + 1,
                BeforeItemAuthorization = _ =>
                {
                    if (++seen == 2)
                    {
                        sibling = ActiveWorkflowRegistry.Enter();
                    }
                }
            };

            service.Clean(scan);

            AssertFalse(File.Exists(pa), "The item authorized before the sibling registered was deleted.");
            AssertTrue(File.Exists(pb), "The item authorized after the sibling registered survives.");
            AssertTrue(File.Exists(pc), "Later items survive too.");
        }
        finally
        {
            sibling?.Dispose();
            foreach (string dir in new[] { a, b, c })
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }

        AssertEqual(baseline, ActiveWorkflowRegistry.ActiveCount, "All leases were released.");
    }

    // A listed directory replaced by a junction (to a directory outside the workspace root) must not be followed.
    private static void TestOrphanCleanDoesNotFollowReplacedDirectory(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        string dir = NewOrphanWorkspace("replaced", out _);
        OrphanedWorkItem item = OrphanItemFor(dir);

        string outside = Path.Combine(workDirectory, "orphan-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "precious.bin");
        File.WriteAllBytes(victim, [1, 2, 3]);

        try
        {
            Directory.Delete(dir, recursive: true);
            CreateJunction(dir, outside);

            using IDisposable self = ActiveWorkflowRegistry.Enter();
            new OrphanedWorkItemCleanupService { OwnActiveWorkflows = baseline + 1 }
                .Clean(new OrphanedWorkItemScanResult([item]));

            AssertTrue(File.Exists(victim), "The junction target is untouched.");
        }
        finally
        {
            try { Directory.Delete(dir); } catch (IOException) { }
            Directory.Delete(outside, recursive: true);
        }
    }

    // A listed directory that was written to after the scan is in use again: leave it.
    private static void TestOrphanCleanSkipsItemsChangedAfterScan(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        string dir = NewOrphanWorkspace("changed", out string payload);
        OrphanedWorkItem item = OrphanItemFor(dir);

        try
        {
            File.WriteAllBytes(Path.Combine(dir, "new-activity.bin"), [1]); // bumps the directory time to now

            using IDisposable self = ActiveWorkflowRegistry.Enter();
            new OrphanedWorkItemCleanupService { OwnActiveWorkflows = baseline + 1 }
                .Clean(new OrphanedWorkItemScanResult([item]));

            AssertTrue(File.Exists(payload), "A directory that changed after the scan is not deleted.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class BlockingHandler(SemaphoreSlim entered) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            entered.Release();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // The Redump sync keeps a work directory under the scanned temp root and is not a queue workflow, so it must
    // hold a lease while it runs, and release it on cancellation.
    private static void TestRedumpSyncHoldsAnActiveLeaseUntilCancelled(string workDirectory)
    {
        int baseline = ActiveWorkflowRegistry.ActiveCount;
        using var entered = new SemaphoreSlim(0);
        using var cts = new CancellationTokenSource();
        using var manager = new RedumpGitHubSyncManager(new BlockingHandler(entered));

        Task run = manager.SyncFromGitHubAsync(null, null, cts.Token);
        AssertTrue(entered.Wait(TimeSpan.FromSeconds(10)), "The download started.");
        AssertEqual(baseline + 1, ActiveWorkflowRegistry.ActiveCount, "The running sync holds an active lease.");

        cts.Cancel();
        try { run.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }

        AssertEqual(baseline, ActiveWorkflowRegistry.ActiveCount, "Cancellation releases the lease.");
    }
}
