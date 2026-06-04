using Serilog;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed class SevenZipArchiveInspector
{
    private const long MaxDescriptorPreviewChars = 4L * 1024L * 1024L;

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<SevenZipArchiveInspector>();
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromMinutes(2);

    private readonly SevenZipToolService _toolService = SevenZipToolService.Instance;

    public bool IsAvailable => _toolService.IsAvailable;

    public async Task<ArchiveContentPreviewResult> PreviewAsync(
        string archivePath,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? listingCache = null,
        CancellationToken cancellationToken = default)
    {
        if (!_toolService.TryGetExecutablePath(out string sevenZipPath))
        {
            return new ArchiveContentPreviewResult
            {
                IsUnreadable = true,
                MessageResourceKey = "LocArchive_PreviewUnreadable"
            };
        }

        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return new ArchiveContentPreviewResult
            {
                IsUnreadable = true,
                MessageResourceKey = "LocArchive_PreviewUnreadable"
            };
        }

        string fullArchivePath;
        try
        {
            fullArchivePath = Path.GetFullPath(archivePath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "7-Zip archive preview rejected an invalid archive path. Archive={Archive}", archivePath);

            return new ArchiveContentPreviewResult
            {
                IsUnreadable = true,
                MessageResourceKey = "LocArchive_PreviewUnreadable"
            };
        }

        SevenZipProcessResult result;
        bool hasCacheKey = ArchiveInspectionCacheKey.TryCreate(fullArchivePath, out ArchiveInspectionCacheKey cacheKey);

        if (hasCacheKey && listingCache is not null && listingCache.TryGetValue(cacheKey, out SevenZipProcessResult? cachedResult))
        {
            result = cachedResult;
        }
        else
        {
            using CancellationTokenSource previewTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            previewTimeoutCts.CancelAfter(PreviewTimeout);

            try
            {
                result = await SevenZipProcessRunner.RunAsync(
                    sevenZipPath,
                    ["l", "-slt", "-ba", "--", fullArchivePath],
                    parseProgressPercent: false,
                    progress: null,
                    previewTimeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Logger.Debug(ex, "7-Zip archive preview cancelled before completion. Archive={Archive}", fullArchivePath);
                return ArchiveContentPreviewResult.Cancelled();
            }
            catch (OperationCanceledException ex)
            {
                Logger.Debug(ex, "7-Zip archive preview timed out. Archive={Archive}", fullArchivePath);
                result = new SevenZipProcessResult
                {
                    ExitCode = ChdmanProcessRunner.CanceledExitCode,
                    WasCancelled = true
                };
            }

            if (!cancellationToken.IsCancellationRequested && hasCacheKey && listingCache is not null)
            {
                listingCache[cacheKey] = result;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Logger.Debug("7-Zip archive preview cancelled. Archive={Archive}", fullArchivePath);
            return ArchiveContentPreviewResult.Cancelled();
        }

        if (result.WasCancelled)
        {
            Logger.Debug("7-Zip archive preview timed out or was cancelled by the preview token. Archive={Archive}", fullArchivePath);
            return new ArchiveContentPreviewResult
            {
                IsUnreadable = true,
                MessageResourceKey = "LocQueueAdd_ArchivePreviewTimeout"
            };
        }

        if (result.ExitCode != 0)
        {
            string combined = result.CombinedOutput;
            bool requiresPassword = LooksPasswordProtected(combined);

            Logger.Warning(
                "7-Zip archive preview failed. Archive={Archive}, ExitCode={ExitCode}, Output={Output}",
                fullArchivePath,
                result.ExitCode,
                combined);

            return new ArchiveContentPreviewResult
            {
                IsUnreadable = !requiresPassword,
                RequiresPassword = requiresPassword,
                MessageResourceKey = requiresPassword
                    ? "LocArchive_PasswordProtected"
                    : "LocArchive_PreviewUnreadable"
            };
        }

        List<string> entryPaths =
        [
            .. ParseSevenZipListPaths(result.StandardOutput)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => !LooksLikeDirectory(path))
                .Where(path => !IsArchiveSelfPath(path, fullArchivePath))
                .Where(IsSafeArchiveEntryPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        if (entryPaths.Count == 0)
        {
            return new ArchiveContentPreviewResult
            {
                MessageResourceKey = ArchiveCandidateDiscovery.EmptyArchiveMessageResourceKey
            };
        }

        int convertibleLeaderCount = ArchiveCandidateDiscovery.CountEffectiveConvertibleLeaderPaths(entryPaths);
        if (convertibleLeaderCount > 1)
        {
            return new ArchiveContentPreviewResult
            {
                MessageResourceKey = ArchiveCandidateDiscovery.MultipleConvertibleImageSetsMessageResourceKey
            };
        }

        string[] convertibleLeaders = ArchiveCandidateDiscovery.GetEffectiveConvertibleLeaderExtensions(entryPaths);
        if (convertibleLeaderCount == 1 && convertibleLeaders.Length > 0)
        {
            string? leaderPath = ArchiveCandidateDiscovery.FindFirstEffectiveConvertibleLeaderPath(entryPaths);
            if (string.IsNullOrWhiteSpace(leaderPath))
            {
                return new ArchiveContentPreviewResult
                {
                    IsUnreadable = true,
                    MessageResourceKey = "LocArchive_PreviewUnreadable"
                };
            }

            if (ArchiveCandidateDiscovery.IsDescriptorLeaderPath(leaderPath))
            {
                SevenZipDescriptorTextResult descriptor = await ReadDescriptorTextAsync(
                    sevenZipPath,
                    fullArchivePath,
                    leaderPath,
                    cancellationToken).ConfigureAwait(false);

                if (descriptor.WasCancelled)
                {
                    return ArchiveContentPreviewResult.Cancelled();
                }

                if (!descriptor.IsSuccess)
                {
                    return new ArchiveContentPreviewResult
                    {
                        IsUnreadable = !descriptor.RequiresPassword,
                        RequiresPassword = descriptor.RequiresPassword,
                        MessageResourceKey = descriptor.MessageResourceKey
                    };
                }

                ArchiveDescriptorDependencyValidationResult dependencyResult =
                    ArchiveCandidateDiscovery.AnalyzeDescriptorDependencies(
                        leaderPath,
                        descriptor.Text,
                        entryPaths);

                if (!dependencyResult.IsValid)
                {
                    return new ArchiveContentPreviewResult
                    {
                        MessageResourceKey = dependencyResult.MessageResourceKey
                    };
                }
            }

            return new ArchiveContentPreviewResult
            {
                CanUnpackThenConvert = true,
                MessageResourceKey = "LocArchive_WillUnpackThenConvert",
                ConvertibleLeaderExtensions = convertibleLeaders
            };
        }

        if (ArchiveCandidateDiscovery.HasUnsupportedDiscImagePath(entryPaths))
        {
            return new ArchiveContentPreviewResult
            {
                MessageResourceKey = ArchiveCandidateDiscovery.UnsupportedDiscImageMessageResourceKey
            };
        }

        bool containsOnlyChd = entryPaths.Count > 0
            && entryPaths.All(path => ArchiveCandidateDiscovery.IsChdExtension(Path.GetExtension(path)));

        return new ArchiveContentPreviewResult
        {
            ContainsOnlyChd = containsOnlyChd,
            MessageResourceKey = containsOnlyChd
                ? "LocArchive_ContainsOnlyChd"
                : "LocArchive_NoConvertibleDiscImage"
        };
    }

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
        if (string.IsNullOrWhiteSpace(entryPath) || !ArchiveCandidateDiscovery.IsDescriptorLeaderPath(entryPath))
        {
            return new SevenZipDescriptorTextResult(
                false,
                false,
                false,
                ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey,
                string.Empty);
        }

        using CancellationTokenSource readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeoutCts.CancelAfter(PreviewTimeout);

        SevenZipProcessResult result;
        try
        {
            result = await SevenZipProcessRunner.RunAsync(
                sevenZipPath,
                ["e", "-so", "-y", "-bb0", "-bsp0", "-bse2", "--", archivePath, entryPath],
                parseProgressPercent: false,
                progress: null,
                readTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Debug(ex, "7-Zip descriptor read cancelled. Archive={Archive}, Entry={Entry}", archivePath, entryPath);
            return new SevenZipDescriptorTextResult(false, true, false, string.Empty, string.Empty);
        }
        catch (OperationCanceledException ex)
        {
            Logger.Debug(ex, "7-Zip descriptor read timed out. Archive={Archive}, Entry={Entry}", archivePath, entryPath);
            return new SevenZipDescriptorTextResult(false, false, false, "LocQueueAdd_ArchivePreviewTimeout", string.Empty);
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || ex is InvalidOperationException)
        {
            Logger.Debug(ex, "7-Zip descriptor read failed before start. Archive={Archive}, Entry={Entry}", archivePath, entryPath);
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
                : new SevenZipDescriptorTextResult(false, false, false, "LocQueueAdd_ArchivePreviewTimeout", string.Empty);
        }

        if (result.ExitCode != 0)
        {
            string output = result.CombinedOutput;
            bool requiresPassword = LooksPasswordProtected(output);

            Logger.Debug(
                "7-Zip descriptor read returned non-zero exit code. Archive={Archive}, Entry={Entry}, ExitCode={ExitCode}, Output={Output}",
                archivePath,
                entryPath,
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
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxDescriptorPreviewChars)
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
        var paths = new List<string>();

        if (string.IsNullOrWhiteSpace(output))
        {
            return paths;
        }

        using var reader = new StringReader(output);

        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("Path = ", StringComparison.Ordinal))
            {
                continue;
            }

            string value = line[7..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                paths.Add(value);
            }
        }

        return paths;
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

    private static bool IsArchiveSelfPath(string path, string archivePath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(archivePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return string.Equals(path, archivePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsSafeArchiveEntryPath(string path)
    {
        string trimmed = path.Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.StartsWith('@')
            || trimmed.StartsWith('/')
            || trimmed.StartsWith('\\')
            || Path.IsPathRooted(trimmed)
            || trimmed.Contains(':'))
        {
            return false;
        }

        string normalized = trimmed.Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0
            && segments.All(segment => !string.Equals(segment, ".", StringComparison.Ordinal)
                && !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static bool LooksLikeDirectory(string path) =>
        path.EndsWith('/')
        || path.EndsWith('\\');

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;
}