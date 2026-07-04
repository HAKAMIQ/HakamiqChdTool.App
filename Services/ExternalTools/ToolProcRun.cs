using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed record ExternalToolProcessResult(
    int ExitCode,
    bool WasCancelled,
    string StandardOutput,
    string StandardError);

public sealed class ExternalToolProcessRunner
{
    public const int CanceledExitCode = 130;

    private const int DefaultOutputCharacterLimit = 16 * 1024;
    private const int CancelWaitMilliseconds = 10_000;

    private static readonly ILogger Log = global::Serilog.Log.ForContext<ExternalToolProcessRunner>();

    public async Task<ExternalToolProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        int outputCharacterLimit = DefaultOutputCharacterLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        int safeLimit = Math.Clamp(outputCharacterLimit, 1024, 128 * 1024);
        var stdout = new LimitedOutputCollector(safeLimit);
        var stderr = new LimitedOutputCollector(safeLimit);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ErrorDialog = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var stdoutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutClosed.TrySetResult();
                return;
            }

            stdout.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrClosed.TrySetResult();
                return;
            }

            stderr.AppendLine(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                return new ExternalToolProcessResult(1, false, string.Empty, "Process did not start.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException
                                  or InvalidOperationException
                                  or Win32Exception
                                  or NotSupportedException)
        {
            Log.Warning(ex, "External tool process could not start. Tool={ToolPath}", executablePath);
            return new ExternalToolProcessResult(1, false, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);

            return new ExternalToolProcessResult(
                process.ExitCode,
                false,
                stdout.Text,
                stderr.Text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process, executablePath);
            await WaitAfterKillAsync(process).ConfigureAwait(false);

            return new ExternalToolProcessResult(
                CanceledExitCode,
                true,
                stdout.Text,
                stderr.Text);
        }
        catch (TimeoutException ex)
        {
            Log.Debug(ex, "External tool output streams did not close promptly. Tool={ToolPath}", executablePath);
            return new ExternalToolProcessResult(
                process.HasExited ? process.ExitCode : 1,
                false,
                stdout.Text,
                stderr.Text);
        }
    }

    private static void TryKillProcessTree(Process process, string executablePath)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException
                                  or InvalidOperationException
                                  or Win32Exception
                                  or NotSupportedException)
        {
            Log.Debug(ex, "External tool process kill failed after cancellation. Tool={ToolPath}", executablePath);
        }
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(CancelWaitMilliseconds), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException
                                  or InvalidOperationException
                                  or OperationCanceledException)
        {
            Log.Debug(ex, "External tool process did not exit promptly after cancellation.");
        }
    }

    private sealed class LimitedOutputCollector
    {
        private readonly object _sync = new();
        private readonly StringBuilder _builder = new();
        private readonly int _limit;
        private bool _truncated;

        public LimitedOutputCollector(int limit)
        {
            _limit = Math.Max(1024, limit);
        }

        public string Text
        {
            get
            {
                lock (_sync)
                {
                    return _builder.ToString().Trim();
                }
            }
        }

        public void AppendLine(string value)
        {
            lock (_sync)
            {
                if (_truncated)
                {
                    return;
                }

                string line = value + Environment.NewLine;
                int remaining = _limit - _builder.Length;
                if (remaining <= 0)
                {
                    MarkTruncated();
                    return;
                }

                if (line.Length <= remaining)
                {
                    _builder.Append(line);
                    return;
                }

                _builder.Append(line.AsSpan(0, remaining));
                MarkTruncated();
            }
        }

        private void MarkTruncated()
        {
            if (_truncated)
            {
                return;
            }

            const string suffix = "\n[output truncated]";
            if (_builder.Length + suffix.Length <= _limit)
            {
                _builder.Append(suffix);
            }

            _truncated = true;
        }
    }
}
