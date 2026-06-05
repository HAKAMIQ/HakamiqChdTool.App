using System.Text.Json;
using System.Text.Json.Serialization;

namespace HakamiqChdTool.App.Models;

public sealed class AppSettings
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Theme { get; set; } = "Dark";

    public string UiLanguage { get; set; } = "ar-SA";

    public string RedumpDatXmlPath { get; set; } = string.Empty;

    public string RedumpSystemName { get; set; } = string.Empty;

    public string RedumpPlatformMode { get; set; } = "Auto";

    public string RedumpPlatformKey { get; set; } = string.Empty;

    public string RedumpArtifactKind { get; set; } = "Datfile";

    public bool IncludeSubfolders { get; set; } = false;

    public bool UseCustomOutputRoot { get; set; } = false;

    public string CustomOutputRoot { get; set; } = string.Empty;

    public PendingWorkspaceMode PendingWorkspaceMode { get; set; } = PendingWorkspaceMode.Automatic;

    public string PendingWorkspaceCustomRoot { get; set; } = string.Empty;

    public bool UseCustomPendingWorkspace { get; set; } = false;

    public bool SuppressStorageAdvisorDialog { get; set; } = false;

    public bool OrganizeByPlatform { get; set; } = false;

    public bool OrganizeByRegion { get; set; } = false;

    public bool VerifyAfterConversion { get; set; } = true;

    public bool SkipExistingOutput { get; set; } = true;

    public bool CopyMatchingSbi { get; set; } = true;

    public bool EnableAutoM3uGeneration { get; set; } = false;

    public bool OverwriteExistingM3uPlaylists { get; set; } = false;

    public bool EnableDiskSpaceGuard { get; set; } = true;

    public bool DeleteTemporaryExtraction { get; set; } = false;

    public bool DeleteFailedOutput { get; set; } = false;

    public bool DeleteSourceAfterVerifiedConversion { get; set; } = false;

    public bool DeleteSourceAfterVerifiedExtraction { get; set; } = false;

    public bool EnableDeepIntegrityCheck { get; set; } = false;

    public bool ApplyStandardNamingBasedOnHash { get; set; } = false;

    public string RedumpDatabaseDownloadUrl { get; set; } = string.Empty;

    public string RedumpLastSyncedUtc { get; set; } = string.Empty;

    public bool EnableRedumpAutoSync { get; set; } = false;

    public bool UseBundledChdman { get; set; } = true;

    public string ExternalChdmanPath { get; set; } = string.Empty;

    public bool PortableMode { get; set; } = false;

    public bool HasSeenAdministratorWarning { get; set; } = false;

    public string StorePageUrl { get; set; } = string.Empty;

    public string DiscordSupportUrl { get; set; } = string.Empty;

    public int MaxProcessorCount { get; set; } = 0;

    public bool EnableAutoResourceLimiter { get; set; } = true;

    public int ReservedLogicalCores { get; set; } = 2;

    public bool EnableStressMode { get; set; } = false;

    public string CompressionCodecs { get; set; } = "preset:default";

    public int HunkSizeBytes { get; set; } = 0;

    public IsoCreateCommandOverride IsoCreateCommandOverride { get; set; } = IsoCreateCommandOverride.Auto;

    public static AppSettings CreateSafeDefaults() => new();

    public static void ResetEngineToDefaults(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.MaxProcessorCount = 0;
        settings.EnableAutoResourceLimiter = true;
        settings.ReservedLogicalCores = 2;
        settings.CompressionCodecs = "preset:default";
        settings.HunkSizeBytes = 0;
        settings.IsoCreateCommandOverride = IsoCreateCommandOverride.Auto;
    }

    public void CopyFrom(AppSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Theme = other.Theme;
        UiLanguage = other.UiLanguage;
        RedumpDatXmlPath = other.RedumpDatXmlPath;
        RedumpSystemName = other.RedumpSystemName;
        RedumpPlatformMode = other.RedumpPlatformMode;
        RedumpPlatformKey = other.RedumpPlatformKey;
        RedumpArtifactKind = other.RedumpArtifactKind;
        IncludeSubfolders = other.IncludeSubfolders;
        UseCustomOutputRoot = other.UseCustomOutputRoot;
        CustomOutputRoot = other.CustomOutputRoot;
        PendingWorkspaceMode = other.PendingWorkspaceMode;
        PendingWorkspaceCustomRoot = other.PendingWorkspaceCustomRoot;
        UseCustomPendingWorkspace = other.UseCustomPendingWorkspace;
        SuppressStorageAdvisorDialog = other.SuppressStorageAdvisorDialog;
        OrganizeByPlatform = other.OrganizeByPlatform;
        OrganizeByRegion = other.OrganizeByRegion;
        VerifyAfterConversion = other.VerifyAfterConversion;
        SkipExistingOutput = other.SkipExistingOutput;
        CopyMatchingSbi = other.CopyMatchingSbi;
        EnableAutoM3uGeneration = other.EnableAutoM3uGeneration;
        OverwriteExistingM3uPlaylists = other.OverwriteExistingM3uPlaylists;
        EnableDiskSpaceGuard = other.EnableDiskSpaceGuard;
        DeleteTemporaryExtraction = other.DeleteTemporaryExtraction;
        DeleteFailedOutput = other.DeleteFailedOutput;
        DeleteSourceAfterVerifiedConversion = other.DeleteSourceAfterVerifiedConversion;
        DeleteSourceAfterVerifiedExtraction = other.DeleteSourceAfterVerifiedExtraction;
        EnableDeepIntegrityCheck = other.EnableDeepIntegrityCheck;
        ApplyStandardNamingBasedOnHash = other.ApplyStandardNamingBasedOnHash;
        RedumpDatabaseDownloadUrl = other.RedumpDatabaseDownloadUrl;
        RedumpLastSyncedUtc = other.RedumpLastSyncedUtc;
        EnableRedumpAutoSync = other.EnableRedumpAutoSync;
        UseBundledChdman = other.UseBundledChdman;
        ExternalChdmanPath = other.ExternalChdmanPath;
        PortableMode = other.PortableMode;
        HasSeenAdministratorWarning = other.HasSeenAdministratorWarning;
        StorePageUrl = other.StorePageUrl;
        DiscordSupportUrl = other.DiscordSupportUrl;
        MaxProcessorCount = other.MaxProcessorCount;
        EnableAutoResourceLimiter = other.EnableAutoResourceLimiter;
        ReservedLogicalCores = other.ReservedLogicalCores;
        EnableStressMode = other.EnableStressMode;
        CompressionCodecs = other.CompressionCodecs;
        HunkSizeBytes = other.HunkSizeBytes;
        IsoCreateCommandOverride = other.IsoCreateCommandOverride;
        ExtensionData = other.ExtensionData is null
            ? null
            : new Dictionary<string, JsonElement>(other.ExtensionData, StringComparer.Ordinal);
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Theme = Theme,
            UiLanguage = UiLanguage,
            RedumpDatXmlPath = RedumpDatXmlPath,
            RedumpSystemName = RedumpSystemName,
            RedumpPlatformMode = RedumpPlatformMode,
            RedumpPlatformKey = RedumpPlatformKey,
            RedumpArtifactKind = RedumpArtifactKind,
            IncludeSubfolders = IncludeSubfolders,
            UseCustomOutputRoot = UseCustomOutputRoot,
            CustomOutputRoot = CustomOutputRoot,
            PendingWorkspaceMode = PendingWorkspaceMode,
            PendingWorkspaceCustomRoot = PendingWorkspaceCustomRoot,
            UseCustomPendingWorkspace = UseCustomPendingWorkspace,
            SuppressStorageAdvisorDialog = SuppressStorageAdvisorDialog,
            OrganizeByPlatform = OrganizeByPlatform,
            OrganizeByRegion = OrganizeByRegion,
            VerifyAfterConversion = VerifyAfterConversion,
            SkipExistingOutput = SkipExistingOutput,
            CopyMatchingSbi = CopyMatchingSbi,
            EnableAutoM3uGeneration = EnableAutoM3uGeneration,
            OverwriteExistingM3uPlaylists = OverwriteExistingM3uPlaylists,
            EnableDiskSpaceGuard = EnableDiskSpaceGuard,
            DeleteTemporaryExtraction = DeleteTemporaryExtraction,
            DeleteFailedOutput = DeleteFailedOutput,
            DeleteSourceAfterVerifiedConversion = DeleteSourceAfterVerifiedConversion,
            DeleteSourceAfterVerifiedExtraction = DeleteSourceAfterVerifiedExtraction,
            EnableDeepIntegrityCheck = EnableDeepIntegrityCheck,
            ApplyStandardNamingBasedOnHash = ApplyStandardNamingBasedOnHash,
            RedumpDatabaseDownloadUrl = RedumpDatabaseDownloadUrl,
            RedumpLastSyncedUtc = RedumpLastSyncedUtc,
            EnableRedumpAutoSync = EnableRedumpAutoSync,
            UseBundledChdman = UseBundledChdman,
            ExternalChdmanPath = ExternalChdmanPath,
            PortableMode = PortableMode,
            HasSeenAdministratorWarning = HasSeenAdministratorWarning,
            StorePageUrl = StorePageUrl,
            DiscordSupportUrl = DiscordSupportUrl,
            MaxProcessorCount = MaxProcessorCount,
            EnableAutoResourceLimiter = EnableAutoResourceLimiter,
            ReservedLogicalCores = ReservedLogicalCores,
            EnableStressMode = EnableStressMode,
            CompressionCodecs = CompressionCodecs,
            HunkSizeBytes = HunkSizeBytes,
            IsoCreateCommandOverride = IsoCreateCommandOverride,
            ExtensionData = ExtensionData is null
                ? null
                : new Dictionary<string, JsonElement>(ExtensionData, StringComparer.Ordinal)
        };
    }
}