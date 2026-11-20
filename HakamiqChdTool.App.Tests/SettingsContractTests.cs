using System.IO;
using System.Reflection;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private enum SettingKind
    {
        // A named test proves what the setting changes.
        Effect,
        // Kept for display, bookkeeping or compatibility; it changes no processing.
        Stored,
        // It changes behaviour but no test proves it yet. The reason says what is missing.
        Gap
    }

    private sealed record SettingContract(SettingKind Kind, string Reason, params string[] Tests);

    // Every AppSettings property is classified here. A new setting fails this test until it is added with a
    // test that proves its effect, or an explicit reason. This keeps options that do nothing from coming back.
    private static readonly Dictionary<string, SettingContract> SettingsContract = new(StringComparer.Ordinal)
    {
        ["ExtensionData"] = new(SettingKind.Stored, "Unknown fields from newer versions are kept when saving."),
        ["Theme"] = new(SettingKind.Stored, "Display preference applied by ThemeService; colours are covered by the theme tests."),
        ["UiLanguage"] = new(SettingKind.Effect, "The header button switches the language live.", nameof(TestLanguageSwitchesWithoutRestart)),
        ["RedumpDatXmlPath"] = new(SettingKind.Stored, "Only pre-fills the folder of the DAT import dialog."),
        ["RedumpLocalLibraryRoot"] = new(SettingKind.Gap, "Used by the Redump library scan commands in Options; no test runs a scan from this setting."),
        ["RedumpSystemName"] = new(SettingKind.Stored, "Hidden and no longer applied to DAT imports.", nameof(TestOptionsFollowUpWiring)),
        ["RedumpPlatformMode"] = new(SettingKind.Stored, "Kept from older versions; nothing reads it now.", nameof(TestOptionsKeepTheSavedRedumpUrl)),
        ["RedumpPlatformKey"] = new(SettingKind.Stored, "Kept from older versions; nothing reads it now.", nameof(TestOptionsKeepTheSavedRedumpUrl)),
        ["RedumpArtifactKind"] = new(SettingKind.Stored, "Kept from older versions; nothing reads it now.", nameof(TestOptionsKeepTheSavedRedumpUrl)),
        ["IncludeSubfolders"] = new(SettingKind.Effect, "Folder intake searches subfolders.", nameof(TestIncludeSubfoldersControlsFolderScan)),
        ["UseCustomOutputRoot"] = new(SettingKind.Effect, "Output goes to the chosen folder.", nameof(TestOverwriteNeverReplacesAnotherSourcesOutputFromThisSession), nameof(TestSkipExistingOutputLeavesTheExistingChd)),
        ["CustomOutputRoot"] = new(SettingKind.Effect, "Output goes to the chosen folder.", nameof(TestOverwriteNeverReplacesAnotherSourcesOutputFromThisSession), nameof(TestSkipExistingOutputLeavesTheExistingChd)),
        ["PendingWorkspaceMode"] = new(SettingKind.Effect, "Work folders go under the chosen root.", nameof(TestOrphanCleanRequiresAppOwnershipMarker)),
        ["PendingWorkspaceCustomRoot"] = new(SettingKind.Effect, "Work folders go under the chosen root.", nameof(TestOrphanCleanRequiresAppOwnershipMarker)),
        ["UseCustomPendingWorkspace"] = new(SettingKind.Effect, "Work folders go under the chosen root.", nameof(TestOrphanCleanRequiresAppOwnershipMarker)),
        ["OrganizeByPlatform"] = new(SettingKind.Effect, "Output goes into a platform folder.", nameof(TestOrganizeByRegionBuildsTheRegionFolder)),
        ["OrganizeByRegion"] = new(SettingKind.Effect, "Output goes into a region folder.", nameof(TestOrganizeByRegionBuildsTheRegionFolder)),
        ["VerifyAfterConversion"] = new(SettingKind.Effect, "The output is verified, and source deletion depends on it.", nameof(TestConversionSourceCleanupContractUnchanged), nameof(TestExtractionSourceCleanupRequiresOutputProof)),
        ["SkipExistingOutput"] = new(SettingKind.Effect, "An existing CHD is left alone and reported as skipped.", nameof(TestSkipExistingOutputLeavesTheExistingChd)),
        ["CopyMatchingSbi"] = new(SettingKind.Effect, "A matching SBI is copied next to the output.", nameof(TestSourceCleanupKeepsSbiUnlessCopiedNextToOutput)),
        ["EnableAutoM3uGeneration"] = new(SettingKind.Effect, "Multi-disc sets get an M3U after a run.", nameof(TestM3uSettingsReachTheGenerator), nameof(TestM3uListsDiscsInOrderAndKeepsExistingFiles)),
        ["OverwriteExistingM3uPlaylists"] = new(SettingKind.Effect, "An existing M3U is replaced or kept.", nameof(TestM3uSettingsReachTheGenerator), nameof(TestM3uListsDiscsInOrderAndKeepsExistingFiles)),
        ["EnableDiskSpaceGuard"] = new(SettingKind.Gap, "The disk space preflight runs only while it is on; no test turns it off."),
        ["DeleteTemporaryExtraction"] = new(SettingKind.Gap, "Removes temporary extraction folders after a run; the boundary is tested, the setting is not."),
        ["DeleteFailedOutput"] = new(SettingKind.Stored, "Kept for old settings files; incomplete output from a failed job is always removed.", nameof(TestFailedExtractFinalizeNeverDeletesForeignCueBin), nameof(TestFailedOutputCleanupIsAlwaysOn)),
        ["DeleteSourceAfterVerifiedConversion"] = new(SettingKind.Effect, "The source is deleted only after a verified conversion.", nameof(TestConversionSourceCleanupContractUnchanged), nameof(TestSkipExistingOutputLeavesTheExistingChd)),
        ["DeleteSourceAfterVerifiedExtraction"] = new(SettingKind.Effect, "The CHD is deleted only after a proven extraction.", nameof(TestExtractionSourceCleanupRequiresOutputProof)),
        ["ExtractGdRomAsCueBin"] = new(SettingKind.Effect, "GD-ROM extracts to CUE/BIN instead of GDI.", nameof(TestGdRomExtractionPicksGdi)),
        ["ExplorerMenuEnabled"] = new(SettingKind.Effect, "Adds or removes the Explorer menu entries.", nameof(TestExplorerMenuRegistersAndRemovesOnlyItsOwnKeys)),
        ["ExplorerOpensQuickWindow"] = new(SettingKind.Effect, "An Explorer menu launch opens the quick window or the main window.", nameof(TestQuickWindowFollowsTheSetting), nameof(TestQuickWindowSettingInOptions)),
        ["WatchFolderEnabled"] = new(SettingKind.Effect, "Starts or stops watching the folder.", nameof(TestWatchFolderSettingInOptions), nameof(TestWatchFolderReportsALostFolderOnce)),
        ["WatchFolderPath"] = new(SettingKind.Effect, "The folder that is watched.", nameof(TestWatchFolderSettingInOptions), nameof(TestWatchFolderReportsALostFolderOnce)),
        ["EnableDeepIntegrityCheck"] = new(SettingKind.Gap, "Adds the full read of each source before converting; no test runs a conversion with it on."),
        ["ApplyStandardNamingBasedOnHash"] = new(SettingKind.Gap, "Only computes and logs a suggested name after a match; no test."),
        ["RedumpDatabaseDownloadUrl"] = new(SettingKind.Effect, "Used by startup auto-sync and kept when Options is saved.", nameof(TestOptionsKeepTheSavedRedumpUrl), nameof(TestHiddenRedumpDownloadUiIsRemoved)),
        ["RedumpLastSyncedUtc"] = new(SettingKind.Stored, "Bookkeeping written by the Redump sync."),
        ["RedumpAutoSyncBackoffUntilUtc"] = new(SettingKind.Stored, "Bookkeeping written by the Redump sync."),
        ["EnableRedumpAutoSync"] = new(SettingKind.Gap, "No UI and off by default (checked); the startup download itself is not exercised by a test.", nameof(TestHiddenRedumpDownloadUiIsRemoved)),
        ["UseBundledChdman"] = new(SettingKind.Effect, "Chooses the bundled or the external chdman.", nameof(TestChdmanPathFollowsTheSetting)),
        ["ExternalChdmanPath"] = new(SettingKind.Effect, "The external chdman that is run.", nameof(TestChdmanPathFollowsTheSetting)),
        ["ExternalCsoKitPath"] = new(SettingKind.Gap, "The CsoKit tests use the bundled tool; a chosen csokit.exe is not tested."),
        ["ExternalIgirPath"] = new(SettingKind.Effect, "A chosen igir is used only if it is the pinned build.", nameof(TestChosenIgirDoesNotDisableBundledIgir)),
        ["PortableMode"] = new(SettingKind.Gap, "Moves settings and data next to the app; AppPaths is process-wide, so tests do not switch it."),
        ["HasSeenAdministratorWarning"] = new(SettingKind.Stored, "Remembers that a one-time notice was shown."),
        ["DiscordSupportUrl"] = new(SettingKind.Stored, "Link shown in About."),
        ["MaxProcessorCount"] = new(SettingKind.Effect, "The chdman thread count, or Auto.", nameof(TestAutoThreadsAreSharedAcrossConcurrentConversions)),
        ["MaxConcurrentConversions"] = new(SettingKind.Effect, "How many files convert at once; Auto threads are shared between them.", nameof(TestAutoThreadsAreSharedAcrossConcurrentConversions)),
        ["EnableAutoResourceLimiter"] = new(SettingKind.Effect, "No UI; when off, Auto passes no thread count to chdman.", nameof(TestAutoLimiterAndReservedCoresShapeTheThreadCount)),
        ["ReservedLogicalCores"] = new(SettingKind.Effect, "No UI; the processors Auto leaves free.", nameof(TestAutoLimiterAndReservedCoresShapeTheThreadCount)),
        ["EnableStressMode"] = new(SettingKind.Gap, "Developer stress load during a conversion; no UI and no test."),
        ["PerformanceMode"] = new(SettingKind.Effect, "The Auto thread count.", nameof(TestAutoThreadsAreSharedAcrossConcurrentConversions), nameof(TestOptionsOfferOnlyWorkingChoices)),
        ["ChdmanPriorityMode"] = new(SettingKind.Effect, "Runs chdman and the helper tools below normal in Quiet mode.", nameof(TestHelperToolsFollowThePrioritySetting)),
        ["CompressionCodecs"] = new(SettingKind.Effect, "The chdman -c codecs.", nameof(TestTargetEmulatorPolicy)),
        ["HunkSizeBytes"] = new(SettingKind.Effect, "The chdman -hs hunk size.", nameof(TestTargetEmulatorPolicy)),
        ["Xbox360Format"] = new(SettingKind.Effect, "Xbox 360 games become Games on Demand or ZAR in the Xbox window.", nameof(TestXbox360FormatSetting)),
        ["TargetEmulator"] = new(SettingKind.Effect, "Overrides codecs and hunk size for an emulator.", nameof(TestTargetEmulatorPolicy)),
        ["IsoCreateCommandOverride"] = new(SettingKind.Effect, "Force DVD uses createdvd; Auto follows the media evidence.", nameof(TestIsoOverrideForceDvdUsesCreateDvd), nameof(TestOptionsOfferOnlyWorkingChoices)),
        ["ChdPlatformProfileId"] = new(SettingKind.Effect, "The selected CHD profile, falling back to Auto for other disc types.", nameof(TestPlatformProfileFallsBackToAutoForOtherInputs), nameof(TestHardDiskPlannerRequiresExplicitProfile)),
    };

    private static void TestEverySettingIsClassified()
    {
        string[] settings = [.. typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.SetMethod!.IsPublic)
            .Select(property => property.Name)];

        string[] unclassified = [.. settings.Where(name => !SettingsContract.ContainsKey(name))];
        AssertEqual(0, unclassified.Length, "Every setting is classified in SettingsContract: " + string.Join(", ", unclassified));

        string[] stale = [.. SettingsContract.Keys.Where(name => !settings.Contains(name))];
        AssertEqual(0, stale.Length, "SettingsContract lists no removed setting: " + string.Join(", ", stale));

        foreach ((string name, SettingContract contract) in SettingsContract)
        {
            AssertFalse(string.IsNullOrWhiteSpace(contract.Reason), $"{name} has a reason.");
            if (contract.Kind == SettingKind.Effect)
            {
                AssertTrue(contract.Tests.Length > 0, $"{name} names the test that proves its effect.");
            }

            foreach (string test in contract.Tests)
            {
                AssertTrue(typeof(Program).GetMethod(test, BindingFlags.Static | BindingFlags.NonPublic) is not null,
                    $"{name} points to an existing test: {test}.");
            }
        }

        foreach ((string name, SettingContract contract) in SettingsContract.Where(pair => pair.Value.Kind == SettingKind.Gap))
        {
            Console.WriteLine($"[SETTINGS-GAP] {name}: {contract.Reason}");
        }
    }

    // The folder intake searches subfolders only when Include subfolders is on.
    private static void TestIncludeSubfoldersControlsFolderScan()
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "subfolders-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "Disc Set");
        Directory.CreateDirectory(nested);
        try
        {
            string top = Path.Combine(root, "Top Game.iso");
            string inner = Path.Combine(nested, "Inner Game.iso");
            File.WriteAllBytes(top, [1]);
            File.WriteAllBytes(inner, [2]);

            var resolver = new InputResolver();
            string[] topOnly = [.. resolver.Resolve(root, SearchOption.TopDirectoryOnly)];
            string[] all = [.. resolver.Resolve(root, SearchOption.AllDirectories)];
            AssertTrue(topOnly.Contains(top, StringComparer.OrdinalIgnoreCase) && !topOnly.Contains(inner, StringComparer.OrdinalIgnoreCase),
                "Off: only the files in the chosen folder are added.");
            AssertTrue(all.Contains(top, StringComparer.OrdinalIgnoreCase) && all.Contains(inner, StringComparer.OrdinalIgnoreCase),
                "On: files in subfolders are added too.");

            string intake = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "MainVM.Queue.cs")));
            AssertTrue(intake.Contains("_session.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly", StringComparison.Ordinal),
                "The queue intake maps the setting to the search option.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A valid external chdman is used when chosen; an unusable one falls back to the bundled chdman.
    private static void TestChdmanPathFollowsTheSetting()
    {
        string bundled = RequireBundledChdman();
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "chdman-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string external = Path.Combine(root, "chdman.exe");
            File.Copy(bundled, external);
            var resolver = new ChdmanPathResolver(RuntimeToolService.Instance);

            string chosen = resolver.ResolvePath(new AppSettings { UseBundledChdman = false, ExternalChdmanPath = external });
            AssertEqual(Path.GetFullPath(external), chosen, "A valid external chdman is used when chosen.");

            AssertTrue(ChdmanPathResolver.TryResolveExternal(external, out _), "The copied chdman is a usable external path.");
            AssertFalse(ChdmanPathResolver.TryResolveExternal(Path.Combine(root, "missing.exe"), out _), "A missing file is not usable.");
            AssertFalse(ChdmanPathResolver.TryResolveExternal(Path.Combine(root, "chdman.txt"), out _), "A file that is not an .exe is not usable.");

            string resolverSource = File.ReadAllText(FindRepoFile(Path.Combine("Services", "ChdPathRes.cs")));
            AssertTrue(resolverSource.Contains("External chdman is not usable; the bundled chdman is used instead.", StringComparison.Ordinal),
                "Falling back from an unusable external chdman is logged.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // "Force DVD" makes an ISO use createdvd; Auto lets the media evidence decide.
    private static void TestIsoOverrideForceDvdUsesCreateDvd()
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "iso-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string iso = Path.Combine(root, "Sample Disc.iso");
            File.WriteAllBytes(iso, new byte[2048 * 400]);

            var forced = IsoChdmanCreateCommandResolver.ResolveCreateCompressionCommandWithDiagnostics(
                iso, HakamiqChdTool.App.Models.Chd.IsoCreateCommandOverride.CreateDvd);
            AssertEqual("createdvd", forced.Command, "Force DVD uses createdvd.");

            var auto = IsoChdmanCreateCommandResolver.ResolveCreateCompressionCommandWithDiagnostics(
                iso, HakamiqChdTool.App.Models.Chd.IsoCreateCommandOverride.Auto);
            AssertEqual(auto.AutoSuggestedCommand, auto.Command, "Auto uses the command the media evidence suggests.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The hidden thread settings: with the limiter off, Auto passes no thread count; a larger reserve never
    // gives chdman more threads.
    private static void TestAutoLimiterAndReservedCoresShapeTheThreadCount()
    {
        var safe = HakamiqChdTool.App.Models.Chd.ConversionPerformanceMode.Safe;
        AssertEqual(0, ProcessorTopologyService.ResolveChdmanProcessorCount(0, false, 0, safe), "Limiter off: Auto leaves the thread count to chdman.");
        AssertTrue(ProcessorTopologyService.ResolveChdmanProcessorCount(0, true, 0, safe) >= 1, "Limiter on: Auto picks a thread count.");

        int available = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        int noReserve = ProcessorTopologyService.ResolveChdmanProcessorCount(0, true, 0, safe);
        int bigReserve = ProcessorTopologyService.ResolveChdmanProcessorCount(0, true, Math.Max(1, available - 1), safe);
        AssertTrue(bigReserve <= noReserve && bigReserve >= 1, "A larger reserve never gives chdman more threads.");
    }

    // The M3U settings reach the generator after a run: off writes nothing, Overwrite replaces an existing list.
    private static void TestM3uSettingsReachTheGenerator()
    {
        string post = File.ReadAllText(FindRepoFile(Path.Combine("Views", "MainWindow", "MW.Post.cs")));
        AssertTrue(post.Contains("if (!_settings.EnableAutoM3uGeneration || outputPaths.Count == 0)", StringComparison.Ordinal),
            "No playlist is written while the M3U option is off.");
        AssertTrue(post.Contains("_settings.OverwriteExistingM3uPlaylists);", StringComparison.Ordinal),
            "The overwrite option is passed to the generator.");

        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "m3u-overwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string disc1 = Path.Combine(root, "Sample Game (USA) (Disc 1).chd");
            string disc2 = Path.Combine(root, "Sample Game (USA) (Disc 2).chd");
            File.WriteAllBytes(disc1, [1]);
            File.WriteAllBytes(disc2, [2]);
            var sets = new HakamiqChdTool.App.Services.M3u.MultiDiscSetDetector().Detect([disc1, disc2]);
            var generator = new HakamiqChdTool.App.Services.M3u.M3uPlaylistGenerator();

            string playlist = generator.Generate(sets, overwriteExisting: false).GeneratedPaths.Single();
            File.WriteAllText(playlist, "edited by the user");
            var replaced = generator.Generate(sets, overwriteExisting: true);
            AssertTrue(replaced.GeneratedCount == 1 && File.ReadAllText(playlist).Contains("(Disc 1).chd", StringComparison.Ordinal),
                "Overwrite on replaces an existing playlist.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
