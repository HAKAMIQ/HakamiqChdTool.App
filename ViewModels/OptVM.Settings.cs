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

        ApplySettingsValues(settings);
        AcceptAppliedSettings(BuildResultSettings(settings));
    }

    /// <summary>
    /// Puts the options shown in this window back to their defaults. Everything else keeps its current value: the
    /// language and theme (header buttons), chosen CsoKit and igir paths, the Xbox 360 format, Redump sync data and
    /// settings with no control here.
    /// </summary>
    public void ApplyProgramDefaults()
    {
        AppSettings current = _appliedSnapshot?.Clone() ?? AppSettings.CreateSafeDefaults();
        AppSettings defaults = AppSettings.CreateSafeDefaults();
        defaults.Theme = current.Theme;
        defaults.UiLanguage = current.UiLanguage;
        defaults.PortableMode = current.PortableMode;
        defaults.RedumpDatabaseDownloadUrl = current.RedumpDatabaseDownloadUrl;
        defaults.EnableRedumpAutoSync = current.EnableRedumpAutoSync;
        defaults.RedumpLastSyncedUtc = current.RedumpLastSyncedUtc;
        defaults.RedumpSystemName = current.RedumpSystemName;
        defaults.OverwriteExistingM3uPlaylists = current.OverwriteExistingM3uPlaylists;
        ApplySettingsValues(defaults);
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
        RedumpLocalLibraryRoot = settings.RedumpLocalLibraryRoot;
        RedumpSystemName = settings.RedumpSystemName;
        OrganizeByPlatform = settings.OrganizeByPlatform;
        OrganizeByRegion = settings.OrganizeByRegion;
        IncludeSubfolders = settings.IncludeSubfolders;
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
        ExtractGdRomAsCueBin = settings.ExtractGdRomAsCueBin;
        ExplorerMenuEnabled = settings.ExplorerMenuEnabled;
        ExplorerOpensQuickWindow = settings.ExplorerOpensQuickWindow;
        WatchFolderEnabled = settings.WatchFolderEnabled;
        WatchFolderPath = settings.WatchFolderPath ?? string.Empty;
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
        SelectedTargetEmulator = TargetEmulatorOptions.FirstOrDefault(x => x.Key == HakamiqChdTool.App.Core.Chd.TargetEmulatorPolicy.Normalize(settings.TargetEmulator))
            ?? TargetEmulatorOptions.FirstOrDefault();
        SelectedIsoCreateOverride = ResolveIsoCreateOverride(settings.IsoCreateCommandOverride);
        SelectedChdPlatformProfile = ResolveChdPlatformProfile(settings.ChdPlatformProfileId);
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

        // Built on the current settings, so values this window does not show are kept.
        AppSettings result = source.Clone();
        result.UseCustomOutputRoot = UseCustomOutputRoot;
        result.CustomOutputRoot = CustomOutputRoot.Trim();
        result.PendingWorkspaceMode = UseCustomPendingWorkspace ? PendingWorkspaceMode.Custom : PendingWorkspaceMode.Automatic;
        result.PendingWorkspaceCustomRoot = PendingWorkspaceCustomRoot.Trim();
        result.UseCustomPendingWorkspace = UseCustomPendingWorkspace;
        result.RedumpDatXmlPath = RedumpDatXmlPath.Trim();
        result.RedumpLocalLibraryRoot = RedumpLocalLibraryRoot.Trim();
        result.RedumpSystemName = RedumpSystemName.Trim();
        result.OrganizeByPlatform = OrganizeByPlatform;
        result.OrganizeByRegion = OrganizeByRegion;
        result.IncludeSubfolders = IncludeSubfolders;
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
        result.ExtractGdRomAsCueBin = ExtractGdRomAsCueBin;
        result.ExplorerMenuEnabled = ExplorerMenuEnabled;
        result.ExplorerOpensQuickWindow = ExplorerOpensQuickWindow;
        result.WatchFolderEnabled = WatchFolderEnabled;
        result.WatchFolderPath = WatchFolderPath.Trim();
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
        result.TargetEmulator = HakamiqChdTool.App.Core.Chd.TargetEmulatorPolicy.Normalize(SelectedTargetEmulator?.Key);
        result.ChdPlatformProfileId = string.IsNullOrWhiteSpace(SelectedChdPlatformProfile?.Key)
            ? "auto"
            : SelectedChdPlatformProfile.Key;
        result.Theme = Theme;
        result.UiLanguage = AppLanguageService.NormalizeLanguageName(UiLanguage);

        return result;
    }

    public void AcceptAppliedSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _appliedSnapshot = settings.Clone();
        NotifySaveStateChanged();
    }

    public void SetDatabaseLastSyncedUtc(string? value)
    {
        _redumpLastSyncedUtc = NormalizeStoredTimestamp(value);
        OnPropertyChanged(nameof(DatabaseLastSyncedDisplay));
    }
}