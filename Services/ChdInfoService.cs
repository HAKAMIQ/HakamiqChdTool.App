using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ChdInfoService
{
    private const string UserCancelledMessageKey = "LocStatus_UserCancelled";
    private const string InfoReadSuccessMessageKey = "LocChdInfo_ReadSuccess";
    private const string InfoReadFailedMessageKey = "LocChdInfo_ReadFailed";
    private const string InvalidChdmanPathMessageKey = "LocConversion_InvalidChdmanPath";
    private const string InvalidChdPathMessageKey = "LocExtraction_InvalidChdPath";
    private const string ChdmanNotFoundMessageKey = "LocConversion_ChdmanNotFound";
    private const string InputFileNotFoundMessageKey = "LocConversion_InputFileNotFound";

    private static readonly char[] LineSeparators = ['\r', '\n'];

    public async Task<ChdInfoResult> ReadInfoAsync(
        string chdmanPath,
        string chdFilePath,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chdmanPath))
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(chdmanPath));
        }

        if (string.IsNullOrWhiteSpace(chdFilePath))
        {
            throw new ArgumentException(InvalidChdPathMessageKey, nameof(chdFilePath));
        }

        if (!File.Exists(chdmanPath))
        {
            throw new FileNotFoundException(ChdmanNotFoundMessageKey, chdmanPath);
        }

        if (!File.Exists(chdFilePath))
        {
            throw new FileNotFoundException(InputFileNotFoundMessageKey, chdFilePath);
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(chdmanPath, nameof(chdmanPath));
        ConversionPathValidator.ThrowIfUnsafeForChdman(chdFilePath, nameof(chdFilePath));

        string resolvedChdPath = NormalizePathForCli(chdFilePath);

        if (!string.Equals(Path.GetExtension(resolvedChdPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(InvalidChdPathMessageKey);
        }

        string logsDirectory = BuildLogsDirectory();
        string logPath = Path.Combine(
            logsDirectory,
            $"info_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(Path.GetFileNameWithoutExtension(resolvedChdPath))}.log");

        var arguments = new List<string> { "info", "-i", resolvedChdPath };
        string displayCommandLine = ChdmanCliRunner.FormatCommandLineForDisplay(chdmanPath, arguments);

        Log.Information("CHD info starting. File: {ChdPath}", resolvedChdPath);
        Log.Information("CHDMAN CMD: {Args}", displayCommandLine);

        ChdmanCliRunner.Result run;
        try
        {
            run = await ChdmanCliRunner.ExecuteAsync(
                    chdmanPath,
                    arguments,
                    parseProgressPercent: false,
                    progress: null,
                    onProcessStarted,
                    cancellationToken,
                    exclusiveFileAccessPath: resolvedChdPath)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Debug("CHD info cancelled. File: {ChdPath}", chdFilePath);

            return new ChdInfoResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = ChdmanProcessRunner.CanceledExitCode,
                CommandLine = displayCommandLine,
                Message = UserCancelledMessageKey,
                LogPath = logPath
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CHD info threw. File: {ChdPath}", chdFilePath);
            throw;
        }

        if (run.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            Log.Debug("CHD info cancelled. File: {ChdPath}", chdFilePath);

            return new ChdInfoResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = run.ExitCode,
                CommandLine = displayCommandLine,
                Output = run.StandardOutput,
                Error = run.StandardError,
                Message = UserCancelledMessageKey,
                LogPath = logPath
            };
        }

        string output = run.StandardOutput;
        string error = run.StandardError;
        bool success = run.ExitCode == 0;

        string combined = $"{output}{Environment.NewLine}{error}";
        long? logicalBytes = TryParseLogicalBytes(combined);
        string metadataType = DetectMediaTypeFromMetadata(combined);
        string mediaType;

        if (!IsUnknownMediaType(metadataType))
        {
            mediaType = metadataType;
        }
        else
        {
            string? heuristic = TryInferMediaTypeHeuristic(combined);
            if (heuristic is not null)
            {
                mediaType = heuristic;
                Log.Warning("MediaType corrected via heuristic: {Type}", mediaType);
            }
            else
            {
                mediaType = "Unknown";
            }
        }

        if (success)
        {
            Log.Information("CHD info finished. File: {ChdPath}, MediaType: {MediaType}", chdFilePath, mediaType);
        }
        else
        {
            Log.Error(
                "CHD info failed. File: {ChdPath}, ExitCode: {ExitCode}, StdErr: {StdErr}",
                chdFilePath,
                run.ExitCode,
                error);
        }

        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logBuilder.AppendLine($"File: {chdFilePath}");
        logBuilder.AppendLine($"ExitCode: {run.ExitCode}");
        logBuilder.AppendLine($"MediaType (metadata): {metadataType}");
        logBuilder.AppendLine($"MediaType (resolved): {mediaType}");
        logBuilder.AppendLine($"LogicalBytes: {(logicalBytes.HasValue ? logicalBytes.Value.ToString(CultureInfo.InvariantCulture) : "unknown")}");
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

        await File.WriteAllTextAsync(logPath, logBuilder.ToString(), CancellationToken.None)
            .ConfigureAwait(false);

        return new ChdInfoResult
        {
            IsSuccess = success,
            WasCancelled = run.WasCancelled,
            ExitCode = run.ExitCode,
            MediaType = mediaType,
            LogicalBytes = logicalBytes,
            CommandLine = displayCommandLine,
            Output = output,
            Error = error,
            Message = success ? InfoReadSuccessMessageKey : InfoReadFailedMessageKey,
            LogPath = logPath
        };
    }

    private static long? TryParseLogicalBytes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0
                || line.IndexOf("logical", StringComparison.OrdinalIgnoreCase) < 0
                || (line.IndexOf("size", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }

            long? parsed = TryParseSizeFromLine(line);
            if (parsed.HasValue && parsed.Value > 0)
            {
                return parsed.Value;
            }
        }

        return null;
    }

    private static long? TryParseSizeFromLine(string line)
    {
        string source = line.Contains(':', StringComparison.Ordinal)
            ? line[(line.IndexOf(':') + 1)..]
            : line;

        string normalized = source.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        string[] parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number <= 0)
            {
                continue;
            }

            string unit = i + 1 < parts.Length ? parts[i + 1] : "bytes";
            double multiplier = unit.ToLowerInvariant() switch
            {
                "b" or "byte" or "bytes" => 1d,
                "kb" or "kib" => 1024d,
                "mb" or "mib" => 1024d * 1024d,
                "gb" or "gib" => 1024d * 1024d * 1024d,
                "tb" or "tib" => 1024d * 1024d * 1024d * 1024d,
                _ => 1d
            };

            double value = Math.Ceiling(number * multiplier);
            return value >= long.MaxValue ? long.MaxValue : (long)value;
        }

        return null;
    }

    private static bool IsUnknownMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
        || string.Equals(mediaType, "Unknown", StringComparison.OrdinalIgnoreCase);

    private static string DetectMediaTypeFromMetadata(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Unknown";
        }

        string? committed = null;

        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            bool looksStructured =
                line.StartsWith("Metadata", StringComparison.OrdinalIgnoreCase)
                || line.IndexOf("Media type", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Media:", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Disk type", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksStructured)
            {
                continue;
            }

            string? lineType = ClassifyStructuredLine(line);
            if (lineType is null)
            {
                continue;
            }

            if (committed is null)
            {
                committed = lineType;
                continue;
            }

            if (!string.Equals(committed, lineType, StringComparison.OrdinalIgnoreCase))
            {
                return "Unknown";
            }
        }

        return committed ?? "Unknown";
    }

    private static string? ClassifyStructuredLine(string line)
    {
        if (ContainsAny(line, "cht2", "chtr", "chcd", "chgd", "avav", "avld"))
        {
            return "CD-ROM";
        }

        if (ContainsAny(line, "dvd-rom", "dvdrom", "dvdi", "dvdt"))
        {
            return "DVD-ROM";
        }

        if (ContainsAny(line, "gd-rom", "gdrom", "gddd", "gdc"))
        {
            return "CD-ROM";
        }

        if (ContainsAny(line, "cd-rom", "cdrom"))
        {
            return "CD-ROM";
        }

        if (ContainsAny(line, "hard disk", "harddisk", "hd-rom", "hdrom"))
        {
            return "HD-ROM";
        }

        if (ContainsAny(line, "raw media", "raw disk", "raw data"))
        {
            return "Raw";
        }

        return null;
    }

    private static string? TryInferMediaTypeHeuristic(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            bool looksStructured =
                line.StartsWith("Metadata", StringComparison.OrdinalIgnoreCase)
                || line.IndexOf("Media type", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Media:", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Disk type", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksStructured)
            {
                continue;
            }

            string? lineType = ClassifyStructuredLine(line);
            if (lineType is not null)
            {
                return lineType;
            }
        }

        if (LooksLikeCdTrackChd(text))
        {
            return "CD-ROM";
        }

        if (LooksLikeDvdChd(text))
        {
            return "DVD-ROM";
        }

        long? logicalBytes = TryParseLogicalBytes(text);
        if (logicalBytes.HasValue)
        {
            const long cdUpperBoundBytes = 900L * 1024L * 1024L;
            const long dvdLowerBoundBytes = 1_000_000_000L;

            if (logicalBytes.Value > 0 && logicalBytes.Value <= cdUpperBoundBytes)
            {
                return "CD-ROM";
            }

            if (logicalBytes.Value >= dvdLowerBoundBytes)
            {
                return "DVD-ROM";
            }
        }

        return null;
    }

    private static bool LooksLikeCdTrackChd(string text)
    {
        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0
                && ContainsAny(line, "mode1", "mode2", "audio", "2352", "2448", "pregap", "frames"))
            {
                return true;
            }

            if (ContainsAny(line, "cd-rom", "gd-rom", "chtr", "cht2", "chcd", "chgd", "avav", "avld"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeDvdChd(string text)
    {
        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (ContainsAny(line, "dvd-rom", "dvdrom", "dvdi", "dvdt"))
            {
                return true;
            }

            if (line.IndexOf("2048", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("sector", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("track", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        foreach (string value in values)
        {
            if (source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildLogsDirectory()
    {
        string path = AppPaths.LogsDirectory;
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "file" : value;
    }

    private static string NormalizePathForCli(string path) =>
        FilePathExclusiveGate.NormalizePathForExclusiveLock(path);
}