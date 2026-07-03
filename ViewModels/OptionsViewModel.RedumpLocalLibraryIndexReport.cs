using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
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
}
