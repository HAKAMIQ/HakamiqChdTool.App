using HakamiqChdTool.App.Core.Contracts;
using HakamiqChdTool.App.Models;
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

public sealed class ChdVerificationService : IChdVerificationService
{
    private const string ValidMessageKey = "LocChdVerify_Valid";
    private const string InvalidMessageKey = "LocChdVerify_Invalid";
    private const string ToolStartFailedMessageKey = "LocChdVerify_ToolStartFailed";
    private const string ToolExecutionFailedMessageKey = "LocChdVerify_ToolExecutionFailed";
    private const string InvalidInputPathMessageKey = "LocChdVerify_InvalidInputPath";
    private const string InputFileNotFoundMessageKey = "LocChdVerify_InputFileNotFound";
    private const string UnsupportedFileTypeMessageKey = "LocChdVerify_UnsupportedFileType";
    private const string CancelledMessageKey = "LocStatus_UserCancelled";
    private const string InvalidChdmanPathMessageKey = "LocConversion_InvalidChdmanPath";
    private const string ChdmanNotFoundMessageKey = "LocConversion_ChdmanNotFound";

    public async Task<ChdVerificationResult> VerifyAsync(
        string chdmanPath,
        string chdFilePath,
        IProgress<int>? progress = null,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default,
        ChdmanProcessPriorityMode priorityMode = ChdmanProcessPriorityMode.Quiet)
    {
        var stopwatch = Stopwatch.StartNew();
        string safeInputName = BuildSafeInputName(chdFilePath);
        string logsDirectory = BuildLogsDirectory();
        string logPath = Path.Combine(logsDirectory, $"verify_{DateTime.Now:yyyyMMdd_HHmmss}_{safeInputName}.log");
        string commandLine = string.Empty;

        await WriteVerificationLogAsync(
                logPath,
                status: "Started",
                file: chdFilePath,
                commandLine: commandLine,
                exitCode: null,
                output: string.Empty,
                error: string.Empty,
                exception: null)
            .ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.Cancelled,
                    isSuccess: false,
                    wasCancelled: true,
                    exitCode: ChdmanProcessRunner.CanceledExitCode,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: string.Empty,
                    message: CancelledMessageKey,
                    logPath: logPath,
                    exception: null)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(chdmanPath))
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.ToolStartFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: InvalidChdmanPathMessageKey,
                    message: ToolStartFailedMessageKey,
                    logPath: logPath,
                    exception: null)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(chdFilePath))
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.ToolExecutionFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: InvalidInputPathMessageKey,
                    message: InvalidInputPathMessageKey,
                    logPath: logPath,
                    exception: null)
                .ConfigureAwait(false);
        }

        try
        {
            ConversionPathValidator.ThrowIfUnsafeForChdman(chdmanPath, nameof(chdmanPath));
            ConversionPathValidator.ThrowIfUnsafeForChdman(chdFilePath, nameof(chdFilePath));
        }
        catch (Exception ex) when (IsExpectedValidationException(ex))
        {
            Log.Warning(ex, "CHD verify rejected unsafe path. File: {ChdPath}", chdFilePath);

            return await BuildResultAsync(
                    ChdVerificationStatus.ToolExecutionFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: InvalidInputPathMessageKey,
                    message: InvalidInputPathMessageKey,
                    logPath: logPath,
                    exception: ex)
                .ConfigureAwait(false);
        }

        if (!File.Exists(chdmanPath))
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.ToolStartFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: ChdmanNotFoundMessageKey,
                    message: ToolStartFailedMessageKey,
                    logPath: logPath,
                    exception: null)
                .ConfigureAwait(false);
        }

        if (!File.Exists(chdFilePath))
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.ToolExecutionFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: InputFileNotFoundMessageKey,
                    message: InputFileNotFoundMessageKey,
                    logPath: logPath,
                    exception: null)
                .ConfigureAwait(false);
        }

        string resolvedChdPath;
        try
        {
            resolvedChdPath = NormalizePathForCli(chdFilePath);
        }
        catch (Exception ex) when (IsExpectedValidationException(ex))
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.ToolExecutionFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: chdFilePath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: InvalidInputPathMessageKey,
                    message: InvalidInputPathMessageKey,
                    logPath: logPath,
                    exception: ex)
                .ConfigureAwait(false);
        }

        if (!string.Equals(Path.GetExtension(resolvedChdPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildResultAsync(
                    ChdVerificationStatus.ToolExecutionFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: resolvedChdPath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: UnsupportedFileTypeMessageKey,
                    message: UnsupportedFileTypeMessageKey,
                    logPath: logPath,
                    exception: null)
                .ConfigureAwait(false);
        }

        var arguments = new List<string> { "verify", "-i", resolvedChdPath };
        commandLine = ChdmanCliRunner.FormatCommandLineForDisplay(chdmanPath, arguments);

        await WriteVerificationLogAsync(
                logPath,
                status: "Running",
                file: resolvedChdPath,
                commandLine: commandLine,
                exitCode: null,
                output: string.Empty,
                error: string.Empty,
                exception: null)
            .ConfigureAwait(false);

        Log.Information("CHD verify starting. File: {ChdPath}", resolvedChdPath);
        Log.Information("CHDMAN CMD: {Args}", commandLine);

        ChdmanCliRunner.Result run;
        try
        {
            run = await ChdmanCliRunner.ExecuteAsync(
                    chdmanPath,
                    arguments,
                    parseProgressPercent: progress is not null,
                    progress,
                    onProcessStarted,
                    cancellationToken,
                    exclusiveFileAccessPath: resolvedChdPath,
                    priorityMode: priorityMode)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Debug("CHD verify cancelled. File: {ChdPath}", resolvedChdPath);

            return await BuildResultAsync(
                    ChdVerificationStatus.Cancelled,
                    isSuccess: false,
                    wasCancelled: true,
                    exitCode: ChdmanProcessRunner.CanceledExitCode,
                    file: resolvedChdPath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: string.Empty,
                    message: CancelledMessageKey,
                    logPath: logPath,
                    exception: null,
                    duration: stopwatch.Elapsed)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsToolStartException(ex))
        {
            Log.Error(ex, "CHD verify could not start chdman. File: {ChdPath}", resolvedChdPath);

            return await BuildResultAsync(
                    ChdVerificationStatus.ToolStartFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: resolvedChdPath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: ToolStartFailedMessageKey,
                    message: ToolStartFailedMessageKey,
                    logPath: logPath,
                    exception: ex,
                    duration: stopwatch.Elapsed)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CHD verify execution failed. File: {ChdPath}", resolvedChdPath);

            return await BuildResultAsync(
                    ChdVerificationStatus.ToolExecutionFailed,
                    isSuccess: false,
                    wasCancelled: false,
                    exitCode: -1,
                    file: resolvedChdPath,
                    commandLine: commandLine,
                    output: string.Empty,
                    error: ToolExecutionFailedMessageKey,
                    message: ToolExecutionFailedMessageKey,
                    logPath: logPath,
                    exception: ex,
                    duration: stopwatch.Elapsed)
                .ConfigureAwait(false);
        }

        if (run.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            Log.Debug("CHD verify cancelled. File: {ChdPath}", resolvedChdPath);

            return await BuildResultAsync(
                    ChdVerificationStatus.Cancelled,
                    isSuccess: false,
                    wasCancelled: true,
                    exitCode: run.ExitCode,
                    file: resolvedChdPath,
                    commandLine: commandLine,
                    output: run.StandardOutput,
                    error: run.StandardError,
                    message: CancelledMessageKey,
                    logPath: logPath,
                    exception: null,
                    duration: stopwatch.Elapsed)
                .ConfigureAwait(false);
        }

        string output = run.StandardOutput;
        string error = run.StandardError;

        if (run.ExitCode == 0)
        {
            TryReportProgress(progress, 100);
            Log.Information("CHD verify finished successfully. File: {ChdPath}", resolvedChdPath);

            return await BuildResultAsync(
                    ChdVerificationStatus.Valid,
                    isSuccess: true,
                    wasCancelled: false,
                    exitCode: run.ExitCode,
                    file: resolvedChdPath,
                    commandLine: commandLine,
                    output: output,
                    error: error,
                    message: ValidMessageKey,
                    logPath: logPath,
                    exception: null,
                    duration: stopwatch.Elapsed)
                .ConfigureAwait(false);
        }

        Log.Error(
            "CHD verify failed. File: {ChdPath}, ExitCode: {ExitCode}, StdErr: {StdErr}",
            resolvedChdPath,
            run.ExitCode,
            error);

        return await BuildResultAsync(
                ChdVerificationStatus.Invalid,
                isSuccess: false,
                wasCancelled: false,
                exitCode: run.ExitCode,
                file: resolvedChdPath,
                commandLine: commandLine,
                output: output,
                error: error,
                message: InvalidMessageKey,
                logPath: logPath,
                exception: null,
                duration: stopwatch.Elapsed)
            .ConfigureAwait(false);
    }

    private static async Task<ChdVerificationResult> BuildResultAsync(
        ChdVerificationStatus status,
        bool isSuccess,
        bool wasCancelled,
        int exitCode,
        string? file,
        string commandLine,
        string output,
        string error,
        string message,
        string logPath,
        Exception? exception,
        TimeSpan duration = default)
    {
        await WriteVerificationLogAsync(
                logPath,
                status: status.ToString(),
                file: file,
                commandLine: commandLine,
                exitCode: exitCode,
                output: output,
                error: error,
                exception: exception)
            .ConfigureAwait(false);

        return new ChdVerificationResult
        {
            Status = status,
            IsSuccess = isSuccess,
            WasCancelled = wasCancelled,
            ExitCode = exitCode,
            CommandLine = commandLine,
            Output = output,
            Error = error,
            Message = message,
            LogPath = logPath,
            Duration = duration
        };
    }

    private static bool IsToolStartException(Exception exception) =>
        exception is ArgumentException
        || exception is InvalidOperationException
        || exception is Win32Exception
        || exception is FileNotFoundException
        || exception is UnauthorizedAccessException;

    private static bool IsExpectedValidationException(Exception exception) =>
        exception is ArgumentException
        || exception is InvalidOperationException
        || exception is NotSupportedException
        || exception is IOException
        || exception is UnauthorizedAccessException
        || exception is PathTooLongException;

    private static async Task WriteVerificationLogAsync(
        string logPath,
        string status,
        string? file,
        string commandLine,
        int? exitCode,
        string output,
        string error,
        Exception? exception)
    {
        try
        {
            var logBuilder = new StringBuilder();
            logBuilder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logBuilder.AppendLine($"Status: {status}");
            logBuilder.AppendLine($"File: {file ?? string.Empty}");
            logBuilder.AppendLine($"CommandLine: {commandLine}");
            logBuilder.AppendLine($"ExitCode: {(exitCode.HasValue ? exitCode.Value.ToString() : "not-started")}");
            logBuilder.AppendLine();

            if (!string.IsNullOrWhiteSpace(output))
            {
                logBuilder.AppendLine("=== STDOUT ===");
                logBuilder.AppendLine(output);
                logBuilder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                logBuilder.AppendLine("=== STDERR ===");
                logBuilder.AppendLine(error);
                logBuilder.AppendLine();
            }

            if (exception is not null)
            {
                logBuilder.AppendLine("=== EXCEPTION ===");
                logBuilder.AppendLine(exception.ToString());
                logBuilder.AppendLine();
            }

            string? directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(logPath, logBuilder.ToString(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write CHD verification log. Path={LogPath}", logPath);
        }
    }

    private static string BuildLogsDirectory()
    {
        string path = AppPaths.LogsDirectory;
        Directory.CreateDirectory(path);
        return path;
    }

    private static string BuildSafeInputName(string? value)
    {
        string baseName;
        try
        {
            baseName = string.IsNullOrWhiteSpace(value)
                ? "file"
                : Path.GetFileNameWithoutExtension(value);
        }
        catch
        {
            baseName = "file";
        }

        return SanitizeFileName(baseName);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "file" : value;
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
            Log.Debug(ex, "CHD verification progress subscriber threw.");
        }
    }

    private static string NormalizePathForCli(string path) =>
        FilePathExclusiveGate.NormalizePathForExclusiveLock(path);
}
