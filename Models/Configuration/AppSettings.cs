using System.Text.Json;
using System.Text.Json.Serialization;

namespace HakamiqChdTool.App.Models;

public sealed class AppSettings
{
    public const int DefaultMaxConcurrentConversions = 1;

    public const int MaxConcurrentConversionsUpperBound = 4;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2227:Collection properties should be read only",
        Justification = "The JsonExtensionData dictionary must remain replaceable so deserialization and deep-copy operations preserve unknown configuration properties.")]
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Theme { get; set; } = "Dark";

    public string UiLanguage { get; set; } = "ar-SA";

    public string RedumpDatXmlPath { get; set; } = string.Empty;

    public string RedumpLocalLibraryRoot { get; set; } = string.Empty;

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

    public bool OrganizeByPlatform { get; set; } = false;

    public bool OrganizeByRegion { get; set; } = false;

    public bool VerifyAfterConversion { get; set; } = true;

    public bool SkipExistingOutput { get; set; } = true;

    public bool CopyMatchingSbi { get; set; } = true;

    public bool EnableAutoM3uGeneration { get; set; } = false;

    public bool OverwriteExistingM3uPlaylists { get; set; } = false;

    public bool EnableDiskSpaceGuard { get; set; } = true;

    // On by default so files unpacked from archives do not pile up in %TEMP%; a saved value is kept.
    public bool DeleteTemporaryExtraction { get; set; } = true;

    public bool DeleteFailedOutput { get; set; } = false;

    public bool DeleteSourceAfterVerifiedConversion { get; set; } = false;

    public bool DeleteSourceAfterVerifiedExtraction { get; set; } = false;

    /// <summary>GD-ROM (Dreamcast) CHDs extract to GDI unless this is set; then they extract to CUE/BIN like CDs.</summary>
    public bool ExtractGdRomAsCueBin { get; set; } = false;

    /// <summary>Adds CHD Tool to the Explorer right-click menu for this Windows account.</summary>
    public bool ExplorerMenuEnabled { get; set; } = false;

    /// <summary>The Explorer menu opens the small quick window instead of the main window (when the app is not already open).</summary>
    public bool ExplorerOpensQuickWindow { get; set; } = true;

    /// <summary>Converts new disc images and archives that appear in WatchFolderPath while the app is open.</summary>
    public bool WatchFolderEnabled { get; set; } = false;

    public string WatchFolderPath { get; set; } = string.Empty;

    public bool EnableDeepIntegrityCheck { get; set; } = false;

    public bool ApplyStandardNamingBasedOnHash { get; set; } = false;

    public string RedumpDatabaseDownloadUrl { get; set; } = string.Empty;

    public string RedumpLastSyncedUtc { get; set; } = string.Empty;

    public string RedumpAutoSyncBackoffUntilUtc { get; set; } = string.Empty;

    public bool EnableRedumpAutoSync { get; set; } = false;

    public bool UseBundledChdman { get; set; } = true;

    public string ExternalChdmanPath { get; set; } = string.Empty;

    public string ExternalCsoKitPath { get; set; } = string.Empty;

    public string ExternalIgirPath { get; set; } = string.Empty;

    public bool PortableMode { get; set; } = false;

    public bool HasSeenAdministratorWarning { get; set; } = false;

    public string DiscordSupportUrl { get; set; } = string.Empty;

    public int MaxProcessorCount { get; set; } = 0;

    public int MaxConcurrentConversions { get; set; } = DefaultMaxConcurrentConversions;

    public bool EnableAutoResourceLimiter { get; set; } = true;

    public int ReservedLogicalCores { get; set; } = 2;

    public bool EnableStressMode { get; set; } = false;

    public ConversionPerformanceMode PerformanceMode { get; set; } = ConversionPerformanceMode.Safe;

    public ChdmanProcessPriorityMode ChdmanPriorityMode { get; set; } = ChdmanProcessPriorityMode.Quiet;

    public string CompressionCodecs { get; set; } = "preset:default";

    public int HunkSizeBytes { get; set; } = 0;

    /// <summary>auto, dvd2048 (PCSX2 and PPSSPP) or nozstd (RetroArch and older emulators); see TargetEmulatorPolicy.</summary>
    public string TargetEmulator { get; set; } = "auto";

    /// <summary>god (Games on Demand, Xenia and an Xbox 360) or zar (Xenia Canary); see XboxFormats.</summary>
    public string Xbox360Format { get; set; } = "god";

    public IsoCreateCommandOverride IsoCreateCommandOverride { get; set; } = IsoCreateCommandOverride.Auto;

    public string ChdPlatformProfileId { get; set; } = "auto";

    public static AppSettings CreateSafeDefaults() => new();

    public static void ResetEngineToDefaults(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.MaxProcessorCount = 0;
        settings.MaxConcurrentConversions = DefaultMaxConcurrentConversions;
        settings.EnableAutoResourceLimiter = true;
        settings.ReservedLogicalCores = 2;
        settings.PerformanceMode = ConversionPerformanceMode.Safe;
        settings.ChdmanPriorityMode = ChdmanProcessPriorityMode.Quiet;
        settings.CompressionCodecs = "preset:default";
        settings.HunkSizeBytes = 0;
        settings.IsoCreateCommandOverride = IsoCreateCommandOverride.Auto;
        settings.ChdPlatformProfileId = "auto";
        settings.TargetEmulator = "auto";
    }

    public static int NormalizeMaxConcurrentConversions(int value)
    {
        return Math.Clamp(value, DefaultMaxConcurrentConversions, MaxConcurrentConversionsUpperBound);
    }

    public void CopyFrom(AppSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Theme = other.Theme;
        UiLanguage = other.UiLanguage;
        RedumpDatXmlPath = other.RedumpDatXmlPath;
        RedumpLocalLibraryRoot = other.RedumpLocalLibraryRoot;
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
        ExtractGdRomAsCueBin = other.ExtractGdRomAsCueBin;
        ExplorerMenuEnabled = other.ExplorerMenuEnabled;
        ExplorerOpensQuickWindow = other.ExplorerOpensQuickWindow;
        WatchFolderEnabled = other.WatchFolderEnabled;
        WatchFolderPath = other.WatchFolderPath;
        EnableDeepIntegrityCheck = other.EnableDeepIntegrityCheck;
        ApplyStandardNamingBasedOnHash = other.ApplyStandardNamingBasedOnHash;
        RedumpDatabaseDownloadUrl = other.RedumpDatabaseDownloadUrl;
        RedumpLastSyncedUtc = other.RedumpLastSyncedUtc;
        RedumpAutoSyncBackoffUntilUtc = other.RedumpAutoSyncBackoffUntilUtc;
        EnableRedumpAutoSync = other.EnableRedumpAutoSync;
        UseBundledChdman = other.UseBundledChdman;
        ExternalChdmanPath = other.ExternalChdmanPath;
        ExternalCsoKitPath = other.ExternalCsoKitPath;
        ExternalIgirPath = other.ExternalIgirPath;
        PortableMode = other.PortableMode;
        HasSeenAdministratorWarning = other.HasSeenAdministratorWarning;
        DiscordSupportUrl = other.DiscordSupportUrl;
        MaxProcessorCount = other.MaxProcessorCount;
        MaxConcurrentConversions = NormalizeMaxConcurrentConversions(other.MaxConcurrentConversions);
        EnableAutoResourceLimiter = other.EnableAutoResourceLimiter;
        ReservedLogicalCores = other.ReservedLogicalCores;
        EnableStressMode = other.EnableStressMode;
        PerformanceMode = other.PerformanceMode;
        ChdmanPriorityMode = other.ChdmanPriorityMode;
        CompressionCodecs = other.CompressionCodecs;
        HunkSizeBytes = other.HunkSizeBytes;
        TargetEmulator = other.TargetEmulator;
        Xbox360Format = other.Xbox360Format;
        IsoCreateCommandOverride = other.IsoCreateCommandOverride;
        ChdPlatformProfileId = other.ChdPlatformProfileId;
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
            RedumpLocalLibraryRoot = RedumpLocalLibraryRoot,
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
            ExtractGdRomAsCueBin = ExtractGdRomAsCueBin,
            ExplorerMenuEnabled = ExplorerMenuEnabled,
            ExplorerOpensQuickWindow = ExplorerOpensQuickWindow,
            WatchFolderEnabled = WatchFolderEnabled,
            WatchFolderPath = WatchFolderPath,
            EnableDeepIntegrityCheck = EnableDeepIntegrityCheck,
            ApplyStandardNamingBasedOnHash = ApplyStandardNamingBasedOnHash,
            RedumpDatabaseDownloadUrl = RedumpDatabaseDownloadUrl,
            RedumpLastSyncedUtc = RedumpLastSyncedUtc,
            RedumpAutoSyncBackoffUntilUtc = RedumpAutoSyncBackoffUntilUtc,
            EnableRedumpAutoSync = EnableRedumpAutoSync,
            UseBundledChdman = UseBundledChdman,
            ExternalChdmanPath = ExternalChdmanPath,
            ExternalCsoKitPath = ExternalCsoKitPath,
            ExternalIgirPath = ExternalIgirPath,
            PortableMode = PortableMode,
            HasSeenAdministratorWarning = HasSeenAdministratorWarning,
            DiscordSupportUrl = DiscordSupportUrl,
            MaxProcessorCount = MaxProcessorCount,
            MaxConcurrentConversions = NormalizeMaxConcurrentConversions(MaxConcurrentConversions),
            EnableAutoResourceLimiter = EnableAutoResourceLimiter,
            ReservedLogicalCores = ReservedLogicalCores,
            EnableStressMode = EnableStressMode,
            PerformanceMode = PerformanceMode,
            ChdmanPriorityMode = ChdmanPriorityMode,
            CompressionCodecs = CompressionCodecs,
            HunkSizeBytes = HunkSizeBytes,
            TargetEmulator = TargetEmulator,
            Xbox360Format = Xbox360Format,
            IsoCreateCommandOverride = IsoCreateCommandOverride,
            ChdPlatformProfileId = ChdPlatformProfileId,
            ExtensionData = ExtensionData is null
                ? null
                : new Dictionary<string, JsonElement>(ExtensionData, StringComparer.Ordinal)
        };
    }
}
