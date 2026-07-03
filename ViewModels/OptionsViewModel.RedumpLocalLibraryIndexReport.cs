using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
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
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
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
                .IndexAsync(root, CancellationToken.None)
                .ConfigureAwait(true);

            if (indexResult.TotalDatXmlFiles <= 0)
            {
                return;
            }

            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderIndexAppendFormat",
                scanSummary,
                indexResult.PlatformCount,
                indexResult.SelectedCount,
                indexResult.OlderCount,
                indexResult.DuplicateCount,
                indexResult.VariantCount,
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
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
            return;
        }

        IsRedumpLocalLibraryScanRunning = true;
        RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanRunning");

        try
        {
            RedumpLocalLibraryScanResult scanResult = await _redumpLocalLibraryScanner
                .ScanAsync(root, CancellationToken.None)
                .ConfigureAwait(true);

            if (!scanResult.HasImportableDatFiles)
            {
                RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
                return;
            }

            string scanSummary = BuildRedumpLocalLibraryScanSummary(scanResult);
            RedumpLocalLibraryScanSummary = scanSummary;

            RedumpLocalLibraryIndexResult indexResult = await _redumpLocalLibraryIndexer
                .IndexAsync(root, CancellationToken.None)
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
                    indexResult.PlatformCount,
                    indexResult.SelectedCount,
                    indexResult.OlderCount,
                    indexResult.DuplicateCount,
                    indexResult.VariantCount,
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
                indexResult.PlatformCount,
                indexResult.SelectedCount,
                indexResult.OlderCount,
                indexResult.DuplicateCount,
                indexResult.VariantCount,
                indexResult.ReadErrorCount,
                importSummary.ImportedFileCount,
                importSummary.ImportedRows,
                importSummary.FailedFileCount);

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

    private static string BuildRedumpLocalLibraryScanSummary(RedumpLocalLibraryScanResult result)
    {
        string newest = result.NewestModifiedLocal.HasValue
            ? result.NewestModifiedLocal.Value.ToString("yyyy-MM-dd HH:mm")
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
            .Where(entry => File.Exists(entry.FilePath))
            .ToArray();

        if (selectedEntries.Length == 0)
        {
            return new RedumpLocalLibraryImportSummary(0, 0, 0);
        }

        int importedFileCount = 0;
        int failedFileCount = 0;
        int importedRows = 0;

        for (int index = 0; index < selectedEntries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RedumpLocalLibraryDatEntry entry = selectedEntries[index];

            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderImportProgressFormat",
                index + 1,
                selectedEntries.Length,
                importedFileCount,
                importedRows);

            string systemName = FirstNonEmpty(
                entry.Name,
                entry.Description,
                RedumpCompactDisplayFormatter.FormatFileName(entry.FileName));

            RedumpImportResult result = await RedumpSqliteManager.Default
                .ImportDatFileAsync(entry.FilePath, systemName, progress: null, cancellationToken)
                .ConfigureAwait(true);

            if (result.Success)
            {
                importedFileCount++;
                importedRows += Math.Max(0, result.RowsImported);
            }
            else
            {
                failedFileCount++;
            }
        }

        return new RedumpLocalLibraryImportSummary(
            importedFileCount,
            importedRows,
            failedFileCount);
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
