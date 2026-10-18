using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed class UnusedOrchestrator : IChdWorkflowOrchestrator
    {
        public Task<WorkflowExecutionResult> ProcessAsync(ChdTaskRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The orchestrator must not run in a store-only test.");
    }

    // An item can be completed twice (cancel/stop completes a pending item, then its cancelled
    // worker completes it again). The second completion must not release the input-path
    // reservation of a newer item that was admitted in between.
    private static void TestQueueCompletionDoesNotReleaseNewerPathOwner()
    {
        var state = new QueueRuntimeState(new UnusedOrchestrator(), AppSettings.CreateSafeDefaults, static () => string.Empty, 1);
        var store = new QueueStateStore(state, new QueueNotificationPublisher());
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "queue-ownership", "disc.iso");

        var first = new ChdQueueItem { InputPath = path };
        AssertEqual(QueueEnqueueResult.Accepted, store.Enqueue(first), "First item is accepted.");
        store.Cancel(first.Id);

        var second = new ChdQueueItem { InputPath = path };
        AssertEqual(QueueEnqueueResult.Accepted, store.Enqueue(second), "Path is free after the first item completed.");

        store.CompleteScheduledItem(first); // late duplicate completion from the cancelled worker

        var third = new ChdQueueItem { InputPath = path };
        AssertEqual(
            QueueEnqueueResult.RejectedDuplicatePath,
            store.Enqueue(third),
            "A late completion of the first item must not release the second item's path.");

        store.CompleteScheduledItem(second);
        var fourth = new ChdQueueItem { InputPath = path };
        AssertEqual(QueueEnqueueResult.Accepted, store.Enqueue(fourth), "Completing the owner releases the path.");
    }
}
