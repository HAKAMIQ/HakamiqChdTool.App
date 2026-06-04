using Serilog;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

public static class SevenZipProcessRunner
{
    private const int ReadBufferSize = 4096;
    private const int RollingMaxChars = 1024;

    private const string ExecutablePathRequiredMessageKey = "LocArchive_SevenZipExecutablePathRequired";
    private const string ExecutablePathInvalidMessageKey = "LocArchive_SevenZipExecutablePathInvalid";
    private const string ExecutableNotFoundMessageKey = "LocArchive_SevenZipExecutableNotFound";
    private const string ProcessStartFailedMessageKey = "LocArchive_SevenZipProcessStartFailed";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(SevenZipProcessRunner));
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PumpCompletionTimeout = TimeSpan.FromSeconds(5);

    public static async Task<SevenZipProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool parseProgressPercent,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string fullExecutablePath = ResolveExecutablePathOrThrow(executablePath);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var captureLock = new object();

        var startInfo = new ProcessStartInfo
        {
            FileName = fullExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(ProcessStartFailedMessageKey);
            }
        }
        catch (Exception ex) when (IsExpectedProcessStartException(ex))
        {
            Logger.Error(ex, "7-Zip process start failed. ArgumentCount={ArgumentCount}", arguments.Count);
            throw new InvalidOperationException(ProcessStartFailedMessageKey, ex);
        }

        progress?.Report(0);

        Task stdoutPump = PumpStreamAsync(
            process.StandardOutput.BaseStream,
            stdout,
            captureLock,
            parseProgressPercent,
            progress,
            isErrorStream: false,
            cancellationToken);

        Task stderrPump = PumpStreamAsync(
            process.StandardError.BaseStream,
            stderr,
            captureLock,
            parseProgressPercent,
            progress,
            isErrorStream: true,
            cancellationToken);

        Task exitTask = process.WaitForExitAsync(CancellationToken.None);

        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                var payload = (CancellationPayload)state!;
                TryKillProcessTree(payload.Process, "cancellation requested");
                payload.Signal.TrySetResult(true);
            },
            new CancellationPayload(process, cancellationSignal));

        Task winner = await Task.WhenAny(exitTask, cancellationSignal.Task).ConfigureAwait(false);
        bool wasCancelled = ReferenceEquals(winner, cancellationSignal.Task);

        if (wasCancelled)
        {
            TryKillProcessTree(process, "cancellation signal won");

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
                catch (Exception ex) when (IsExpectedProcessWaitException(ex))
                {
                    Logger.Debug(ex, "7-Zip exit task faulted after cancellation.");
                }
            }
            else
            {
                Logger.Warning(
                    "7-Zip did not exit within cancellation timeout. ProcessId={ProcessId}",
                    SafeProcessId(process));
            }
        }
        else
        {
            await exitTask.ConfigureAwait(false);
        }

        await WaitForPumpCompletionWithTimeoutAsync(
                Task.WhenAll(stdoutPump, stderrPump),
                stdoutPump,
                stderrPump,
                PumpCompletionTimeout,
                wasCancelled ? "cancellation" : "normal completion",
                SafeProcessId(process))
            .ConfigureAwait(false);

        int exitCode = SafeExitCode(process, wasCancelled);
        if (!wasCancelled)
        {
            progress?.Report(exitCode == 0 ? 100 : 0);
        }

        return new SevenZipProcessResult
        {
            ExitCode = exitCode,
            WasCancelled = wasCancelled,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString()
        };
    }

    private static string ResolveExecutablePathOrThrow(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException(ExecutablePathRequiredMessageKey, nameof(executablePath));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            throw new ArgumentException(ExecutablePathInvalidMessageKey, nameof(executablePath), ex);
        }

        if (!IsValidSevenZipExecutable(fullPath))
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(ExecutableNotFoundMessageKey, fullPath);
            }

            throw new ArgumentException(ExecutablePathInvalidMessageKey, nameof(executablePath));
        }

        return fullPath;
    }

    private static bool IsValidSevenZipExecutable(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                return false;
            }

            if (!string.Equals(Path.GetFileName(fullPath), "7z.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            if (HasReparsePointInExistingPathFromVolumeRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(directory))
            {
                return false;
            }

            string dllPath = Path.Combine(directory, "7z.dll");
            if (!File.Exists(dllPath))
            {
                return false;
            }

            return !HasReparsePointInExistingPathFromVolumeRoot(dllPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static async Task PumpStreamAsync(
        Stream stream,
        StringBuilder capture,
        object captureLock,
        bool parseProgressPercent,
        IProgress<int>? progress,
        bool isErrorStream,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReadBufferSize];
        var rolling = new StringBuilder();

        try
        {
            while (true)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, read);

                lock (captureLock)
                {
                    capture.Append(chunk);
                }

                if (!parseProgressPercent || progress is null)
                {
                    continue;
                }

                rolling.Append(chunk);
                if (rolling.Length > RollingMaxChars)
                {
                    rolling.Remove(0, rolling.Length - RollingMaxChars);
                }

                if (ChdmanOutputParser.TryParseActiveProgressSnapshot(
                        rolling,
                        isErrorLine: isErrorStream,
                        minimumPercent: null,
                        out ChdmanProgressSnapshot snapshot)
                    && snapshot.Percent is int percent)
                {
                    progress.Report(percent);
                }
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Debug(ex, "7-Zip stream pump cancelled. IsErrorStream={IsErrorStream}", isErrorStream);
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "7-Zip stream pump ended because the stream was disposed. IsErrorStream={IsErrorStream}", isErrorStream);
        }
        catch (IOException ex)
        {
            Logger.Debug(ex, "7-Zip stream pump ended because the stream failed. IsErrorStream={IsErrorStream}", isErrorStream);
        }
    }

    private static async Task WaitForPumpCompletionWithTimeoutAsync(
        Task aggregateTask,
        Task stdoutPump,
        Task stderrPump,
        TimeSpan timeout,
        string context,
        int processId)
    {
        try
        {
            Task completed = await Task.WhenAny(
                    aggregateTask,
                    Task.Delay(timeout, CancellationToken.None))
                .ConfigureAwait(false);

            if (!ReferenceEquals(completed, aggregateTask))
            {
                Logger.Debug(
                    "7-Zip stream pumps did not finish before timeout. Context={Context}, ProcessId={ProcessId}, StdoutDone={StdoutDone}, StderrDone={StderrDone}",
                    context,
                    processId,
                    stdoutPump.IsCompleted,
                    stderrPump.IsCompleted);

                return;
            }

            await aggregateTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedPumpException(ex))
        {
            Logger.Debug(
                ex,
                "7-Zip stream pump completion failed. Context={Context}, ProcessId={ProcessId}",
                context,
                processId);
        }
    }

    private static int SafeExitCode(Process process, bool wasCancelled)
    {
        if (wasCancelled)
        {
            return ChdmanProcessRunner.CanceledExitCode;
        }

        try
        {
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Logger.Debug(ex, "Could not read 7-Zip exit code. ProcessId={ProcessId}", SafeProcessId(process));
            return -1;
        }
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Logger.Debug(ex, "Could not read 7-Zip process id.");
            return -1;
        }
    }

    private static void TryKillProcessTree(Process process, string reason)
    {
        try
        {
            if (!process.HasExited)
            {
                Logger.Debug(
                    "Killing 7-Zip process tree. ProcessId={ProcessId}, Reason={Reason}",
                    SafeProcessId(process),
                    reason);

                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (IsExpectedProcessKillException(ex))
        {
            Logger.Debug(
                ex,
                "Could not kill 7-Zip process tree. ProcessId={ProcessId}, Reason={Reason}",
                SafeProcessId(process),
                reason);
        }
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or System.Security.SecurityException;

    private static bool IsExpectedProcessStartException(Exception ex) =>
        ex is ArgumentException
        or InvalidOperationException
        or Win32Exception
        or NotSupportedException
        or UnauthorizedAccessException;

    private static bool IsExpectedProcessWaitException(Exception ex) =>
        ex is InvalidOperationException
        or ObjectDisposedException
        or Win32Exception;

    private static bool IsExpectedProcessKillException(Exception ex) =>
        ex is InvalidOperationException
        or ObjectDisposedException
        or Win32Exception
        or NotSupportedException;

    private static bool IsExpectedPumpException(Exception ex) =>
        ex is IOException
        or ObjectDisposedException
        or OperationCanceledException;

    private sealed record CancellationPayload(Process Process, TaskCompletionSource<bool> Signal);
}