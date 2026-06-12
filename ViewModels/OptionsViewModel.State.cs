using CommunityToolkit.Mvvm.ComponentModel;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.RedumpCatalog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{

    private void NotifySaveStateChanged()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanSave));
    }


    private bool CurrentValuesEqual(AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string redumpPlatformMode = SelectedRedumpPlatformOption is null
            ? "Auto"
            : string.Equals(SelectedRedumpPlatformOption.Key, "Auto", StringComparison.OrdinalIgnoreCase)
                ? "Auto"
                : string.Equals(SelectedRedumpPlatformOption.Key, "Unknown", StringComparison.OrdinalIgnoreCase)
                    ? "Unknown"
                    : "Platform";

        string redumpPlatformKey = redumpPlatformMode == "Platform"
            ? SelectedRedumpPlatformOption?.Key ?? string.Empty
            : string.Empty;

        string compressionCodecs = SelectedCompressionPreset?.Key switch
        {
            "fast" => "preset:fast",
            "balanced" => "preset:balanced",
            "max" => "preset:max",
            _ => "preset:default"
        };

        int hunkSizeBytes = SelectedHunkPreset?.Key switch
        {
            "small" => -1,
            "balanced" => -2,
            "large" => -3,
            _ => 0
        };

        IsoCreateCommandOverride isoCreateOverride = Enum.TryParse<IsoCreateCommandOverride>(
            SelectedIsoCreateOverride?.Key,
            true,
            out var iso)
                ? iso
                : IsoCreateCommandOverride.Auto;

        ConversionPerformanceMode performanceMode = Enum.TryParse<ConversionPerformanceMode>(
            SelectedPerformanceMode?.Key,
            true,
            out var performance)
                ? performance
                : ConversionPerformanceMode.Safe;

        ChdmanProcessPriorityMode priorityMode = Enum.TryParse<ChdmanProcessPriorityMode>(
            SelectedPriorityMode?.Key,
            true,
            out var priority)
                ? priority
                : ChdmanProcessPriorityMode.Quiet;

        bool useCustomPendingWorkspace = UseCustomPendingWorkspace;
        PendingWorkspaceMode pendingWorkspaceMode = useCustomPendingWorkspace
            ? PendingWorkspaceMode.Custom
            : PendingWorkspaceMode.Automatic;

        return StringEquals(Theme, snapshot.Theme)
            && StringEquals(AppLanguageService.NormalizeLanguageName(UiLanguage), AppLanguageService.NormalizeLanguageName(snapshot.UiLanguage))
            && UseCustomOutputRoot == snapshot.UseCustomOutputRoot
            && StringEquals(CustomOutputRoot.Trim(), snapshot.CustomOutputRoot)
            && pendingWorkspaceMode == snapshot.PendingWorkspaceMode
            && StringEquals(PendingWorkspaceCustomRoot.Trim(), snapshot.PendingWorkspaceCustomRoot)
            && useCustomPendingWorkspace == snapshot.UseCustomPendingWorkspace
            && StringEquals(RedumpDatXmlPath.Trim(), snapshot.RedumpDatXmlPath)
            && StringEquals(RedumpSystemName.Trim(), snapshot.RedumpSystemName)
            && StringEquals(redumpPlatformMode, snapshot.RedumpPlatformMode)
            && StringEquals(redumpPlatformKey, snapshot.RedumpPlatformKey)
            && StringEquals(SelectedRedumpArtifactOption?.Key ?? "Datfile", snapshot.RedumpArtifactKind)
            && OrganizeByPlatform == snapshot.OrganizeByPlatform
            && OrganizeByRegion == snapshot.OrganizeByRegion
            && IncludeSubfolders == snapshot.IncludeSubfolders
            && ShowStorageAdvisorDialog == !snapshot.SuppressStorageAdvisorDialog
            && UseBundledChdman == snapshot.UseBundledChdman
            && StringEquals(ExternalChdmanPath.Trim(), snapshot.ExternalChdmanPath)
            && PortableMode == snapshot.PortableMode
            && VerifyAfterConversion == snapshot.VerifyAfterConversion
            && SkipExistingOutput == snapshot.SkipExistingOutput
            && CopyMatchingSbi == snapshot.CopyMatchingSbi
            && EnableAutoM3uGeneration == snapshot.EnableAutoM3uGeneration
            && OverwriteExistingM3uPlaylists == snapshot.OverwriteExistingM3uPlaylists
            && DeleteTemporaryExtraction == snapshot.DeleteTemporaryExtraction
            && DeleteFailedOutput == snapshot.DeleteFailedOutput
            && DeleteSourceAfterVerifiedConversion == snapshot.DeleteSourceAfterVerifiedConversion
            && DeleteSourceAfterVerifiedExtraction == snapshot.DeleteSourceAfterVerifiedExtraction
            && EnableDeepIntegrityCheck == snapshot.EnableDeepIntegrityCheck
            && ApplyStandardNamingBasedOnHash == snapshot.ApplyStandardNamingBasedOnHash
            && StringEquals(RedumpDatabaseDownloadUrl.Trim(), snapshot.RedumpDatabaseDownloadUrl)
            && EnableRedumpAutoSync == snapshot.EnableRedumpAutoSync
            && StringEquals(_redumpLastSyncedUtc ?? string.Empty, snapshot.RedumpLastSyncedUtc)
            && (SelectedProcessorOption?.Value ?? 0) == snapshot.MaxProcessorCount
            && (SelectedConcurrentConversionOption?.Value ?? AppSettings.DefaultMaxConcurrentConversions) == AppSettings.NormalizeMaxConcurrentConversions(snapshot.MaxConcurrentConversions)
            && performanceMode == snapshot.PerformanceMode
            && priorityMode == snapshot.ChdmanPriorityMode
            && StringEquals(compressionCodecs, snapshot.CompressionCodecs)
            && hunkSizeBytes == snapshot.HunkSizeBytes
            && isoCreateOverride == snapshot.IsoCreateCommandOverride;
    }


    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
    }
}
