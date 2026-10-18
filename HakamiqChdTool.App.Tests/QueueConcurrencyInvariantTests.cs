using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed class GatedCountingOrchestrator : IChdWorkflowOrchestrator
    {
        private readonly SemaphoreSlim _release = new(0);
        private readonly HashSet<string> _failing;
        private int _active;
        private int _maxActive;
        private int _started;

        public GatedCountingOrchestrator(IEnumerable<string>? failingInputs = null) =>
            _failing = new HashSet<string>(failingInputs ?? [], StringComparer.OrdinalIgnoreCase);

        public int MaxActive => Volatile.Read(ref _maxActive);

        public int Active => Volatile.Read(ref _active);

        public int Started => Volatile.Read(ref _started);

        public void ReleaseOne() => _release.Release();

        public async Task<WorkflowExecutionResult> ProcessAsync(ChdTaskRequest request, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _started);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxActive))
                   && Interlocked.CompareExchange(ref _maxActive, now, seen) != seen)
            {
            }

            try
            {
                await _release.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (_failing.Contains(request.InputPath))
                {
                    throw new InvalidOperationException("synthetic failure");
                }

                return WorkflowExecutionResult.Success(QueueItemTerminalOutcome.Healthy, "ok", request.InputPath);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        // Event-free polling of an observable condition with a hard bound; used only to await a state, never to assert absence.
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Yield();
        }

        return condition();
    }

    private static void TestQueueNeverExceedsConfiguredConcurrency(string workDirectory)
    {
        foreach (int limit in new[] { 1, 2, 3 })
        {
            RunConcurrencyScenario(workDirectory, limit, cancelOneRunning: false, failingIndex: -1);
        }

        RunConcurrencyScenario(workDirectory, 2, cancelOneRunning: true, failingIndex: -1);
        RunConcurrencyScenario(workDirectory, 2, cancelOneRunning: false, failingIndex: 1);
    }

    private static void RunConcurrencyScenario(string workDirectory, int limit, bool cancelOneRunning, int failingIndex)
    {
        string label = $"limit={limit} cancel={cancelOneRunning} fail={failingIndex}";
        int itemCount = limit * 3 + 1;
        string[] inputs = Enumerable.Range(0, itemCount)
            .Select(i => Path.Combine(workDirectory, "conc-" + limit + "-" + i + ".iso"))
            .ToArray();

        var orchestrator = new GatedCountingOrchestrator(failingIndex >= 0 ? [inputs[failingIndex]] : null);
        var queue = new QueueManager(orchestrator, AppSettings.CreateSafeDefaults, static () => string.Empty, limit);
        var snapshots = new ConcurrentDictionary<Guid, QueueItemSnapshot>();
        var items = new List<ChdQueueItem>();

        try
        {
            queue.ConfigureUiBindings(
                id => snapshots.TryGetValue(id, out QueueItemSnapshot? s) ? s : null,
                _ => NoOpQueueItemStateSink.Instance,
                static () => { });

            foreach (string input in inputs)
            {
                var item = new ChdQueueItem { InputPath = input };
                snapshots[item.Id] = new QueueItemSnapshot
                {
                    ItemId = item.Id,
                    OriginalPath = input,
                    SourcePath = input,
                    FileName = Path.GetFileName(input),
                    DetectedPlatform = "Test",
                    RequestedAction = "Convert"
                };
                items.Add(item);
                AssertEqual(QueueEnqueueResult.Accepted, queue.Enqueue(item), label + ": enqueue accepted.");
            }

            queue.Start();
            AssertTrue(WaitFor(() => orchestrator.Active == limit, TimeSpan.FromSeconds(20)), label + ": the queue saturates at the limit.");
            AssertEqual(limit, orchestrator.Started, label + ": no item beyond the limit has started while saturated.");

            if (cancelOneRunning)
            {
                ChdQueueItem running = items.First(i => string.Equals(i.Status, QueueConstants.StatusRunning, StringComparison.OrdinalIgnoreCase));
                queue.Cancel(running.Id);
                AssertTrue(WaitFor(() => orchestrator.Started > limit, TimeSpan.FromSeconds(20)), label + ": cancelling one frees exactly one slot.");
                AssertTrue(orchestrator.MaxActive <= limit, label + ": cancellation never admits a replacement before the slot is free.");
            }

            for (int i = 0; i < itemCount; i++)
            {
                orchestrator.ReleaseOne();
            }

            AssertTrue(
                WaitFor(() => items.All(i => IsTerminalQueueStatus(i.Status)), TimeSpan.FromSeconds(30)),
                label + ": every item reaches a terminal state.");
            AssertTrue(orchestrator.MaxActive <= limit, label + $": observed {orchestrator.MaxActive} concurrent, limit {limit}.");
            AssertEqual(0, orchestrator.Active, label + ": nothing remains active.");
        }
        finally
        {
            queue.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(30));
        }
    }

    private static bool IsTerminalQueueStatus(string status) =>
        !string.Equals(status, QueueConstants.StatusPending, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(status, QueueConstants.StatusRunning, StringComparison.OrdinalIgnoreCase);
}
