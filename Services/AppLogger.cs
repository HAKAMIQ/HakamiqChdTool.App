using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.IO;

namespace HakamiqChdTool.App.Services;

public static class AppLogger
{
    private const long MaximumSelfLogBytes = 1024 * 1024;

    private static readonly object Sync = new();
    private static readonly object SelfLogSync = new();

    private static bool _initialized;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                string logsDirectory = AppPaths.LogsDirectory;
                Directory.CreateDirectory(logsDirectory);

                if (IsReparsePoint(logsDirectory))
                {
                    Trace.TraceError("AppLogger: Logs directory is a reparse point.");
                    return;
                }

                string logPath = Path.Combine(logsDirectory, "app-.log");
                string diagnosticLogPath = Path.Combine(logsDirectory, "diag-.log");
                string selfLogPath = Path.Combine(logsDirectory, "serilog-selflog.txt");

                if (!IsSafeLogFileTarget(logPath, logsDirectory)
                    || !IsSafeLogFileTarget(diagnosticLogPath, logsDirectory)
                    || !IsSafeLogFileTarget(selfLogPath, logsDirectory))
                {
                    Trace.TraceError("AppLogger: One or more log targets are unsafe.");
                    return;
                }

                SelfLog.Enable(message =>
                {
                    try
                    {
                        lock (SelfLogSync)
                        {
                            if (!IsSafeLogFileTarget(selfLogPath, logsDirectory))
                            {
                                Trace.TraceError("AppLogger: Serilog self-log target is unsafe.");
                                return;
                            }

                            RotateSelfLogIfNeeded(selfLogPath, logsDirectory);

                            File.AppendAllText(
                                selfLogPath,
                                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
                        }
                    }
                    catch (Exception ex) when (IsLoggingFailure(ex))
                    {
                        Trace.TraceError("AppLogger: Serilog self-log write failed: {0}", ex);
                    }
                });

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .Enrich.WithProperty("Application", "HakamiqChdTool.App")
                    .WriteTo.Async(
                        configure: sink => sink.File(
                            path: logPath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14,
                            shared: true,
                            restrictedToMinimumLevel: LogEventLevel.Information,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                            flushToDiskInterval: TimeSpan.FromSeconds(1)),
                        bufferSize: 50_000,
                        blockWhenFull: false)
                    .WriteTo.Async(
                        configure: sink => sink.File(
                            path: diagnosticLogPath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7,
                            shared: true,
                            restrictedToMinimumLevel: LogEventLevel.Debug,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                            flushToDiskInterval: TimeSpan.FromMilliseconds(500)),
                        bufferSize: 20_000,
                        blockWhenFull: false)
                    .CreateLogger();

                _initialized = true;
            }
            catch (Exception ex) when (IsLoggingFailure(ex))
            {
                SelfLog.Disable();
                _initialized = false;
                Trace.TraceError("AppLogger: initialization failed: {0}", ex);
            }
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                Log.CloseAndFlush();
            }
            catch (Exception ex) when (IsLoggingFailure(ex))
            {
                Trace.TraceError("AppLogger: CloseAndFlush failed: {0}", ex);
            }
            finally
            {
                SelfLog.Disable();
                _initialized = false;
            }
        }
    }

    private static void RotateSelfLogIfNeeded(string selfLogPath, string logsDirectory)
    {
        if (!File.Exists(selfLogPath))
        {
            return;
        }

        FileInfo selfLog = new(selfLogPath);
        if (selfLog.Length <= MaximumSelfLogBytes)
        {
            return;
        }

        string archivePath = Path.Combine(logsDirectory, "serilog-selflog.1.txt");

        if (!IsSafeLogFileTarget(archivePath, logsDirectory))
        {
            throw new IOException();
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        File.Move(selfLogPath, archivePath, overwrite: false);
    }

    private static bool IsSafeLogFileTarget(string filePath, string logsDirectory)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            string fullDirectory = Path.GetFullPath(logsDirectory);

            if (!IsPathInsideDirectory(fullPath, fullDirectory))
            {
                return false;
            }

            if (File.Exists(fullPath) && IsReparsePoint(fullPath))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex) when (IsLoggingFailure(ex))
        {
            return false;
        }
    }

    private static bool IsPathInsideDirectory(string fullPath, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string normalizedPath = Path.GetFullPath(fullPath);

        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
        catch (Exception ex) when (IsLoggingFailure(ex))
        {
            return true;
        }
    }

    private static bool IsLoggingFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException;
    }
}