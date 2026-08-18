using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Configuration;
using HakamiqChdTool.App.Startup;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static int RunWpfShutdownCampaign(
        string[] args,
        string appDirectory,
        string outputRoot)
    {
        int queueLength = ReadBoundedInt(args, "--queue-length", 32, 8, 1_000);
        int concurrency = ReadBoundedInt(args, "--concurrency", 4, 1, AppSettings.MaxConcurrentConversionsUpperBound);
        int timeoutSeconds = ReadBoundedInt(args, "--timeout-seconds", 45, 10, 180);
        string campaignRoot = Path.Combine(outputRoot, "wpf-shutdown");
        Directory.CreateDirectory(campaignRoot);
        HashSet<int> baselineCsoKitProcessIds = GetProcessIdsByName("csokit");

        var resultSource = new TaskCompletionSource<WpfShutdownResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var testThread = new Thread(
            () => ExecuteWpfShutdownScenario(
                appDirectory,
                campaignRoot,
                queueLength,
                concurrency,
                resultSource))
        {
            IsBackground = true,
            Name = "Hakamiq-WPF-Shutdown-Security-Test"
        };
        testThread.SetApartmentState(ApartmentState.STA);
        testThread.Start();

        if (!resultSource.Task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException(
                $"The full WPF shutdown scenario did not complete within {timeoutSeconds} seconds.");
        }

        WpfShutdownResult result = resultSource.Task.GetAwaiter().GetResult();
        if (result.Exception is not null)
        {
            throw new InvalidOperationException("The full WPF shutdown scenario failed.", result.Exception);
        }

        Thread.Sleep(500);
        int leakedCsoKitProcesses = GetProcessIdsByName("csokit")
            .Count(id => !baselineCsoKitProcessIds.Contains(id));

        if (!result.WindowClosed
            || result.StartedOperations < concurrency
            || result.CancelledOperations < concurrency
            || result.ActiveOperationsAfterClose != 0
            || leakedCsoKitProcesses != 0)
        {
            throw new InvalidOperationException(
                $"WPF shutdown did not quiesce real compression. Closed={result.WindowClosed}; Active={result.ActiveOperationsAfterClose}; LeakedCsoKit={leakedCsoKitProcesses}.");
        }

        string reportPath = Path.Combine(campaignRoot, "wpf-shutdown-report.json");
        WriteJsonAtomically(
            reportPath,
            new
            {
                format = "HakamiqWpfShutdownReport.v1",
                completedAtUtc = DateTimeOffset.UtcNow,
                queueLength,
                concurrency,
                result.WindowClosed,
                result.StartedOperations,
                result.CancelledOperations,
                result.ActiveOperationsAfterClose,
                leakedCsoKitProcesses,
                result.ElapsedMilliseconds,
                compressionTool = "Bundled CsoKit",
                realCompression = true,
                dispatcherException = (string?)null
            });

        Console.WriteLine(
            $"[PASS] Full WPF busy-queue shutdown completed: started={result.StartedOperations}; cancelled={result.CancelledOperations}; elapsedMs={result.ElapsedMilliseconds}; report={reportPath}");
        return 0;
    }

    private static void ExecuteWpfShutdownScenario(
        string appDirectory,
        string campaignRoot,
        int queueLength,
        int concurrency,
        TaskCompletionSource<WpfShutdownResult> resultSource)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Exception? failure = null;
        bool windowClosed = false;
        string csoKitPath = Path.Combine(appDirectory, "Tools", "hakamiq-cso", "win-x64", "csokit.exe");
        string compressionInputPath = Path.Combine(campaignRoot, "wpf-real-compression-input.iso");
        CreateRealCompressionInput(compressionInputPath, 8 * 1024 * 1024);
        var orchestrator = new RealCompressionSecurityOrchestrator(
            concurrency,
            csoKitPath,
            compressionInputPath,
            campaignRoot);
        string settingsPath = Path.Combine(campaignRoot, "wpf-test-settings.json");

        try
        {
            var application = new global::HakamiqChdTool.App.App
            {
                IsIntegrationTestHost = true,
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.InitializeComponent();
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                failure = eventArgs.Exception;
                eventArgs.Handled = true;
                application.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };

            using var settingsService = new AppSettingsService(settingsPath);
            AppSettings settings = AppSettings.CreateSafeDefaults();
            settings.EnableRedumpAutoSync = false;
            settings.MaxConcurrentConversions = concurrency;

            MainWindowBootstrap bootstrap = MainWindowBootstrap.CreateDefault(
                settingsService,
                settings,
                AppMetadata.CreateDefault(),
                RuntimeToolService.Instance);
            string chdmanPath = Path.Combine(appDirectory, "Tools", "chdman.exe");
            var queue = new QueueManager(
                orchestrator,
                () => settings,
                () => chdmanPath,
                maxConcurrentItems: concurrency);
            var window = new global::HakamiqChdTool.App.MainWindow(bootstrap, queue)
            {
                ShowInTaskbar = false,
                WindowState = WindowState.Minimized
            };
            var snapshots = new ConcurrentDictionary<Guid, QueueItemSnapshot>();
            var sinks = new ConcurrentDictionary<Guid, IQueueItemStateSink>();
            queue.ConfigureUiBindings(
                id => snapshots.TryGetValue(id, out QueueItemSnapshot? snapshot) ? snapshot : null,
                id => sinks.TryGetValue(id, out IQueueItemStateSink? sink) ? sink : null,
                static () => { });

            window.Closed += (_, _) =>
            {
                windowClosed = true;
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };

            window.Show();
            _ = window.Dispatcher.InvokeAsync(
                async () =>
                {
                    try
                    {
                        for (int index = 0; index < queueLength; index++)
                        {
                            string inputPath = Path.Combine(campaignRoot, $"busy-item-{index:D4}.iso");
                            var item = new ChdQueueItem { InputPath = inputPath };
                            snapshots[item.Id] = new QueueItemSnapshot
                            {
                                ItemId = item.Id,
                                OriginalPath = inputPath,
                                SourcePath = inputPath,
                                FileName = Path.GetFileName(inputPath),
                                DetectedPlatform = "SecurityTest",
                                RequestedAction = "Convert"
                            };
                            sinks[item.Id] = NoOpQueueItemStateSink.Instance;

                            QueueEnqueueResult enqueueResult = queue.Enqueue(item);
                            if (enqueueResult != QueueEnqueueResult.Accepted)
                            {
                                throw new InvalidOperationException(
                                    $"Busy queue rejected item {index}: {enqueueResult}.");
                            }
                        }

                        queue.Start();
                        await orchestrator.WaitUntilSaturatedAsync(TimeSpan.FromSeconds(10));
                        await Task.Delay(250);

                        // The actual Window.Close path executes MainWindow_Closing and its full async shutdown pipeline.
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        application.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    }
                },
                DispatcherPriority.Background);

            // The integration-host flag lets Dispatcher.Run raise WPF startup without
            // creating the second production window used by normal application startup.
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            stopwatch.Stop();
            TryDeleteFile(compressionInputPath);
            foreach (string partialOutput in Directory.EnumerateFiles(
                         campaignRoot,
                         "wpf-compression-output-*.cso",
                         SearchOption.TopDirectoryOnly))
            {
                TryDeleteFile(partialOutput);
            }
            resultSource.TrySetResult(
                new WpfShutdownResult(
                    windowClosed,
                    orchestrator.StartedOperations,
                    orchestrator.CancelledOperations,
                    orchestrator.ActiveOperations,
                    stopwatch.ElapsedMilliseconds,
                    failure));
        }
    }

    private static void CreateRealCompressionInput(string path, int sizeBytes)
    {
        byte[] buffer = new byte[1024 * 1024];
        var random = new Random(0x43534F);
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        int remaining = sizeBytes;
        while (remaining > 0)
        {
            random.NextBytes(buffer);
            int write = Math.Min(remaining, buffer.Length);
            stream.Write(buffer, 0, write);
            remaining -= write;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static HashSet<int> GetProcessIdsByName(string processName)
    {
        var ids = new HashSet<int>();
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                ids.Add(process.Id);
            }
        }
        return ids;
    }

    private sealed class RealCompressionSecurityOrchestrator(
        int saturationTarget,
        string csoKitPath,
        string inputIsoPath,
        string outputRoot) : IChdWorkflowOrchestrator
    {
        private readonly TaskCompletionSource _saturated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeOperations;
        private int _startedOperations;
        private int _cancelledOperations;

        public int ActiveOperations => Volatile.Read(ref _activeOperations);

        public int StartedOperations => Volatile.Read(ref _startedOperations);

        public int CancelledOperations => Volatile.Read(ref _cancelledOperations);

        public async Task<WorkflowExecutionResult> ProcessAsync(
            ChdTaskRequest request,
            CancellationToken cancellationToken)
        {
            _ = request ?? throw new ArgumentNullException(nameof(request));
            Interlocked.Increment(ref _activeOperations);
            int started = Interlocked.Increment(ref _startedOperations);
            if (started >= saturationTarget)
            {
                _saturated.TrySetResult();
            }

            try
            {
                string outputPath = Path.Combine(
                    outputRoot,
                    $"wpf-compression-output-{request.OperationId:N}.cso");
                var runner = new ExternalToolProcessRunner();
                ExternalToolProcessResult result = await runner.RunAsync(
                        csoKitPath,
                        [
                            "compress", inputIsoPath,
                            "-o", outputPath,
                            "--profile", "game-safe",
                            "--threads", "1",
                            "--block", "2048",
                            "--zopfli",
                            "--force",
                            "--quiet",
                            "--json"
                        ],
                        cancellationToken,
                        64 * 1024)
                    .ConfigureAwait(false);

                if (result.WasCancelled || cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _cancelledOperations);
                    return WorkflowExecutionResult.Cancelled("Real CsoKit compression was cancelled by WPF shutdown.");
                }

                return result.ExitCode == 0
                    ? WorkflowExecutionResult.Success(
                        QueueItemTerminalOutcome.Healthy,
                        "Real CsoKit compression completed before shutdown.",
                        outputPath)
                    : WorkflowExecutionResult.Failure(
                        QueueItemFailureKind.FailedConvert,
                        "Real CsoKit compression failed during the WPF integration scenario.");
            }
            finally
            {
                Interlocked.Decrement(ref _activeOperations);
            }
        }

        public Task WaitUntilSaturatedAsync(TimeSpan timeout) => _saturated.Task.WaitAsync(timeout);
    }

    private sealed class NoOpQueueItemStateSink : IQueueItemStateSink
    {
        public static readonly NoOpQueueItemStateSink Instance = new();

        public void ResetForRun() { }

        public void ReportStage(QueueItemStage stage, string? detail) { }

        public void ReportProgress(double percent, bool indeterminate) { }

        public void ReportRuntimeProgress(QueueRuntimeProgressSnapshot snapshot) { }

        public void ClearRuntimeProgress() { }

        public void ReportTerminalSuccess(QueueItemTerminalOutcome outcome, string? detail) { }

        public void ReportTerminalFailure(QueueItemFailureKind kind, string? detail) { }

        public void AttachArtifact(QueueItemArtifactKind kind, string path) { }

        public void RecordPlatformDetection(string platform, string reason) { }

        public void RecordInputOutputBytes(long inputBytes, long outputBytes) { }

        public void AddCleanupDeletedBytes(long deltaBytes) { }

        public void RecordPostConversionArtifacts(PostConversionArtifactResult result) { }

        public void RecordConversionPerformanceReport(ConversionPerformanceReport report) { }

        public void ReportWorkingPathPromotion(string newWorkingPath, string newRequestedAction) { }

        public void RestoreArchiveSourceState() { }
    }

    private sealed record WpfShutdownResult(
        bool WindowClosed,
        int StartedOperations,
        int CancelledOperations,
        int ActiveOperationsAfterClose,
        long ElapsedMilliseconds,
        Exception? Exception);
}
