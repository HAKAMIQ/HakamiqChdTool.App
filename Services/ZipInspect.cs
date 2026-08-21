using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class SevenZipArchiveInspector
{
    private const long MaxDescriptorTextChars = 4L * 1024L * 1024L;

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<SevenZipArchiveInspector>();
    private static readonly TimeSpan DescriptorReadTimeout = TimeSpan.FromMinutes(2);

    internal sealed record SevenZipDescriptorTextResult(
        bool IsSuccess,
        bool WasCancelled,
        bool RequiresPassword,
        string MessageResourceKey,
        string Text);

    internal static async Task<SevenZipDescriptorTextResult> ReadDescriptorTextAsync(
        string sevenZipPath,
        string archivePath,
        string entryPath,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeReadableArchivePath(archivePath, out string fullArchivePath)
            || string.IsNullOrWhiteSpace(entryPath)
            || !IsSafeArchiveEntryPath(entryPath)
            || !ArchiveCandidateDiscovery.IsDescriptorLeaderPath(entryPath))
        {
            return new SevenZipDescriptorTextResult(
                false,
                false,
                false,
                ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey,
                string.Empty);
        }

        string safeEntryPath = NormalizeArchiveEntryPath(entryPath);

        using CancellationTokenSource readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeoutCts.CancelAfter(DescriptorReadTimeout);

        SevenZipProcessResult result;
        try
        {
            result = await SevenZipProcessRunner.RunAsync(
                sevenZipPath,
                ["e", "-so", "-y", "-bb0", "-bsp0", "-bse2", "--", fullArchivePath, safeEntryPath],
                parseProgressPercent: false,
                progress: null,
                readTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Debug(ex, "7-Zip descriptor read cancelled. Archive={Archive}, Entry={Entry}", fullArchivePath, safeEntryPath);
            return new SevenZipDescriptorTextResult(false, true, false, string.Empty, string.Empty);
        }
        catch (OperationCanceledException ex)
        {
            Logger.Debug(ex, "7-Zip descriptor read timed out. Archive={Archive}, Entry={Entry}", fullArchivePath, safeEntryPath);
            return new SevenZipDescriptorTextResult(false, false, false, "LocArchive_DescriptorReadTimeout", string.Empty);
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is InvalidOperationException)
        {
            Logger.Debug(ex, "7-Zip descriptor read failed before start. Archive={Archive}, Entry={Entry}", fullArchivePath, safeEntryPath);
            return new SevenZipDescriptorTextResult(
                false,
                false,
                false,
                ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey,
                string.Empty);
        }

        if (result.WasCancelled)
        {
            return cancellationToken.IsCancellationRequested
                ? new SevenZipDescriptorTextResult(false, true, false, string.Empty, string.Empty)
                : new SevenZipDescriptorTextResult(false, false, false, "LocArchive_DescriptorReadTimeout", string.Empty);
        }

        if (result.OutputLimitExceeded)
        {
            return new SevenZipDescriptorTextResult(
                false,
                false,
                false,
                ArchiveResourcePolicy.ResourceLimitMessageKey,
                string.Empty);
        }

        if (result.ExitCode != 0)
        {
            string output = result.CombinedOutput;
            bool requiresPassword = LooksPasswordProtected(output);

            Logger.Debug(
                "7-Zip descriptor read returned non-zero exit code. Archive={Archive}, Entry={Entry}, ExitCode={ExitCode}, Output={Output}",
                fullArchivePath,
                safeEntryPath,
                result.ExitCode,
                output);

            return new SevenZipDescriptorTextResult(
                false,
                false,
                requiresPassword,
                requiresPassword
                    ? "LocArchive_PasswordProtected"
                    : ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey,
                string.Empty);
        }

        string text = result.StandardOutput;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxDescriptorTextChars)
        {
            return new SevenZipDescriptorTextResult(
                false,
                false,
                false,
                ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey,
                string.Empty);
        }

        return new SevenZipDescriptorTextResult(true, false, false, string.Empty, text);
    }

    internal static List<string> ParseSevenZipListPaths(string output)
    {
        return
        [
            .. ParseSevenZipListEntries(output)
                .Select(entry => entry.Path)
        ];
    }

    internal sealed record SevenZipListEntry(
        string Path,
        long? Size,
        long? PackedSize,
        bool IsDirectory);

    internal static List<SevenZipListEntry> ParseSevenZipListEntries(string output)
    {
        var entries = new List<SevenZipListEntry>();

        if (string.IsNullOrWhiteSpace(output))
        {
            return entries;
        }

        using var reader = new StringReader(output);

        string? path = null;
        long? size = null;
        long? packedSize = null;
        bool isDirectory = false;

        void FlushEntry()
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                entries.Add(new SevenZipListEntry(path, size, packedSize, isDirectory));
            }

            path = null;
            size = null;
            packedSize = null;
            isDirectory = false;
        }

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushEntry();
                continue;
            }

            if (line.StartsWith("Path = ", StringComparison.Ordinal))
            {
                if (path is not null)
                {
                    FlushEntry();
                }

                path = line[7..].Trim();
                continue;
            }

            if (line.StartsWith("Size = ", StringComparison.Ordinal)
                && long.TryParse(line[7..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsedSize))
            {
                size = parsedSize;
                continue;
            }

            if (line.StartsWith("Packed Size = ", StringComparison.Ordinal)
                && long.TryParse(line[14..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsedPackedSize))
            {
                packedSize = parsedPackedSize;
                continue;
            }

            if (line.StartsWith("Folder = ", StringComparison.Ordinal))
            {
                isDirectory = string.Equals(line[9..].Trim(), "+", StringComparison.Ordinal);
            }
        }

        FlushEntry();
        return entries;
    }

    internal static bool LooksPasswordProtected(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)
            || output.Contains("password", StringComparison.OrdinalIgnoreCase)
            || output.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Can not open encrypted archive", StringComparison.OrdinalIgnoreCase);
    }


    private static bool IsSafeArchiveEntryPath(string path)
    {
        string trimmed = path.Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Contains('\0')
            || trimmed.StartsWith('@')
            || trimmed.StartsWith('/')
            || trimmed.StartsWith('\\')
            || Path.IsPathRooted(trimmed)
            || trimmed.Contains(':'))
        {
            return false;
        }

        string normalized = NormalizeArchiveEntryPath(trimmed);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0
            && segments.All(segment => !string.Equals(segment, ".", StringComparison.Ordinal)
                && !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static string NormalizeArchiveEntryPath(string value)
    {
        return value.Replace('\\', '/').Trim().Trim('/');
    }


    private static bool TryNormalizeReadableArchivePath(string archivePath, out string fullArchivePath)
    {
        fullArchivePath = string.Empty;

        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return false;
        }

        try
        {
            fullArchivePath = Path.GetFullPath(archivePath.Trim());

            if (!File.Exists(fullArchivePath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullArchivePath, nameof(archivePath));
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "7-Zip archive path could not be evaluated. Archive={Archive}", archivePath);
            fullArchivePath = string.Empty;
            return false;
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or System.Security.SecurityException;
}
