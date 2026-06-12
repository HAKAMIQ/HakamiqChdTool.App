using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Safety;

public sealed class InputSafetyScanner
{
    private readonly SafetyPathPolicy _pathPolicy;
    private readonly ArchiveSafetyScanner _archiveScanner;
    private readonly DescriptorSafetyScanner _descriptorScanner;

    public InputSafetyScanner()
        : this(
            SafetyPathPolicy.Shared,
            new ArchiveSafetyScanner(),
            new DescriptorSafetyScanner())
    {
    }

    internal InputSafetyScanner(
        SafetyPathPolicy pathPolicy,
        ArchiveSafetyScanner archiveScanner,
        DescriptorSafetyScanner descriptorScanner)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _archiveScanner = archiveScanner ?? throw new ArgumentNullException(nameof(archiveScanner));
        _descriptorScanner = descriptorScanner ?? throw new ArgumentNullException(nameof(descriptorScanner));
    }

    public async Task<InputSafetyScanResult> ScanRawInputsAsync(
        IEnumerable<string> rawPaths,
        InputSafetyPolicy policy,
        CancellationToken cancellationToken,
        IDictionary<ArchiveInspectionCacheKey, InputSafetyScanResult>? archiveScanCache = null,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache = null)
    {
        ArgumentNullException.ThrowIfNull(rawPaths);

        return await ScanPathsAsync(
                rawPaths,
                policy,
                archiveScanCache,
                sevenZipListingCache,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<InputSafetyScanResult> ScanBatchAsync(
        IEnumerable<string> paths,
        InputSafetyPolicy policy,
        CancellationToken cancellationToken,
        IDictionary<ArchiveInspectionCacheKey, InputSafetyScanResult>? archiveScanCache = null,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return await ScanPathsAsync(
                paths,
                policy,
                archiveScanCache,
                sevenZipListingCache,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InputSafetyScanResult> ScanPathsAsync(
        IEnumerable<string> paths,
        InputSafetyPolicy policy,
        IDictionary<ArchiveInspectionCacheKey, InputSafetyScanResult>? archiveScanCache,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var results = new List<InputSafetyScanResult>();

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            InputSafetyScanResult result = await ScanSinglePathAsync(
                    path,
                    policy,
                    archiveScanCache,
                    sevenZipListingCache,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.HasFindings)
            {
                results.Add(result);
            }
        }

        return InputSafetyScanResult.Merge([.. results]);
    }

    private async Task<InputSafetyScanResult> ScanSinglePathAsync(
        string path,
        InputSafetyPolicy policy,
        IDictionary<ArchiveInspectionCacheKey, InputSafetyScanResult>? archiveScanCache,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache,
        CancellationToken cancellationToken)
    {
        if (!_pathPolicy.TryGetExistingFilePath(path, out string fullPath))
        {
            return InputSafetyScanResult.Empty;
        }

        QueueInputClassification classification = QueueInputClassifier.Classify(fullPath);
        string extension = Path.GetExtension(fullPath);

        if (classification.IsArchiveContainer)
        {
            return await _archiveScanner.ScanAsync(
                    fullPath,
                    policy,
                    cancellationToken,
                    archiveScanCache,
                    sevenZipListingCache)
                .ConfigureAwait(false);
        }

        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".toc", StringComparison.OrdinalIgnoreCase))
        {
            return await _descriptorScanner.ScanAsync(fullPath, policy, cancellationToken)
                .ConfigureAwait(false);
        }

        return InputSafetyScanResult.Empty;
    }
}
