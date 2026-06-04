using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Safety;

public sealed class ArchiveSafetyScanner
{
    private static readonly TimeSpan SevenZipListingTimeout = TimeSpan.FromSeconds(12);

    private readonly ILogger _logger;
    private readonly SafetyPathPolicy _pathPolicy;

    public ArchiveSafetyScanner()
        : this(Log.ForContext<ArchiveSafetyScanner>(), SafetyPathPolicy.Shared)
    {
    }

    internal ArchiveSafetyScanner(ILogger logger, SafetyPathPolicy pathPolicy)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
    }

    public async Task<InputSafetyScanResult> ScanAsync(
        string archivePath,
        InputSafetyPolicy policy,
        CancellationToken cancellationToken,
        IDictionary<ArchiveInspectionCacheKey, InputSafetyScanResult>? archiveScanCache = null,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!_pathPolicy.TryGetExistingFilePath(archivePath, out string fullArchivePath))
        {
            return InputSafetyScanResult.Empty;
        }

        if (ArchiveInspectionCacheKey.TryCreate(fullArchivePath, out ArchiveInspectionCacheKey cacheKey)
            && archiveScanCache is not null
            && archiveScanCache.TryGetValue(cacheKey, out InputSafetyScanResult? cached))
        {
            return cached;
        }

        if (ShouldUseSevenZipListingFallback(fullArchivePath))
        {
            InputSafetyScanResult sevenZipOnlyResult = await ScanWithSevenZipListingAsync(
                    fullArchivePath,
                    policy,
                    sevenZipListingCache,
                    cancellationToken)
                .ConfigureAwait(false);

            CacheResult(fullArchivePath, sevenZipOnlyResult, archiveScanCache);
            return sevenZipOnlyResult;
        }

        InputSafetyScanResult sharpCompressResult = await ScanWithSharpCompressAsync(
                fullArchivePath,
                policy,
                cancellationToken)
            .ConfigureAwait(false);

        CacheResult(fullArchivePath, sharpCompressResult, archiveScanCache);
        return sharpCompressResult;
    }

    private Task<InputSafetyScanResult> ScanWithSharpCompressAsync(
        string archivePath,
        InputSafetyPolicy policy,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => ScanWithSharpCompressCore(archivePath, policy, cancellationToken),
            cancellationToken);
    }

    private InputSafetyScanResult ScanWithSharpCompressCore(
        string archivePath,
        InputSafetyPolicy policy,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<SuspiciousArtifact>();
        var budget = new SafetyScanBudget(policy);

        ReaderOptions readerOptions = new()
        {
            LookForHeader = true,
            ArchiveEncoding = new ArchiveEncoding()
        };

        try
        {
            if (!_pathPolicy.TryGetExistingFilePath(archivePath, out string fullArchivePath))
            {
                return InputSafetyScanResult.Empty;
            }

            using IArchive archive = ArchiveFactory.OpenArchive(new FileInfo(fullArchivePath), readerOptions);

            foreach (IArchiveEntry entry in archive.Entries.Where(static entry => !entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!budget.TryAcceptFile())
                {
                    _logger.Debug(
                        "Archive safety scan entry limit reached. Archive={Archive}; AcceptedEntries={AcceptedEntries}",
                        fullArchivePath,
                        budget.AcceptedFiles);
                    break;
                }

                string entryPath = entry.Key ?? string.Empty;
                AddEntryNameFindingIfNeeded(fullArchivePath, entryPath, artifacts);

                if (artifacts.Count >= policy.MaxArtifacts)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedArchiveException(ex))
        {
            _logger.Debug(ex, "Archive safety scan could not read archive entries with SharpCompress. Archive={Archive}", archivePath);
        }

        return InputSafetyScanResult.FromArtifacts(artifacts);
    }

    private async Task<InputSafetyScanResult> ScanWithSevenZipListingAsync(
        string archivePath,
        InputSafetyPolicy policy,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache,
        CancellationToken cancellationToken)
    {
        if (!SevenZipToolService.Instance.TryGetExecutablePath(out string sevenZipPath))
        {
            return InputSafetyScanResult.Empty;
        }

        if (!_pathPolicy.TryGetExistingFilePath(archivePath, out string fullArchivePath))
        {
            return InputSafetyScanResult.Empty;
        }

        SevenZipProcessResult result;
        bool hasCacheKey = ArchiveInspectionCacheKey.TryCreate(fullArchivePath, out ArchiveInspectionCacheKey cacheKey);

        if (hasCacheKey
            && sevenZipListingCache is not null
            && sevenZipListingCache.TryGetValue(cacheKey, out SevenZipProcessResult? cachedResult))
        {
            result = cachedResult;
        }
        else
        {
            using CancellationTokenSource listingTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listingTimeoutCts.CancelAfter(SevenZipListingTimeout);

            try
            {
                result = await SevenZipProcessRunner.RunAsync(
                        sevenZipPath,
                        ["l", "-slt", "-ba", "--", fullArchivePath],
                        parseProgressPercent: false,
                        progress: null,
                        listingTimeoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                _logger.Debug(ex, "Archive safety scan 7-Zip listing timed out. Archive={Archive}", fullArchivePath);
                result = new SevenZipProcessResult
                {
                    ExitCode = ChdmanProcessRunner.CanceledExitCode,
                    WasCancelled = true
                };
            }
            catch (Exception ex) when (IsExpectedArchiveException(ex))
            {
                _logger.Debug(ex, "Archive safety scan 7-Zip listing failed. Archive={Archive}", fullArchivePath);
                return InputSafetyScanResult.Empty;
            }

            if (!cancellationToken.IsCancellationRequested && hasCacheKey && sevenZipListingCache is not null)
            {
                sevenZipListingCache[cacheKey] = result;
            }
        }

        if (result.WasCancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Debug("Archive safety scan 7-Zip listing timed out. Archive={Archive}", fullArchivePath);
            return InputSafetyScanResult.Empty;
        }

        if (result.ExitCode != 0)
        {
            return InputSafetyScanResult.Empty;
        }

        var artifacts = new List<SuspiciousArtifact>();
        var budget = new SafetyScanBudget(policy);

        foreach (string entryPath in ParseSevenZipListPaths(result.StandardOutput))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!budget.TryAcceptFile())
            {
                _logger.Debug(
                    "Archive safety scan 7-Zip entry limit reached. Archive={Archive}; AcceptedEntries={AcceptedEntries}",
                    fullArchivePath,
                    budget.AcceptedFiles);
                break;
            }

            AddEntryNameFindingIfNeeded(fullArchivePath, entryPath, artifacts);
            if (artifacts.Count >= policy.MaxArtifacts)
            {
                break;
            }
        }

        return InputSafetyScanResult.FromArtifacts(artifacts);
    }

    private static void CacheResult(
        string archivePath,
        InputSafetyScanResult result,
        IDictionary<ArchiveInspectionCacheKey, InputSafetyScanResult>? archiveScanCache)
    {
        if (archiveScanCache is null || result is null)
        {
            return;
        }

        if (ArchiveInspectionCacheKey.TryCreate(archivePath, out ArchiveInspectionCacheKey cacheKey))
        {
            archiveScanCache[cacheKey] = result;
        }
    }

    private static void AddEntryNameFindingIfNeeded(
        string archivePath,
        string entryPath,
        List<SuspiciousArtifact> artifacts)
    {
        if (string.IsNullOrWhiteSpace(entryPath) || LooksLikeDirectory(entryPath))
        {
            return;
        }

        if (!IsSafeArchiveEntryPath(entryPath))
        {
            artifacts.Add(new SuspiciousArtifact(
                archivePath,
                entryPath,
                SuspiciousArtifactKind.UnsafeArchiveEntryPath,
                QueueIntakeAdvisorySeverity.Blocker,
                "LocInputSafety_UnsafeArchiveEntryPath"));
        }
    }

    private static bool IsSafeArchiveEntryPath(string path)
    {
        string raw = path.Trim();
        string normalized = raw.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('\0')
            || normalized.StartsWith('@')
            || normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || raw.StartsWith("\\\\", StringComparison.Ordinal)
            || Path.IsPathRooted(raw)
            || normalized.Contains(':'))
        {
            return false;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0
            && segments.All(static segment => !string.Equals(segment, ".", StringComparison.Ordinal)
                && !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ParseSevenZipListPaths(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
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
                yield return value;
            }
        }
    }

    private static bool ShouldUseSevenZipListingFallback(string archivePath)
    {
        string extension = Path.GetExtension(archivePath);
        return extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDirectory(string path) =>
        path.EndsWith('/')
        || path.EndsWith('\\');

    private static bool IsExpectedArchiveException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidDataException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }
}