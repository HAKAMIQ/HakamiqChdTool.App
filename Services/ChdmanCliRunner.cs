using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public static class ChdmanCliRunner
{
    private const string InvalidCommandMessageKey = "LocChdman_InvalidCommand";
    private const string UnsupportedCommandMessageKey = "LocChdman_UnsupportedCommand";
    private const string UnsupportedOptionMessageKey = "LocChdman_UnsupportedOption";
    private const string ForceOptionRestrictedMessageKey = "LocChdman_ForceOptionRestricted";

    public sealed class Result
    {
        public int ExitCode { get; init; }
        public bool WasCancelled { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
    }

    public static string FormatCommandLineForDisplay(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        static bool NeedsQuoting(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            if (value.Contains(' ') || value.Contains('"'))
            {
                return true;
            }

            foreach (char character in value)
            {
                if (character > 127)
                {
                    return true;
                }
            }

            return false;
        }

        static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return NeedsQuoting(value)
                ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                : value;
        }

        return arguments.Count == 0
            ? Quote(executablePath)
            : $"{Quote(executablePath)} {string.Join(" ", arguments.Select(Quote))}";
    }

    public static async Task<Result> ExecuteAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool parseProgressPercent,
        IProgress<int>? progress,
        Action<int>? onProcessStarted,
        CancellationToken cancellationToken = default,
        string? exclusiveFileAccessPath = null,
        TimeSpan? maxExecutionTime = null,
        string? monitoredOutputPath = null,
        IProgress<PerformanceSample>? performanceProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        ValidateChdmanCommand(arguments);

        IAsyncDisposable? pathLease = null;
        CancellationTokenSource? timeoutLinked = null;
        CancellationToken effectiveToken = cancellationToken;

        if (maxExecutionTime is { TotalMilliseconds: > 0 })
        {
            timeoutLinked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutLinked.CancelAfter(maxExecutionTime.Value);
            effectiveToken = timeoutLinked.Token;
        }

        try
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(exclusiveFileAccessPath))
                {
                    string lockedPath = FilePathExclusiveGate.NormalizePathForExclusiveLock(exclusiveFileAccessPath);
                    if (!string.IsNullOrWhiteSpace(lockedPath))
                    {
                        pathLease = await FilePathExclusiveGate.AcquireAsync(lockedPath, effectiveToken)
                            .ConfigureAwait(false);
                    }
                }

                (int exitCode, string stdOut, string stdErr, bool wasCancelled) = await ChdmanProcessRunner.RunAsync(
                        executablePath,
                        arguments,
                        parseProgressPercent,
                        progress,
                        onProcessStarted,
                        effectiveToken,
                        monitoredOutputPath,
                        performanceProgress)
                    .ConfigureAwait(false);

                return new Result
                {
                    ExitCode = exitCode,
                    StandardOutput = stdOut,
                    StandardError = stdErr,
                    WasCancelled = wasCancelled
                };
            }
            catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
            {
                return new Result
                {
                    ExitCode = ChdmanProcessRunner.CanceledExitCode,
                    WasCancelled = true
                };
            }
            finally
            {
                await DisposePathLeaseAsync(pathLease).ConfigureAwait(false);
            }
        }
        finally
        {
            timeoutLinked?.Dispose();
        }
    }

    private static async ValueTask DisposePathLeaseAsync(IAsyncDisposable? pathLease)
    {
        if (pathLease is null)
        {
            return;
        }

        try
        {
            await pathLease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to release chdman exclusive file-access lease.");
        }
    }

    private static void ValidateChdmanCommand(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
        {
            throw new ArgumentException(InvalidCommandMessageKey, nameof(arguments));
        }

        string command = arguments[0].Trim();
        if (!IsSupportedCommand(command))
        {
            throw CreateNotSupportedException(UnsupportedCommandMessageKey, "Command", command);
        }

        for (int i = 1; i < arguments.Count; i++)
        {
            string option = arguments[i];
            if (string.IsNullOrWhiteSpace(option))
            {
                continue;
            }

            if (IsBlockedOption(option))
            {
                throw CreateNotSupportedException(UnsupportedOptionMessageKey, "Option", option);
            }

            if (IsForceOption(option) && !IsCreateOrExtractCommand(command))
            {
                throw CreateNotSupportedException(ForceOptionRestrictedMessageKey, "Option", option);
            }
        }
    }

    private static NotSupportedException CreateNotSupportedException(
        string messageKey,
        string detailName,
        string detailValue)
    {
        var exception = new NotSupportedException(messageKey);
        exception.Data[detailName] = detailValue;
        return exception;
    }

    private static bool IsSupportedCommand(string command) =>
        string.Equals(command, "info", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "verify", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractdvd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extracthd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractraw", StringComparison.OrdinalIgnoreCase);

    private static bool IsCreateOrExtractCommand(string command) =>
        string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractdvd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extracthd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractraw", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedOption(string option) =>
        string.Equals(option, "--fix", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "--inputparent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-ip", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "--outputparent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-op", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "--inputstartbyte", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-isb", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "--inputstarthunk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-ish", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "--inputbytes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-ib", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "--inputhunks", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-ih", StringComparison.OrdinalIgnoreCase);

    private static bool IsForceOption(string option) =>
        string.Equals(option, "--force", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option, "-f", StringComparison.OrdinalIgnoreCase);
}