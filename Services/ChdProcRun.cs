using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public static class ChdmanProcessRunner
{
    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(ChdmanProcessRunner));

    private const int ReadBufferSize = 1024;
    private const int RollingMaxChars = 4096;
    private const int FullCaptureMaxChars = 262144;
    private const int ProgressEmitIntervalMs = 100;

    private const string InvalidChdmanPathMessageKey = "LocConversion_InvalidChdmanPath";
    private const string ChdmanNotFoundMessageKey = "LocConversion_ChdmanNotFound";
    private const string InvalidChdmanCommandMessageKey = "LocConversion_Failed";

    public const int CanceledExitCode = -1073741510;

    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PumpCompletionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PerformanceCompletionTimeout = TimeSpan.FromSeconds(5);

    public static async Task<(int ExitCode, string StandardOutput, string StandardError, bool WasCancelled)> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool parseProgressPercent,
        IProgress<int>? progress,
        Action<int>? onProcessStarted,
        CancellationToken cancellationToken,
        string? monitoredOutputPath = null,
        IProgress<PerformanceSample>? performanceProgress = null,
        ChdmanProcessPriorityMode priorityMode = ChdmanProcessPriorityMode.Quiet)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
        {
            throw new ArgumentException(InvalidChdmanCommandMessageKey, nameof(arguments));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return (CanceledExitCode, string.Empty, string.Empty, true);
        }

        string resolvedExecutablePath = ResolveExecutablePathForProcess(executablePath);

        var totalElapsed = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var rollingOut = new StringBuilder();
        var rollingErr = new StringBuilder();
        var captureLock = new object();

        bool progressEmitterActive = parseProgressPercent && progress is not null;
        IChdProgressParser progressParser = ChdProgressParser.Shared;
        var emitter = progressEmitterActive ? new ThrottledProgressEmitter(progress!, progressParser) : null;

        var psi = new ProcessStartInfo
        {
            FileName = resolvedExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        Logger.Information(
            "chdman process starting. ArgumentCount={ArgumentCount}, ParseProgress={ParseProgress}, MonitoredOutput={HasMonitoredOutput}, PerformanceMonitor={HasPerformanceMonitor}",
            arguments.Count,
            parseProgressPercent,
            !string.IsNullOrWhiteSpace(monitoredOutputPath),
            performanceProgress is not null);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(InvalidChdmanPathMessageKey);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Logger.Error(
                ex,
                "chdman process start failed. ArgumentCount={ArgumentCount}, ParseProgress={ParseProgress}",
                arguments.Count,
                parseProgressPercent);

            throw;
        }

        CancellationTokenSource? performanceCts = null;
        Task performanceTask = Task.CompletedTask;
        Task stdoutPump = Task.CompletedTask;
        Task stderrPump = Task.CompletedTask;
        Task? exitTask = null;
        bool completedWithResult = false;

        try
        {
            int processId = SafeProcessId(process);

            TryNotifyProcessStarted(onProcessStarted, processId);
            TrySetChdmanPriority(processId, priorityMode);
            TryReportProgress(progress, 0);

            performanceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            performanceTask = performanceProgress is null
                ? Task.CompletedTask
                : new PerformanceAnalyzerService().MonitorProcessAsync(
                    process,
                    monitoredOutputPath,
                    performanceProgress,
                    performanceCts.Token);

            stdoutPump = PumpUtf8StreamAsync(
                process.StandardOutput.BaseStream,
                "stdout",
                stdout,
                rollingOut,
                captureLock,
                emitter,
                cancellationToken);

            stderrPump = PumpUtf8StreamAsync(
                process.StandardError.BaseStream,
                "stderr",
                stderr,
                rollingErr,
                captureLock,
                emitter,
                cancellationToken);

            exitTask = process.WaitForExitAsync(CancellationToken.None);

            var cancellationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    var payload = (CancellationPayload)state!;
                    TryKillProcessTree(payload.Process, "cancellation requested");
                    payload.Signal.TrySetResult(true);
                },
                new CancellationPayload(process, cancellationSignal));

            Task winner = await Task.WhenAny(exitTask, cancellationSignal.Task).ConfigureAwait(false);

            if (ReferenceEquals(winner, cancellationSignal.Task))
            {
                TryKillProcessTree(process, "cancellation signal won");

                await WaitForProcessExitAfterKillAsync(
                        exitTask,
                        process,
                        "cancellation")
                    .ConfigureAwait(false);

                performanceCts.Cancel();

                await WaitForPumpCompletionWithTimeoutAsync(
                        Task.WhenAll(stdoutPump, stderrPump, performanceTask),
                        stdoutPump,
                        stderrPump,
                        performanceTask,
                        PumpCompletionTimeout,
                        "cancellation",
                        SafeProcessId(process))
                    .ConfigureAwait(false);

                emitter?.FlushFinal();

                Logger.Debug(
                    "chdman process cancelled. ProcessId={ProcessId}, DurationMs={DurationMs}",
                    SafeProcessId(process),
                    totalElapsed.ElapsedMilliseconds);

                completedWithResult = true;
                return (CanceledExitCode, stdout.ToString().Trim(), stderr.ToString().Trim(), true);
            }

            await exitTask.ConfigureAwait(false);

            performanceCts.Cancel();

            await WaitForPumpCompletionWithTimeoutAsync(
                    Task.WhenAll(stdoutPump, stderrPump),
                    stdoutPump,
                    stderrPump,
                    PumpCompletionTimeout,
                    "normal completion",
                    SafeProcessId(process))
                .ConfigureAwait(false);

            await WaitForPumpCompletionWithTimeoutAsync(
                    performanceTask,
                    performanceTask,
                    PerformanceCompletionTimeout,
                    "performance monitor completion",
                    SafeProcessId(process))
                .ConfigureAwait(false);

            emitter?.FlushFinal();

            int exitCode = process.ExitCode;

            Logger.Information(
                "chdman process exited. ProcessId={ProcessId}, ExitCode={ExitCode}, DurationMs={DurationMs}",
                SafeProcessId(process),
                exitCode,
                totalElapsed.ElapsedMilliseconds);

            completedWithResult = true;
            return (exitCode, stdout.ToString().Trim(), stderr.ToString().Trim(), false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process, "operation cancelled after process start");

            await WaitForProcessExitAfterKillAsync(
                    exitTask,
                    process,
                    "operation cancelled after process start")
                .ConfigureAwait(false);

            performanceCts?.Cancel();

            await WaitForPumpCompletionWithTimeoutAsync(
                    Task.WhenAll(stdoutPump, stderrPump, performanceTask),
                    stdoutPump,
                    stderrPump,
                    performanceTask,
                    PumpCompletionTimeout,
                    "operation cancelled after process start",
                    SafeProcessId(process))
                .ConfigureAwait(false);

            emitter?.FlushFinal();

            completedWithResult = true;
            return (CanceledExitCode, stdout.ToString().Trim(), stderr.ToString().Trim(), true);
        }
        catch
        {
            if (!completedWithResult)
            {
                TryKillProcessTree(process, "runner failed after process start");

                await WaitForProcessExitAfterKillAsync(
                        exitTask,
                        process,
                        "runner failure")
                    .ConfigureAwait(false);

                performanceCts?.Cancel();

                await WaitForPumpCompletionWithTimeoutAsync(
                        Task.WhenAll(stdoutPump, stderrPump, performanceTask),
                        stdoutPump,
                        stderrPump,
                        performanceTask,
                        PumpCompletionTimeout,
                        "runner failure",
                        SafeProcessId(process))
                    .ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            performanceCts?.Dispose();
        }
    }

    private sealed record CancellationPayload(
        Process Process,
        TaskCompletionSource<bool> Signal);

    private sealed record TaskObservationPayload(
        string OperationName,
        string Phase,
        int ProcessId);

    private static string ResolveExecutablePathForProcess(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(executablePath));
        }

        string normalizedInput = executablePath.Trim();

        if (normalizedInput.Length >= 2 &&
            normalizedInput[0] == '"' &&
            normalizedInput[^1] == '"')
        {
            normalizedInput = normalizedInput[1..^1].Trim();
        }

        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(executablePath));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(normalizedInput);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(executablePath), ex);
        }

        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(InvalidChdmanPathMessageKey);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(ChdmanNotFoundMessageKey, fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(InvalidChdmanPathMessageKey);
        }

        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(executablePath), ex);
        }

        if (info.Length <= 0)
        {
            throw new InvalidOperationException(InvalidChdmanPathMessageKey);
        }

        return fullPath;
    }

    private static async Task WaitForProcessExitAfterKillAsync(
        Task? exitTask,
        Process process,
        string phase)
    {
        if (exitTask is null)
        {
            return;
        }

        int processId = SafeProcessId(process);

        Task completedExit = await Task.WhenAny(
                exitTask,
                Task.Delay(ProcessExitTimeout, CancellationToken.None))
            .ConfigureAwait(false);

        if (ReferenceEquals(completedExit, exitTask))
        {
            try
            {
                await exitTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Debug(
                    ex,
                    "chdman exit task faulted after kill. Phase={Phase}, ProcessId={ProcessId}",
                    phase,
                    processId);
            }

            return;
        }

        ObserveTaskAfterTimeout(
            exitTask,
            "chdman exit task",
            phase,
            processId);

        Logger.Warning(
            "chdman did not exit within cancellation timeout. Phase={Phase}, ProcessId={ProcessId}",
            phase,
            processId);
    }

    private static void TryKillProcessTree(Process process, string reason)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            int processId = process.Id;
            process.Kill(entireProcessTree: true);

            Logger.Debug(
                "chdman process kill requested. ProcessId={ProcessId}, Reason={Reason}",
                processId,
                reason);
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(
                ex,
                "chdman process was already disposed before kill. Reason={Reason}",
                reason);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(
                ex,
                "chdman process was already exited before kill. ProcessId={ProcessId}, Reason={Reason}",
                SafeProcessId(process),
                reason);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Logger.Debug(
                ex,
                "chdman process kill failed due to Win32 restrictions. ProcessId={ProcessId}, Reason={Reason}",
                SafeProcessId(process),
                reason);
        }
        catch (NotSupportedException ex)
        {
            Logger.Debug(
                ex,
                "chdman process tree kill is not supported. ProcessId={ProcessId}, Reason={Reason}",
                SafeProcessId(process),
                reason);
        }
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Unable to read process id because the process object was disposed.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "Unable to read process id because the process is no longer available.");
            return 0;
        }
    }

    private static void TryNotifyProcessStarted(Action<int>? onProcessStarted, int processId)
    {
        if (onProcessStarted is null || processId <= 0)
        {
            return;
        }

        try
        {
            onProcessStarted(processId);
        }
        catch (Exception ex)
        {
            Logger.Warning(
                ex,
                "chdman process-start notification subscriber failed. ProcessId={ProcessId}",
                processId);
        }
    }

    private static void TryReportProgress(IProgress<int>? progress, int value)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress.Report(Math.Clamp(value, 0, 100));
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "chdman progress subscriber threw.");
        }
    }

    private static async Task PumpUtf8StreamAsync(
        Stream stream,
        string streamName,
        StringBuilder fullSink,
        StringBuilder rolling,
        object captureLock,
        ThrottledProgressEmitter? emitter,
        CancellationToken cancellationToken)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        byte[] buffer = new byte[ReadBufferSize];
        char[] charBuffer = new char[ReadBufferSize * 2];
        var frame = new StringBuilder();

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                int charCount = decoder.GetChars(buffer, 0, read, charBuffer, 0, flush: false);
                if (charCount <= 0)
                {
                    continue;
                }

                for (int i = 0; i < charCount; i++)
                {
                    char current = charBuffer[i];

                    if (current == '\r' || current == '\n')
                    {
                        if (frame.Length > 0)
                        {
                            lock (captureLock)
                            {
                                AppendCapped(fullSink, frame.ToString());
                                AppendCapped(fullSink, Environment.NewLine);
                                AppendRolling(rolling, frame.ToString());
                                emitter?.ParseRollingAndMaybeEmit(rolling);
                            }

                            frame.Clear();
                        }

                        continue;
                    }

                    frame.Append(current);
                    if (frame.Length > RollingMaxChars)
                    {
                        frame.Remove(0, frame.Length - RollingMaxChars);
                    }
                }
            }

            int tailChars = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
            for (int i = 0; i < tailChars; i++)
            {
                char current = charBuffer[i];

                if (current == '\r' || current == '\n')
                {
                    if (frame.Length > 0)
                    {
                        lock (captureLock)
                        {
                            AppendCapped(fullSink, frame.ToString());
                            AppendCapped(fullSink, Environment.NewLine);
                            AppendRolling(rolling, frame.ToString());
                            emitter?.ParseRollingAndMaybeEmit(rolling);
                        }

                        frame.Clear();
                    }

                    continue;
                }

                frame.Append(current);
            }

            if (frame.Length > 0)
            {
                lock (captureLock)
                {
                    AppendCapped(fullSink, frame.ToString());
                    AppendCapped(fullSink, Environment.NewLine);
                    AppendRolling(rolling, frame.ToString());
                    emitter?.ParseRollingAndMaybeEmit(rolling);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ex)
        {
            Logger.Debug(ex, "chdman {StreamName} stream pump ended because the process pipe was closed.", streamName);
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "chdman {StreamName} stream pump ended because the stream was disposed.", streamName);
        }
        catch (DecoderFallbackException ex)
        {
            Logger.Warning(ex, "chdman {StreamName} stream pump failed while decoding UTF-8 output.", streamName);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unexpected chdman {StreamName} stream pump failure.", streamName);
        }
    }

    private static async Task WaitForPumpCompletionWithTimeoutAsync(
        Task completionTask,
        Task firstPump,
        TimeSpan timeout,
        string phase,
        int processId)
    {
        await WaitForPumpCompletionWithTimeoutAsync(
                completionTask,
                new[] { firstPump },
                timeout,
                phase,
                processId)
            .ConfigureAwait(false);
    }

    private static async Task WaitForPumpCompletionWithTimeoutAsync(
        Task completionTask,
        Task firstPump,
        Task secondPump,
        TimeSpan timeout,
        string phase,
        int processId)
    {
        await WaitForPumpCompletionWithTimeoutAsync(
                completionTask,
                new[] { firstPump, secondPump },
                timeout,
                phase,
                processId)
            .ConfigureAwait(false);
    }

    private static async Task WaitForPumpCompletionWithTimeoutAsync(
        Task completionTask,
        Task firstPump,
        Task secondPump,
        Task thirdPump,
        TimeSpan timeout,
        string phase,
        int processId)
    {
        await WaitForPumpCompletionWithTimeoutAsync(
                completionTask,
                new[] { firstPump, secondPump, thirdPump },
                timeout,
                phase,
                processId)
            .ConfigureAwait(false);
    }

    private static async Task WaitForPumpCompletionWithTimeoutAsync(
        Task completionTask,
        IReadOnlyList<Task> pumps,
        TimeSpan timeout,
        string phase,
        int processId)
    {
        Task completedTask = await Task.WhenAny(
                completionTask,
                Task.Delay(timeout, CancellationToken.None))
            .ConfigureAwait(false);

        if (ReferenceEquals(completedTask, completionTask))
        {
            await SuppressPumpCompletionAsync(pumps).ConfigureAwait(false);
            return;
        }

        ObserveTaskAfterTimeout(
            completionTask,
            "chdman background pump completion",
            phase,
            processId);

        Logger.Warning(
            "chdman background pump did not finish within timeout. Phase={Phase}, ProcessId={ProcessId}",
            phase,
            processId);
    }

    private static async Task SuppressPumpCompletionAsync(IEnumerable<Task> pumps)
    {
        try
        {
            await Task.WhenAll(pumps).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Logger.Debug(ex, "chdman stream or performance pump was cancelled.");
        }
        catch (IOException ex)
        {
            Logger.Debug(ex, "chdman stream pump ended after process termination or pipe closure.");
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "chdman stream pump ended because the underlying stream was disposed.");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unexpected chdman stream or performance pump failure.");
        }
    }

    private static void ObserveTaskAfterTimeout(
        Task task,
        string operationName,
        string phase,
        int processId)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.IsCompleted)
        {
            ObserveCompletedTaskFault(task, operationName, phase, processId);
            return;
        }

        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                var payload = (TaskObservationPayload)state!;

                ObserveCompletedTaskFault(
                    completedTask,
                    payload.OperationName,
                    payload.Phase,
                    payload.ProcessId);
            },
            new TaskObservationPayload(operationName, phase, processId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveCompletedTaskFault(
        Task task,
        string operationName,
        string phase,
        int processId)
    {
        if (task.Exception is null)
        {
            return;
        }

        task.Exception.Handle(static _ => true);

        Logger.Debug(
            task.Exception,
            "{OperationName} faulted after timeout. Phase={Phase}, ProcessId={ProcessId}",
            operationName,
            phase,
            processId);
    }

    private static void AppendRolling(StringBuilder rolling, ReadOnlySpan<char> chunk)
    {
        rolling.Append(chunk);
        if (rolling.Length > RollingMaxChars)
        {
            rolling.Remove(0, rolling.Length - RollingMaxChars);
        }
    }

    private static void AppendCapped(StringBuilder sink, ReadOnlySpan<char> chunk)
    {
        sink.Append(chunk);
        if (sink.Length > FullCaptureMaxChars)
        {
            sink.Remove(0, sink.Length - FullCaptureMaxChars);
        }
    }

    private static void TrySetChdmanPriority(
        int processId,
        ChdmanProcessPriorityMode priorityMode)
    {
        if (processId <= 0)
        {
            return;
        }

        if (priorityMode == ChdmanProcessPriorityMode.Normal)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "chdman priority was not changed because the process object was disposed. ProcessId={ProcessId}", processId);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "chdman priority was not changed because the process already exited. ProcessId={ProcessId}", processId);
        }
        catch (ArgumentException ex)
        {
            Logger.Debug(ex, "chdman priority was not changed because the process id was invalid. ProcessId={ProcessId}", processId);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Logger.Debug(ex, "chdman priority change failed due to Win32 restrictions. ProcessId={ProcessId}", processId);
        }
    }

    private sealed class ThrottledProgressEmitter
    {
        private readonly IProgress<int> _progress;
        private readonly IChdProgressParser _parser;
        private readonly object _gate = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private int _pendingMax = -1;
        private int _lastEmitted = -1;
        private long _lastEmitAtMs = -ProgressEmitIntervalMs;

        public ThrottledProgressEmitter(IProgress<int> progress, IChdProgressParser parser)
        {
            _progress = progress;
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        public void ParseRollingAndMaybeEmit(StringBuilder rolling)
        {
            if (rolling.Length == 0)
            {
                return;
            }

            if (_parser.TryParseActiveProgressSnapshot(
                    rolling,
                    isErrorLine: false,
                    minimumPercent: null,
                    out ChdmanProgressSnapshot snapshot) &&
                snapshot.Percent is int activePercent)
            {
                SubmitPercent(activePercent);
                return;
            }

            if (_parser.TryParseLastPercent(rolling, out int fallbackPercent))
            {
                SubmitPercent(fallbackPercent);
            }
        }

        public void FlushFinal()
        {
            lock (_gate)
            {
                EmitPendingCore(force: true);
            }
        }

        private void SubmitPercent(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);

            lock (_gate)
            {
                if (percent <= _lastEmitted && percent <= _pendingMax)
                {
                    return;
                }

                _pendingMax = Math.Max(_pendingMax, percent);
                EmitPendingCore(force: percent >= 100);
            }
        }

        private void EmitPendingCore(bool force)
        {
            if (_pendingMax < 0)
            {
                return;
            }

            long nowMs = _stopwatch.ElapsedMilliseconds;
            if (!force && nowMs - _lastEmitAtMs < ProgressEmitIntervalMs)
            {
                return;
            }

            int candidate = Math.Clamp(Math.Max(_pendingMax, _lastEmitted), 0, 100);
            if (candidate <= _lastEmitted)
            {
                _pendingMax = -1;
                return;
            }

            _lastEmitted = candidate;
            _lastEmitAtMs = nowMs;
            _pendingMax = -1;

            try
            {
                _progress.Report(candidate);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "chdman progress subscriber threw.");
            }
        }
    }
}
