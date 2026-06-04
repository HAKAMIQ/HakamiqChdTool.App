using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Services;
using System;
using System.IO;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class AdvancedOptionsViewModel
{
    private IAdvancedOptionsPickerService? _advancedOptionsPickerService;
    private IRelayCommand? _browseOutputFolderCommand;
    private IRelayCommand? _browseExternalChdmanCommand;
    private IRelayCommand? _browseRedumpDatCommand;
    private IRelayCommand? _browsePendingWorkspaceCommand;

    internal IAdvancedOptionsPickerService AdvancedOptionsPickerService
    {
        get => _advancedOptionsPickerService ??= new WpfAdvancedOptionsPickerService();
        set => _advancedOptionsPickerService = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IRelayCommand BrowseOutputFolderCommand =>
        _browseOutputFolderCommand ??= new RelayCommand(BrowseOutputFolder);

    public IRelayCommand BrowseExternalChdmanCommand =>
        _browseExternalChdmanCommand ??= new RelayCommand(BrowseExternalChdman);

    public IRelayCommand BrowseRedumpDatCommand =>
        _browseRedumpDatCommand ??= new RelayCommand(BrowseRedumpDat);

    public IRelayCommand BrowsePendingWorkspaceCommand =>
        _browsePendingWorkspaceCommand ??= new RelayCommand(BrowsePendingWorkspace);

    private void BrowseOutputFolder()
    {
        string current = CustomOutputRoot?.Trim() ?? string.Empty;
        string? selected = AdvancedOptionsPickerService.PickFolder(
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
        string? selected = AdvancedOptionsPickerService.PickFile(
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

        string? selected = AdvancedOptionsPickerService.PickFolder(
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
        string? selected = AdvancedOptionsPickerService.PickFile(
            "LocAdv_Picker_SelectRedumpDatTitle",
            "LocFilter_RedumpDatXmlFiles",
            File.Exists(current) ? current : null,
            fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            RedumpDatXmlPath = selected;
        }
    }
}