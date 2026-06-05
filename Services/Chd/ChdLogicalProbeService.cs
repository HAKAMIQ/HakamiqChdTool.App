using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ChdLogicalProbeService
{
    private const string ToolFileName = "chd_reader_tool.exe";
    private const string ToolFolderName = "Tools";

    private const string ProbeSucceededCode = "ChdLogicalProbeSucceeded";
    private const string ProbeUnavailableCode = "ChdLogicalProbeUnavailable";
    private const string ProbeInvalidInputCode = "ChdLogicalProbeInvalidInput";
    private const string ProbeFailedCode = "ChdLogicalProbeFailed";
    private const string ProbeTimedOutCode = "ChdLogicalProbeTimedOut";
    private const string ProbeCancelledCode = "ChdLogicalProbeCancelled";
    private const string ProbeInvalidOutputCode = "ChdLogicalProbeInvalidOutput";

    private static readonly ILogger Logger = Log.ForContext<ChdLogicalProbeService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public async Task<ChdLogicalProbeResult> ProbeAsync(
        string chdFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveToolPath(out string toolPath))
        {
            return ChdLogicalProbeResult.ToolUnavailable(ProbeUnavailableCode);
        }

        if (!TryNormalizeChdPath(chdFilePath, out string normalizedChdPath))
        {
            return ChdLogicalProbeResult.Failed(ProbeInvalidInputCode);
        }

        var arguments = new[] { "info", normalizedChdPath };
        ChdReaderToolRunResult run = await RunToolAsync(
                toolPath,
                arguments,
                DefaultTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (run.WasCancelled)
        {
            return new ChdLogicalProbeResult
            {
                IsToolAvailable = true,
                WasCancelled = true,
                ExitCode = run.ExitCode,
                MessageCode = ProbeCancelledCode,
                ToolPath = toolPath,
                Elapsed = run.Elapsed
            };
        }

        if (run.TimedOut)
        {
            return new ChdLogicalProbeResult
            {
                IsToolAvailable = true,
                ExitCode = run.ExitCode,
                MessageCode = ProbeTimedOutCode,
                ToolPath = toolPath,
                Elapsed = run.Elapsed
            };
        }

        if (run.ExitCode != 0)
        {
            Logger.Warning(
                "CHD logical probe failed. File={File}, ExitCode={ExitCode}, ErrorLength={ErrorLength}",
                normalizedChdPath,
                run.ExitCode,
                run.StandardError.Length);

            return new ChdLogicalProbeResult
            {
                IsToolAvailable = true,
                ExitCode = run.ExitCode,
                MessageCode = ProbeFailedCode,
                ToolPath = toolPath,
                Elapsed = run.Elapsed
            };
        }

        if (!TryParseInfo(run.StandardOutput, out ChdLogicalProbeResult parsed))
        {
            Logger.Warning(
                "CHD logical probe returned invalid output. File={File}, OutputLength={OutputLength}",
                normalizedChdPath,
                run.StandardOutput.Length);

            return new ChdLogicalProbeResult
            {
                IsToolAvailable = true,
                ExitCode = run.ExitCode,
                MessageCode = ProbeInvalidOutputCode,
                ToolPath = toolPath,
                Elapsed = run.Elapsed
            };
        }

        Logger.Debug(
            "CHD logical probe succeeded. File={File}, PhysicalBytes={PhysicalBytes}, LogicalBytes={LogicalBytes}, HunkBytes={HunkBytes}, TotalHunks={TotalHunks}",
            normalizedChdPath,
            parsed.PhysicalBytes,
            parsed.LogicalBytes,
            parsed.HunkBytes,
            parsed.TotalHunks);

        return parsed with
        {
            IsSuccess = true,
            IsToolAvailable = true,
            ExitCode = run.ExitCode,
            MessageCode = ProbeSucceededCode,
            ToolPath = toolPath,
            Elapsed = run.Elapsed
        };
    }

    private static bool TryResolveToolPath(out string toolPath)
    {
        toolPath = string.Empty;

        try
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, ToolFolderName, ToolFileName);
            if (!File.Exists(candidate))
            {
                candidate = Path.Combine(AppContext.BaseDirectory, ToolFileName);
            }

            if (!File.Exists(candidate))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(candidate);
            if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length <= 0)
            {
                return false;
            }

            toolPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedIoException(ex))
        {
            Logger.Debug(ex, "CHD logical probe tool path resolution failed.");
            return false;
        }
    }

    private static bool TryNormalizeChdPath(string chdFilePath, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(chdFilePath))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(chdFilePath.Trim());
            if (!File.Exists(fullPath))
            {
                return false;
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".chd", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(chdFilePath));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedValidationException(ex))
        {
            Logger.Debug(ex, "CHD logical probe input validation failed.");
            return false;
        }
    }

    private static async Task<ChdReaderToolRunResult> RunToolAsync(
        string toolPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var psi = new ProcessStartInfo
        {
            FileName = toolPath,
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

        try
        {
            if (!process.Start())
            {
                return new ChdReaderToolRunResult(1, string.Empty, string.Empty, false, false, stopwatch.Elapsed);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            stdout.Append(await stdoutTask.ConfigureAwait(false));
            stderr.Append(await stderrTask.ConfigureAwait(false));

            return new ChdReaderToolRunResult(
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString(),
                false,
                false,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            return new ChdReaderToolRunResult(
                ChdmanProcessRunner.CanceledExitCode,
                stdout.ToString(),
                stderr.ToString(),
                true,
                false,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            return new ChdReaderToolRunResult(
                ChdmanProcessRunner.CanceledExitCode,
                stdout.ToString(),
                stderr.ToString(),
                false,
                true,
                stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Logger.Warning(ex, "CHD logical probe process start/read failed.");
            return new ChdReaderToolRunResult(1, stdout.ToString(), stderr.ToString(), false, false, stopwatch.Elapsed);
        }
    }

    private static bool TryParseInfo(string output, out ChdLogicalProbeResult result)
    {
        result = new ChdLogicalProbeResult();

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            values[key] = value;
        }

        if (!TryGetInt64(values, "physical_bytes", out long physicalBytes)
            || !TryGetInt64(values, "logical_bytes", out long logicalBytes)
            || !TryGetInt32(values, "hunk_bytes", out int hunkBytes)
            || !TryGetInt32(values, "total_hunks", out int totalHunks))
        {
            return false;
        }

        _ = TryGetInt64(values, "decoded_cache_bytes", out long decodedCacheBytes);

        if (physicalBytes <= 0 || logicalBytes <= 0 || hunkBytes <= 0 || totalHunks <= 0)
        {
            return false;
        }

        result = new ChdLogicalProbeResult
        {
            PhysicalBytes = physicalBytes,
            LogicalBytes = logicalBytes,
            HunkBytes = hunkBytes,
            TotalHunks = totalHunks,
            DecodedCacheBytes = decodedCacheBytes
        };

        return true;
    }

    private static bool TryGetInt64(
        IReadOnlyDictionary<string, string> values,
        string key,
        out long value)
    {
        value = 0;
        return values.TryGetValue(key, out string? raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt32(
        IReadOnlyDictionary<string, string> values,
        string key,
        out int value)
    {
        value = 0;
        return values.TryGetValue(key, out string? raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Logger.Debug(ex, "CHD logical probe process kill failed.");
        }
    }

    private static bool IsExpectedValidationException(Exception ex) =>
        ex is ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;

    private static bool IsExpectedIoException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or NotSupportedException;

    private sealed record ChdReaderToolRunResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool WasCancelled,
        bool TimedOut,
        TimeSpan Elapsed);
}
