using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private readonly RedumpLocalLibraryIndexer _redumpLocalLibraryIndexer = new();

    private async Task ScanAndIndexRedumpLocalLibraryAsync()
    {
        await ScanRedumpLocalLibraryAsync().ConfigureAwait(true);
        await AppendRedumpLocalLibraryIndexReportAsync().ConfigureAwait(true);
    }

    private async Task AppendRedumpLocalLibraryIndexReportAsync()
    {
        string root = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;
        if (!TryGetSafeOptionsDirectory(root, out string safeRoot))
        {
            return;
        }

        string scanSummary = RedumpLocalLibraryScanSummary;
        if (string.IsNullOrWhiteSpace(scanSummary))
        {
            return;
        }

        try
        {
            RedumpLocalLibraryIndexResult indexResult = await _redumpLocalLibraryIndexer
                .IndexAsync(safeRoot, CancellationToken.None)
                .ConfigureAwait(true);

            if (indexResult.TotalDatXmlFiles <= 0)
            {
                return;
            }

            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderIndexAppendFormat",
                scanSummary,
                FormatInvariantNumber(indexResult.PlatformCount),
                FormatInvariantNumber(indexResult.SelectedCount),
                FormatInvariantNumber(indexResult.OlderCount),
                FormatInvariantNumber(indexResult.DuplicateCount),
                FormatInvariantNumber(indexResult.VariantCount),
                indexResult.ReadErrorCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderIndexErrorAppendFormat",
                scanSummary,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    private async Task PrepareRedumpLocalLibraryDatabaseAsync()
    {
        string root = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;
        if (!TryGetSafeOptionsDirectory(root, out string safeRoot))
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
            return;
        }

        IsRedumpLocalLibraryScanRunning = true;
        RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanRunning");

        try
        {
            RedumpLocalLibraryScanResult scanResult = await _redumpLocalLibraryScanner
                .ScanAsync(safeRoot, CancellationToken.None)
                .ConfigureAwait(true);

            if (!scanResult.HasImportableDatFiles)
            {
                RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
                return;
            }

            string scanSummary = BuildRedumpLocalLibraryScanSummary(scanResult);
            RedumpLocalLibraryScanSummary = scanSummary;

            RedumpLocalLibraryIndexResult indexResult = await _redumpLocalLibraryIndexer
                .IndexAsync(safeRoot, CancellationToken.None)
                .ConfigureAwait(true);

            if (indexResult.TotalDatXmlFiles <= 0)
            {
                return;
            }

            if (indexResult.SelectedCount <= 0)
            {
                RedumpLocalLibraryScanSummary = ArabicUi.Format(
                    "LocRedumpSettings_LocalFolderIndexAppendFormat",
                    scanSummary,
                    FormatInvariantNumber(indexResult.PlatformCount),
                    FormatInvariantNumber(indexResult.SelectedCount),
                    FormatInvariantNumber(indexResult.OlderCount),
                    FormatInvariantNumber(indexResult.DuplicateCount),
                    FormatInvariantNumber(indexResult.VariantCount),
                    indexResult.ReadErrorCount);

                return;
            }

            RedumpLocalLibraryImportSummary importSummary = await ImportSelectedRedumpLocalDatFilesAsync(
                    indexResult,
                    CancellationToken.None)
                .ConfigureAwait(true);

            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderIndexImportAppendFormat",
                scanSummary,
                FormatInvariantNumber(indexResult.PlatformCount),
                FormatInvariantNumber(indexResult.SelectedCount),
                FormatInvariantNumber(indexResult.OlderCount),
                FormatInvariantNumber(indexResult.DuplicateCount),
                FormatInvariantNumber(indexResult.VariantCount),
                FormatInvariantNumber(indexResult.ReadErrorCount),
                FormatInvariantNumber(importSummary.ImportedFileCount),
                FormatInvariantNumber(importSummary.ImportedRows),
                FormatInvariantNumber(importSummary.FailedFileCount));

            RefreshRedumpDatabaseStatusAfterLocalImport();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderIndexErrorAppendFormat",
                RedumpLocalLibraryScanSummary,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
        finally
        {
            IsRedumpLocalLibraryScanRunning = false;
        }
    }

    private void RefreshRedumpDatabaseStatusAfterLocalImport()
    {
        bool isAvailable = RedumpSqliteManager.Default.HasAnyRows();

        IsDatabaseAvailable = isAvailable;
        DatabaseStatusText = ArabicUi.Get(isAvailable
            ? "LocOptions_DatabaseAvailable"
            : "LocOptions_DatabaseMissing");

        if (isAvailable)
        {
            SetDatabaseLastSyncedUtc(System.DateTimeOffset.UtcNow.ToString(
                "O",
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static string FormatInvariantNumber(int value)
    {
        return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatInvariantNumber(long value)
    {
        return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatInvariantNumber(string value)
    {
        return value;
    }

    private static string BuildRedumpLocalLibraryScanSummary(RedumpLocalLibraryScanResult result)
    {
        string newest = result.NewestModifiedLocal.HasValue
            ? result.NewestModifiedLocal.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : "—";

        return ArabicUi.Format(
            "LocRedumpSettings_LocalFolderScanReadyFormat",
            result.DatXmlFileCount,
            result.CueFileCount,
            result.GdiFileCount,
            result.SubchannelFileCount,
            result.DiscKeyFileCount,
            result.TopLevelFolderCount,
            newest);
    }

    private async Task<RedumpLocalLibraryImportSummary> ImportSelectedRedumpLocalDatFilesAsync(
        RedumpLocalLibraryIndexResult indexResult,
        CancellationToken cancellationToken)
    {
        RedumpLocalLibraryDatEntry[] selectedEntries = indexResult.Entries
            .Where(entry => entry.IsSelected)
            .Where(entry => !entry.Status.Equals(RedumpLocalLibraryIndexer.ReadErrorStatus, StringComparison.OrdinalIgnoreCase))
            .Where(entry => TryGetSafeOptionsFile(entry.FilePath, out _))
            .ToArray();

        if (selectedEntries.Length == 0)
        {
            return new RedumpLocalLibraryImportSummary(0, 0, 0);
        }

        RedumpLocalLibraryScanSummary = ArabicUi.Format(
            "LocRedumpSettings_LocalFolderImportProgressFormat",
            FormatInvariantNumber(0),
            FormatInvariantNumber(selectedEntries.Length),
            FormatInvariantNumber(0),
            FormatInvariantNumber(0));

        Progress<RedumpImportProgress> progress = new(progressValue =>
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderImportProgressFormat",
                FormatInvariantNumber(selectedEntries.Length),
                FormatInvariantNumber(selectedEntries.Length),
                FormatInvariantNumber(0),
                FormatInvariantNumber(progressValue.RowsInserted));
        });

        RedumpImportResult result = await RedumpSqliteManager.Default
            .CleanRebuildFromDatFilesAsync(selectedEntries, progress, cancellationToken)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            return new RedumpLocalLibraryImportSummary(
                0,
                0,
                selectedEntries.Length);
        }

        return new RedumpLocalLibraryImportSummary(
            selectedEntries.Length,
            Math.Max(0, result.RowsImported),
            0);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "Redump";
    }

    private readonly record struct RedumpLocalLibraryImportSummary(
        int ImportedFileCount,
        int ImportedRows,
        int FailedFileCount);
}