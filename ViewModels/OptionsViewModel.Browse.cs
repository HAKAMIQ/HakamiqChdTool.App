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
    private IOptionsPickerService? _optionsPickerService;
    private IRelayCommand? _browseOutputFolderCommand;
    private IRelayCommand? _browseExternalChdmanCommand;
    private IRelayCommand? _browseRedumpDatCommand;
    private IRelayCommand? _browseRedumpLocalLibraryCommand;
    private AsyncRelayCommand? _scanRedumpLocalLibraryCommand;
    private AsyncRelayCommand? _importRedumpLocalLibraryCommand;
    private IRelayCommand? _browsePendingWorkspaceCommand;
    private readonly RedumpLocalLibraryScanner _redumpLocalLibraryScanner = new();

    internal IOptionsPickerService OptionsPickerService
    {
        get => _optionsPickerService ??= new OptionsPickerService();
        set => _optionsPickerService = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IRelayCommand BrowseOutputFolderCommand =>
        _browseOutputFolderCommand ??= new RelayCommand(BrowseOutputFolder);

    public IRelayCommand BrowseExternalChdmanCommand =>
        _browseExternalChdmanCommand ??= new RelayCommand(BrowseExternalChdman);

    public IRelayCommand BrowseRedumpDatCommand =>
        _browseRedumpDatCommand ??= new RelayCommand(BrowseRedumpDat);

    public IRelayCommand BrowseRedumpLocalLibraryCommand =>
        _browseRedumpLocalLibraryCommand ??= new RelayCommand(BrowseRedumpLocalLibrary);

    public IAsyncRelayCommand ScanRedumpLocalLibraryCommand =>
        _scanRedumpLocalLibraryCommand ??= new AsyncRelayCommand(
            ScanAndIndexRedumpLocalLibraryAsync,
            CanScanRedumpLocalLibrary);

    public IAsyncRelayCommand ImportRedumpLocalLibraryCommand =>
        _importRedumpLocalLibraryCommand ??= new AsyncRelayCommand(
            PrepareRedumpLocalLibraryDatabaseAsync,
            CanScanRedumpLocalLibrary);
    public IRelayCommand BrowsePendingWorkspaceCommand =>
        _browsePendingWorkspaceCommand ??= new RelayCommand(BrowsePendingWorkspace);

    private void BrowseOutputFolder()
    {
        string current = CustomOutputRoot?.Trim() ?? string.Empty;
        string? selected = OptionsPickerService.PickFolder(
            "LocAdv_Picker_SelectOutputFolderTitle",
            Directory.Exists(current) ? current : null);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            CustomOutputRoot = selected;
        }
    }

    private void BrowseExternalChdman()
    {
        string current = ExternalChdmanPath?.Trim() ?? string.Empty;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? selected = OptionsPickerService.PickFile(
            "LocAdv_Picker_SelectExternalChdmanTitle",
            "LocFilter_ExecutableFiles",
            File.Exists(current) ? current : null,
            fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            ExternalChdmanPath = selected;
            UseBundledChdman = false;
            UseExternalChdman = true;
        }
    }

    private void BrowsePendingWorkspace()
    {
        string current = PendingWorkspaceCustomRoot?.Trim() ?? string.Empty;
        string fallback = Path.GetTempPath();

        string? selected = OptionsPickerService.PickFolder(
            "LocAdv_Picker_SelectPendingWorkspaceTitle",
            Directory.Exists(current) ? current : fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            PendingWorkspaceCustomRoot = selected;
            UseCustomPendingWorkspace = true;
        }
    }

    private void BrowseRedumpDat()
    {
        string current = RedumpDatXmlPath?.Trim() ?? string.Empty;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string? selected = OptionsPickerService.PickFile(
            "LocAdv_Picker_SelectRedumpDatTitle",
            "LocFilter_RedumpDatXmlFiles",
            File.Exists(current) ? current : null,
            fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            RedumpDatXmlPath = selected;
        }
    }

    private void BrowseRedumpLocalLibrary()
    {
        string current = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string? selected = OptionsPickerService.PickFolder(
            "LocAdv_Picker_SelectRedumpLocalFolderTitle",
            Directory.Exists(current) ? current : fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            RedumpLocalLibraryRoot = selected;
            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderSelectedFormat",
                RedumpCompactDisplayFormatter.FormatRoot(selected));
        }
    }

    private bool CanScanRedumpLocalLibrary()
    {
        string root = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;

        return CanUseRedumpDatabaseImport
            && !IsRedumpLocalLibraryScanRunning
            && Directory.Exists(root);
    }

    private async Task ScanRedumpLocalLibraryAsync()
    {
        string root = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;

        if (!Directory.Exists(root))
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
            return;
        }

        IsRedumpLocalLibraryScanRunning = true;
        RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanRunning");

        try
        {
            RedumpLocalLibraryScanResult result = await _redumpLocalLibraryScanner
                .ScanAsync(root, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.HasImportableDatFiles)
            {
                RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
                return;
            }

            string newest = result.NewestModifiedLocal.HasValue
                ? result.NewestModifiedLocal.Value.ToString("yyyy-MM-dd HH:mm")
                : "—";

            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderScanReadyFormat",
                result.DatXmlFileCount,
                result.CueFileCount,
                result.GdiFileCount,
                result.SubchannelFileCount,
                result.DiscKeyFileCount,
                result.TopLevelFolderCount,
                newest);
        }
        catch (Exception ex)
        {
            RedumpLocalLibraryScanSummary = RuntimeDiagnosticFormatter.SummarizeException(ex);
        }
        finally
        {
            IsRedumpLocalLibraryScanRunning = false;
        }
    }

    partial void NotifyRedumpLocalLibraryScanCommandState()
    {
        _scanRedumpLocalLibraryCommand?.NotifyCanExecuteChanged();
        _importRedumpLocalLibraryCommand?.NotifyCanExecuteChanged();
    }
}
