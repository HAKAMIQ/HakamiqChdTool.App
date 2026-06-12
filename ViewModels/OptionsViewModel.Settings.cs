using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    public void Load(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _useProgramDefaultsAsBase = false;
        ApplySettingsValues(settings);
        AcceptAppliedSettings(BuildResultSettings(settings));
    }

    public void ApplyProgramDefaults()
    {
        _useProgramDefaultsAsBase = true;
        ApplySettingsValues(AppSettings.CreateSafeDefaults());
        NotifySaveStateChanged();
    }

    private void ApplySettingsValues(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Theme = settings.Theme;
        UiLanguage = AppLanguageService.NormalizeLanguageName(settings.UiLanguage);
        UseCustomOutputRoot = settings.UseCustomOutputRoot;
        CustomOutputRoot = settings.CustomOutputRoot;
        UseCustomPendingWorkspace = settings.UseCustomPendingWorkspace || settings.PendingWorkspaceMode == PendingWorkspaceMode.Custom;
        PendingWorkspaceCustomRoot = settings.PendingWorkspaceCustomRoot?.Trim() ?? string.Empty;
        RedumpDatXmlPath = settings.RedumpDatXmlPath;
        RedumpSystemName = settings.RedumpSystemName;
        SelectedRedumpPlatformOption = ResolveRedumpPlatformOption(settings.RedumpPlatformMode, settings.RedumpPlatformKey);
        SelectedRedumpArtifactOption = ResolveRedumpArtifactOption(settings.RedumpArtifactKind);
        OrganizeByPlatform = settings.OrganizeByPlatform;
        OrganizeByRegion = settings.OrganizeByRegion;
        IncludeSubfolders = settings.IncludeSubfolders;
        ShowStorageAdvisorDialog = !settings.SuppressStorageAdvisorDialog;
        UseBundledChdman = settings.UseBundledChdman;
        ExternalChdmanPath = settings.ExternalChdmanPath;
        PortableMode = settings.PortableMode;
        VerifyAfterConversion = settings.VerifyAfterConversion;
        SkipExistingOutput = settings.SkipExistingOutput;
        CopyMatchingSbi = settings.CopyMatchingSbi;
        EnableAutoM3uGeneration = settings.EnableAutoM3uGeneration;
        OverwriteExistingM3uPlaylists = settings.OverwriteExistingM3uPlaylists;
        DeleteTemporaryExtraction = settings.DeleteTemporaryExtraction;
        DeleteFailedOutput = settings.DeleteFailedOutput;
        DeleteSourceAfterVerifiedConversion = settings.DeleteSourceAfterVerifiedConversion;
        DeleteSourceAfterVerifiedExtraction = settings.DeleteSourceAfterVerifiedExtraction;
        RedumpDatabaseDownloadUrl = settings.RedumpDatabaseDownloadUrl?.Trim() ?? string.Empty;
        EnableRedumpAutoSync = settings.EnableRedumpAutoSync;
        _redumpLastSyncedUtc = NormalizeStoredTimestamp(settings.RedumpLastSyncedUtc);
        OnPropertyChanged(nameof(DatabaseLastSyncedDisplay));
        SelectedProcessorOption = ProcessorOptions.FirstOrDefault(x => x.Value == settings.MaxProcessorCount) ?? ProcessorOptions.FirstOrDefault();
        int maxConcurrentConversions = AppSettings.NormalizeMaxConcurrentConversions(settings.MaxConcurrentConversions);
        SelectedConcurrentConversionOption = ConcurrentConversionOptions.FirstOrDefault(x => x.Value == maxConcurrentConversions)
            ?? ConcurrentConversionOptions.FirstOrDefault();
        SelectedPerformanceMode = ResolvePerformanceMode(settings.PerformanceMode);
        SelectedPriorityMode = ResolvePriorityMode(settings.ChdmanPriorityMode);
        SelectedCompressionPreset = ResolveCompressionPreset(settings.CompressionCodecs);
        SelectedHunkPreset = ResolveHunkPreset(settings.HunkSizeBytes);
        SelectedIsoCreateOverride = ResolveIsoCreateOverride(settings.IsoCreateCommandOverride);
        EnableDeepIntegrityCheck = settings.EnableDeepIntegrityCheck;
        ApplyStandardNamingBasedOnHash = settings.ApplyStandardNamingBasedOnHash;

        ValidateAllProperties();
        ValidateForSave();
    }

    public AppSettings BuildResultSettings(AppSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ValidateAllProperties();
        ValidateForSave();

        AppSettings result = _useProgramDefaultsAsBase
            ? AppSettings.CreateSafeDefaults()
            : source.Clone();
        result.UseCustomOutputRoot = UseCustomOutputRoot;
        result.CustomOutputRoot = CustomOutputRoot.Trim();
        result.PendingWorkspaceMode = UseCustomPendingWorkspace ? PendingWorkspaceMode.Custom : PendingWorkspaceMode.Automatic;
        result.PendingWorkspaceCustomRoot = PendingWorkspaceCustomRoot.Trim();
        result.UseCustomPendingWorkspace = UseCustomPendingWorkspace;
        result.RedumpDatXmlPath = RedumpDatXmlPath.Trim();
        result.RedumpSystemName = RedumpSystemName.Trim();
        result.RedumpPlatformMode = SelectedRedumpPlatformOption is null
            ? "Auto"
            : string.Equals(SelectedRedumpPlatformOption.Key, "Auto", StringComparison.OrdinalIgnoreCase)
                ? "Auto"
                : string.Equals(SelectedRedumpPlatformOption.Key, "Unknown", StringComparison.OrdinalIgnoreCase)
                    ? "Unknown"
                    : "Platform";
        result.RedumpPlatformKey = result.RedumpPlatformMode == "Platform" ? SelectedRedumpPlatformOption?.Key ?? string.Empty : string.Empty;
        result.RedumpArtifactKind = SelectedRedumpArtifactOption?.Key ?? "Datfile";
        result.OrganizeByPlatform = OrganizeByPlatform;
        result.OrganizeByRegion = OrganizeByRegion;
        result.IncludeSubfolders = IncludeSubfolders;
        result.SuppressStorageAdvisorDialog = !ShowStorageAdvisorDialog;
        result.UseBundledChdman = UseBundledChdman;
        result.ExternalChdmanPath = ExternalChdmanPath.Trim();
        result.PortableMode = PortableMode;
        result.VerifyAfterConversion = VerifyAfterConversion;
        result.SkipExistingOutput = SkipExistingOutput;
        result.CopyMatchingSbi = CopyMatchingSbi;
        result.EnableAutoM3uGeneration = EnableAutoM3uGeneration;
        result.OverwriteExistingM3uPlaylists = EnableAutoM3uGeneration && OverwriteExistingM3uPlaylists;
        result.DeleteTemporaryExtraction = DeleteTemporaryExtraction;
        result.DeleteFailedOutput = DeleteFailedOutput;
        result.DeleteSourceAfterVerifiedConversion = DeleteSourceAfterVerifiedConversion;
        result.DeleteSourceAfterVerifiedExtraction = DeleteSourceAfterVerifiedExtraction;
        result.EnableDeepIntegrityCheck = EnableDeepIntegrityCheck;
        result.ApplyStandardNamingBasedOnHash = ApplyStandardNamingBasedOnHash;
        result.RedumpDatabaseDownloadUrl = RedumpDatabaseDownloadUrl.Trim();
        result.EnableRedumpAutoSync = EnableRedumpAutoSync;
        result.RedumpLastSyncedUtc = _redumpLastSyncedUtc ?? string.Empty;
        result.MaxProcessorCount = SelectedProcessorOption?.Value ?? 0;
        result.MaxConcurrentConversions = AppSettings.NormalizeMaxConcurrentConversions(
            SelectedConcurrentConversionOption?.Value ?? AppSettings.DefaultMaxConcurrentConversions);
        result.PerformanceMode = Enum.TryParse<ConversionPerformanceMode>(SelectedPerformanceMode?.Key, true, out var performance)
            ? performance
            : ConversionPerformanceMode.Safe;
        result.ChdmanPriorityMode = Enum.TryParse<ChdmanProcessPriorityMode>(SelectedPriorityMode?.Key, true, out var priority)
            ? priority
            : ChdmanProcessPriorityMode.Quiet;
        result.CompressionCodecs = SelectedCompressionPreset?.Key switch
        {
            "fast" => "preset:fast",
            "balanced" => "preset:balanced",
            "max" => "preset:max",
            _ => "preset:default"
        };
        result.HunkSizeBytes = SelectedHunkPreset?.Key switch
        {
            "small" => -1,
            "balanced" => -2,
            "large" => -3,
            _ => 0
        };
        result.IsoCreateCommandOverride = Enum.TryParse<IsoCreateCommandOverride>(SelectedIsoCreateOverride?.Key, true, out var iso)
            ? iso
            : IsoCreateCommandOverride.Auto;
        result.Theme = Theme;
        result.UiLanguage = AppLanguageService.NormalizeLanguageName(UiLanguage);

        return result;
    }
    public void AcceptAppliedSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _useProgramDefaultsAsBase = false;
        _appliedSnapshot = settings.Clone();
        NotifySaveStateChanged();
    }

    public void SetDatabaseLastSyncedUtc(string? value)
    {
        _redumpLastSyncedUtc = NormalizeStoredTimestamp(value);
        OnPropertyChanged(nameof(DatabaseLastSyncedDisplay));
    }
}
