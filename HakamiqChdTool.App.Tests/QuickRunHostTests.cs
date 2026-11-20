using System.IO;
using System.Windows.Threading;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.QueueRun;
using HakamiqChdTool.App.Services.PostProcessing;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The quick window host runs Explorer files through the normal intake, coordinator and queue: it starts on its
    // own, waits for Start when asked, runs files that arrive during a run afterwards, and reports a stopped run.
    private static void TestQuickRunHostRunsWaitsAndCancels(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "quick-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string MakeIso(string name)
        {
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, new byte[64 * 1024]);
            return path;
        }

        string a = MakeIso("Game A.iso");
        string b = MakeIso("Game B.iso");
        string c = MakeIso("Game C.iso");
        string d = MakeIso("Game D.iso");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await QuickHostAutoStartAndLateFilesAsync(dispatcher, a, b, c);
                    await QuickHostWaitsForStartAndCancelsAsync(dispatcher, d);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(120)), "The quick host test thread finished.");
        if (failure is not null)
        {
            throw new InvalidOperationException("Quick host scenario failed: " + failure, failure);
        }
    }

    // Like the real workflow, it reports the result through the row sink. Each item waits until released.
    private sealed class QuickTestOrchestrator : IChdWorkflowOrchestrator
    {
        private readonly SemaphoreSlim _release = new(0);
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public void ReleaseOne() => _release.Release();

        public async Task<WorkflowExecutionResult> ProcessAsync(ChdTaskRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _started);
            await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
            var context = (ChdWorkflowTaskContext)request.Options!;
            context.Sink.ReportTerminalSuccess(QueueItemTerminalOutcome.Healthy, "ok");
            return WorkflowExecutionResult.Success(QueueItemTerminalOutcome.Healthy, "ok", request.InputPath);
        }
    }

    private static QuickRunHost CreateQuickHost(Dispatcher dispatcher, QuickTestOrchestrator orchestrator, bool autoStart)
    {
        var queue = new QueueManager(orchestrator, AppSettings.CreateSafeDefaults, static () => string.Empty, 1);
        return new QuickRunHost(queue, AppSettings.CreateSafeDefaults, new PostConversionArtifactService(), dispatcher, static () => null, autoStart);
    }

    private static async Task<bool> WaitOnDispatcherAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private static async Task QuickHostAutoStartAndLateFilesAsync(Dispatcher dispatcher, string a, string b, string c)
    {
        var orchestrator = new QuickTestOrchestrator();
        await using QuickRunHost host = CreateQuickHost(dispatcher, orchestrator, autoStart: true);
        var results = new List<QuickRunResult>();
        host.RunCompleted += results.Add;

        Task first = host.AddAsync(QueueExecutionProfile.QuickConvert, [a, b]);
        AssertTrue(await WaitOnDispatcherAsync(() => orchestrator.Started == 1, TimeSpan.FromSeconds(30)), "The quick window starts on its own.");
        AssertTrue(host.IsProcessing, "A run is in progress.");

        // A file chosen in Explorer during the run waits and runs after it.
        Task late = host.AddAsync(QueueExecutionProfile.QuickConvert, [c]);
        AssertEqual(2, host.Items.Count, "The late file is not added while the run is in progress.");

        for (int i = 0; i < 3; i++)
        {
            orchestrator.ReleaseOne();
        }

        AssertTrue(await WaitOnDispatcherAsync(() => first.IsCompleted, TimeSpan.FromSeconds(60)),
            $"The runs finish (started {orchestrator.Started}, results {results.Count}); a finished file is never run again.");
        await Task.WhenAll(first, late).WaitAsync(TimeSpan.FromSeconds(60));
        AssertTrue(await WaitOnDispatcherAsync(() => results.Count == 2 && !host.IsBusy, TimeSpan.FromSeconds(30)), "Both runs finished.");
        AssertEqual(3, orchestrator.Started, "Every file ran once.");
        AssertEqual(3, host.Items.Count, "The window lists every file.");
        AssertEqual(2, results[0].Metrics.Completed, "The first run converted the two selected files.");
        AssertEqual(3, results[1].Metrics.Completed, "The result after the late file covers every file in the window.");
        AssertFalse(results[1].WasCancelled, "Nothing was stopped.");
        AssertTrue(results[1].Card.Title.Length > 0 && results[1].Report.Length > 0, "The result has a title line and a report.");
        AssertEqual(0, host.UnfinishedSourcePaths().Count, "Nothing is left to open in the main window.");
    }

    private static async Task QuickHostWaitsForStartAndCancelsAsync(Dispatcher dispatcher, string d)
    {
        var orchestrator = new QuickTestOrchestrator();
        await using QuickRunHost host = CreateQuickHost(dispatcher, orchestrator, autoStart: false);
        var results = new List<QuickRunResult>();
        host.RunCompleted += results.Add;

        await host.AddAsync(QueueExecutionProfile.QuickConvert, [d]).WaitAsync(TimeSpan.FromSeconds(30));
        AssertEqual(1, host.Items.Count, "The file is listed.");
        AssertEqual(0, orchestrator.Started, "Nothing starts while the window waits for Start.");
        AssertTrue(host.HasWaitingFiles, "The file waits for Start.");

        Task run = host.StartAsync();
        AssertTrue(await WaitOnDispatcherAsync(() => orchestrator.Started == 1, TimeSpan.FromSeconds(30)), "Start runs the waiting file.");
        host.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(60));

        AssertTrue(await WaitOnDispatcherAsync(() => results.Count == 1, TimeSpan.FromSeconds(30)), "The stopped run reports a result.");
        AssertTrue(results[0].WasCancelled, "The result says the run was stopped.");
        AssertEqual(0, results[0].Metrics.Completed, "Nothing completed.");
        AssertTrue(host.UnfinishedSourcePaths().Contains(d, StringComparer.OrdinalIgnoreCase), "The stopped file can be opened in the main window.");
    }
}
