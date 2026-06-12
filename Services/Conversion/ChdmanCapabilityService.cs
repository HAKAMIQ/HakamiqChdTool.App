using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ChdmanCapabilityService : IChdmanCapabilityService
{
    private const string ChdmanNotFoundMessageKey = "LocChdman_NotFound";
    private const string CapabilityProbeFailedMessageKey = "LocChdPolicy_CapabilityProbeFailed";
    private const int ProbeTimeoutMilliseconds = 4500;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(ProbeTimeoutMilliseconds);
    private static readonly Regex VersionRegex = new(
        @"(?:chdman(?:\.exe)?|MAME)\s+(?:version\s+)?(?<version>v?\d+(?:\.\d+)+(?:[-+._A-Za-z0-9]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private readonly object _sync = new();
    private readonly Dictionary<string, ChdmanCapabilitySnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ChdmanCapabilitySnapshot> InspectAsync(string chdmanPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chdmanPath))
        {
            return ChdmanCapabilitySnapshot.Unavailable(string.Empty, ChdmanNotFoundMessageKey);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(chdmanPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Log.Debug(ex, "chdman capability probe rejected invalid executable path.");
            return ChdmanCapabilitySnapshot.Unavailable(chdmanPath, ChdmanNotFoundMessageKey);
        }

        if (!File.Exists(fullPath))
        {
            return ChdmanCapabilitySnapshot.Unavailable(fullPath, ChdmanNotFoundMessageKey);
        }

        string cacheKey = BuildCacheKey(fullPath);
        lock (_sync)
        {
            if (_cache.TryGetValue(cacheKey, out ChdmanCapabilitySnapshot? cached))
            {
                return cached;
            }
        }

        ChdmanCapabilitySnapshot snapshot = await ProbeAsync(fullPath, cancellationToken).ConfigureAwait(false);

        if (!cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                _cache[cacheKey] = snapshot;
            }
        }

        return snapshot;
    }

    public bool SupportsRequestedCompression(
        ChdmanCapabilitySnapshot capabilities,
        string command,
        string? resolvedCompression)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.SupportsRequestedCompression(command, resolvedCompression);
    }

    public bool SupportsRequestedHunkSize(
        ChdmanCapabilitySnapshot capabilities,
        string command,
        int hunkSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.SupportsRequestedHunkSize(command, hunkSizeBytes);
    }

    private static async Task<ChdmanCapabilitySnapshot> ProbeAsync(string fullPath, CancellationToken cancellationToken)
    {
        List<ProbeResult> probes = [];

        foreach (string[] arguments in new[]
        {
            Array.Empty<string>(),
            new[] { "--help" },
            new[] { "help" },
            new[] { "createdvd", "--help" },
            new[] { "extractdvd", "--help" },
            new[] { "createcd", "--help" }
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
            probes.Add(await RunProbeAsync(fullPath, arguments, cancellationToken).ConfigureAwait(false));
        }

        string combinedText = BuildCombinedProbeText(probes);
        string generalHelpText = BuildCombinedProbeText(probes.GetRange(0, 3));
        bool hasAnyStartedProbe = probes.Exists(static probe => probe.Started);
        bool hasReliableHelpText = combinedText.Length > 0 && hasAnyStartedProbe;

        if (!hasAnyStartedProbe)
        {
            return ChdmanCapabilitySnapshot.Unavailable(fullPath, CapabilityProbeFailedMessageKey);
        }

        bool supportsCreateDvd = ProbeSupportsCommand("createdvd", generalHelpText, probes[3]);
        bool supportsExtractDvd = ProbeSupportsCommand("extractdvd", generalHelpText, probes[4]);
        bool supportsZstd = ContainsToken(combinedText, "zstd");
        bool supportsHunkSize = combinedText.Contains("--hunksize", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(combinedText, @"(?<!\S)-hs(?!\S)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))
            || combinedText.Contains("hunk size", StringComparison.OrdinalIgnoreCase);

        string version = TryParseVersion(combinedText);
        string diagnosticSummary = BuildDiagnosticSummary(probes);

        return new ChdmanCapabilitySnapshot(
            fullPath,
            version,
            IsAvailable: true,
            supportsCreateDvd,
            supportsExtractDvd,
            supportsZstd,
            supportsHunkSize,
            hasReliableHelpText,
            string.Empty,
            diagnosticSummary);
    }

    private static async Task<ProbeResult> RunProbeAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return ProbeResult.NotStarted(arguments, "Process did not start.");
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await WaitForProcessExitAfterKillAsync(process).ConfigureAwait(false);
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            bool timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

            return new ProbeResult(
                arguments,
                Started: true,
                timedOut ? -1 : process.ExitCode,
                stdout ?? string.Empty,
                stderr ?? string.Empty,
                timedOut,
                string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
            Log.Debug(ex, "chdman capability probe command failed to start or complete safely. Args={Args}", string.Join(' ', arguments));
            return ProbeResult.NotStarted(arguments, ex.GetType().Name);
        }
    }

    private static async Task WaitForProcessExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Debug(ex, "chdman capability probe process did not report normal exit after kill.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Log.Debug(ex, "chdman capability probe process could not be killed after timeout.");
        }
    }

    private static bool ProbeSupportsCommand(string command, string generalHelpText, ProbeResult commandProbe)
    {
        string commandText = commandProbe.Text;
        if (TextLooksLikeUnsupportedCommand(commandText, command))
        {
            return false;
        }

        if (HelpListsCommand(generalHelpText, command))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return commandProbe.Started && commandProbe.ExitCode == 0;
        }

        return commandProbe.ExitCode == 0 || HelpListsCommand(commandText, command);
    }

    private static bool HelpListsCommand(string text, string command)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"(^|\s|\b)" + Regex.Escape(command) + @"(\s|\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    private static bool TextLooksLikeUnsupportedCommand(string text, string command)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.ToLowerInvariant();
        return normalized.Contains("unknown command", StringComparison.Ordinal)
            || normalized.Contains("unrecognized command", StringComparison.Ordinal)
            || normalized.Contains("invalid command", StringComparison.Ordinal)
            || normalized.Contains($"unknown command '{command.ToLowerInvariant()}'", StringComparison.Ordinal)
            || normalized.Contains($"unknown command \"{command.ToLowerInvariant()}\"", StringComparison.Ordinal);
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"(?<![A-Za-z0-9_])" + Regex.Escape(token) + @"(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    private static string TryParseVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        Match match = VersionRegex.Match(text);
        return match.Success ? match.Groups["version"].Value : string.Empty;
    }

    private static string BuildCombinedProbeText(IEnumerable<ProbeResult> probes)
    {
        StringBuilder builder = new();
        foreach (ProbeResult probe in probes)
        {
            if (!string.IsNullOrWhiteSpace(probe.StandardOutput))
            {
                builder.AppendLine(probe.StandardOutput);
            }

            if (!string.IsNullOrWhiteSpace(probe.StandardError))
            {
                builder.AppendLine(probe.StandardError);
            }
        }

        return builder.ToString();
    }

    private static string BuildDiagnosticSummary(IEnumerable<ProbeResult> probes)
    {
        StringBuilder builder = new();
        foreach (ProbeResult probe in probes)
        {
            builder.Append('[')
                .Append(string.Join(' ', probe.Arguments))
                .Append(" => ")
                .Append(probe.Started ? probe.ExitCode.ToString() : "not-started");

            if (probe.TimedOut)
            {
                builder.Append(" timeout");
            }

            if (!string.IsNullOrWhiteSpace(probe.FailureReason))
            {
                builder.Append(' ').Append(probe.FailureReason);
            }

            builder.Append(']');
        }

        return builder.ToString();
    }

    private static string BuildCacheKey(string fullPath)
    {
        try
        {
            FileInfo info = new(fullPath);
            return string.Concat(fullPath, "|", info.Length, "|", info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is IOException or UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedProbeException(Exception ex) =>
        IsExpectedPathException(ex)
        || ex is IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or System.ComponentModel.Win32Exception;

    private sealed record ProbeResult(
        IReadOnlyList<string> Arguments,
        bool Started,
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut,
        string FailureReason)
    {
        public string Text => string.Concat(StandardOutput, Environment.NewLine, StandardError);

        public static ProbeResult NotStarted(IReadOnlyList<string> arguments, string failureReason) => new(
            arguments,
            Started: false,
            ExitCode: -1,
            string.Empty,
            string.Empty,
            TimedOut: false,
            failureReason ?? string.Empty);
    }
}
