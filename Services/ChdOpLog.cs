using Serilog;
using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

internal static class ChdOperationLog
{
    public static string TryBuildPath(
        string fileName,
        Func<string> resolveLogsDirectory)
    {
        ArgumentNullException.ThrowIfNull(resolveLogsDirectory);

        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            Log.Warning("CHD operation log path was not prepared because the file name was invalid. FileName={FileName}", fileName);
            return string.Empty;
        }

        try
        {
            string logsDirectory = resolveLogsDirectory();
            if (string.IsNullOrWhiteSpace(logsDirectory))
            {
                Log.Warning("CHD operation log path was not prepared because the logs directory was empty. FileName={FileName}", fileName);
                return string.Empty;
            }

            string fullLogsDirectory = Path.GetFullPath(logsDirectory);
            Directory.CreateDirectory(fullLogsDirectory);

            string logPath = Path.GetFullPath(Path.Combine(fullLogsDirectory, fileName));
            if (!string.Equals(Path.GetDirectoryName(logPath), fullLogsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("CHD operation log path escaped the logs directory. Path={LogPath}", logPath);
                return string.Empty;
            }

            return logPath;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Log.Warning(ex, "Failed to prepare CHD operation log path. FileName={FileName}", fileName);
            return string.Empty;
        }
    }

    public static async Task<bool> TryWriteAsync(
        string logPath,
        string contents,
        string operationName)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return false;
        }

        try
        {
            await File.WriteAllTextAsync(logPath, contents, CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Log.Warning(
                ex,
                "Failed to write CHD operation log. Operation={Operation}; Path={LogPath}",
                operationName,
                logPath);
            return false;
        }
    }

    private static bool IsExpectedFileSystemException(Exception exception) =>
        exception is IOException
        or UnauthorizedAccessException
        or SecurityException
        or ArgumentException
        or NotSupportedException
        or InvalidOperationException;
}
