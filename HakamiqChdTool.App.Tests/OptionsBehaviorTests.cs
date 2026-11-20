using System.IO;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.ExternalTools.Igir;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // With several files at once, Auto splits the processors between them; an explicit count is kept.
    private static void TestAutoThreadsAreSharedAcrossConcurrentConversions()
    {
        int single = ProcessorTopologyService.ResolveChdmanProcessorCount(0, true, 0, ConversionPerformanceMode.Fast);
        AssertEqual(0, ProcessorTopologyService.ShareAutoProcessorCount(0, true, 0, ConversionPerformanceMode.Fast, 1),
            "One file at a time leaves Auto as Auto.");
        AssertEqual(6, ProcessorTopologyService.ShareAutoProcessorCount(6, true, 0, ConversionPerformanceMode.Fast, 4),
            "An explicit thread count is kept as chosen.");

        int shared = ProcessorTopologyService.ShareAutoProcessorCount(0, true, 0, ConversionPerformanceMode.Fast, 4);
        AssertEqual(Math.Max(1, single / 4), shared, "Four files share the Auto count.");
        AssertTrue(shared >= 1 && shared * 4 <= Math.Max(4, single), "Four chdman runs together stay within the Auto count.");
    }

    // A disc profile no longer refuses the other disc types in the queue; the hard-disk profile stays strict.
    private static void TestPlatformProfileFallsBackToAutoForOtherInputs()
    {
        AssertTrue(ChdPlatformProfiles.AppliesToInput(ChdPlatformProfiles.Ps1, ".cue"), "The PS1 profile applies to a CUE.");
        AssertFalse(ChdPlatformProfiles.AppliesToInput(ChdPlatformProfiles.Ps1, ".iso"), "The PS1 profile does not apply to an ISO.");
        AssertTrue(ChdPlatformProfiles.AppliesToInput(ChdPlatformProfiles.GenericHardDisk, ".iso"), "The hard-disk profile keeps judging every input.");

        ChdRequestedCreateProfileSelection iso = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            "createdvd", ".iso", "sony-ps1", ChdmanExtractionKind.None, @"C:\Samples\game.iso", @"C:\Samples\game.chd");
        AssertTrue(iso.FailureResult is null && iso.Profile is null && iso.Command == "createdvd",
            "An ISO with the PS1 profile selected converts with Auto instead of failing.");

        ChdRequestedCreateProfileSelection cue = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            "createcd", ".cue", "sony-ps1", ChdmanExtractionKind.None, @"C:\Samples\game.cue", @"C:\Samples\game.chd");
        AssertTrue(cue.FailureResult is null && cue.Profile == ChdPlatformProfiles.Ps1, "A CUE still uses the selected PS1 profile.");

        ChdRequestedCreateProfileSelection hdIso = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            "createdvd", ".iso", "generic-hd", ChdmanExtractionKind.None, @"C:\Samples\game.iso", @"C:\Samples\game.chd");
        AssertTrue(hdIso.FailureResult is not null, "The hard-disk profile still refuses an ISO.");
    }

    // A chosen igir that is not the pinned build is reported, and does not switch off the bundled igir.
    private static void TestChosenIgirDoesNotDisableBundledIgir()
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "igir-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string fake = Path.Combine(root, "igir.exe");
            File.WriteAllBytes(fake, [0x4D, 0x5A, 0x00, 0x01]);
            var locator = new IgirToolLocator();

            AssertFalse(locator.IsPinnedBuildAsync(fake).GetAwaiter().GetResult(), "A different igir.exe is not the pinned build.");
            AssertTrue(locator.FindVerifiedAsync(root, fake).GetAwaiter().GetResult() is null,
                "Without a bundled igir, a wrong chosen file finds nothing.");

            string repoRoot = Path.GetDirectoryName(FindRepoFile("HakamiqChdTool.App.csproj"))!;
            string bundled = Path.Combine(repoRoot, "Tools", "igir", "win-x64", "igir.exe");
            if (File.Exists(bundled))
            {
                string? found = locator.FindVerifiedAsync(repoRoot, fake).GetAwaiter().GetResult();
                AssertEqual(Path.GetFullPath(bundled), found, "A wrong chosen file falls back to the bundled igir.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Restore defaults resets the options shown in the window and nothing else: language, theme, chosen tool paths,
    // the Xbox 360 format, Redump sync data and settings with no control keep their values.
    private static void TestRestoreDefaultsKeepsSettingsOutsideTheWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new AppSettings
                {
                    UiLanguage = "en-US",
                    Theme = "Light",
                    ExternalCsoKitPath = @"C:\Tools\csokit.exe",
                    ExternalIgirPath = @"C:\Tools\igir.exe",
                    Xbox360Format = "zar",
                    RedumpLastSyncedUtc = "2026-11-01T10:00:00Z",
                    EnableDiskSpaceGuard = false,
                    ReservedLogicalCores = 4,
                    VerifyAfterConversion = false,
                    IncludeSubfolders = true,
                    MaxConcurrentConversions = 3
                };

                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(source);
                options.ApplyProgramDefaults();
                AppSettings result = options.BuildResultSettings(source);

                AssertTrue(result.VerifyAfterConversion, "A visible option goes back to its default.");
                AssertFalse(result.IncludeSubfolders, "Another visible option goes back to its default.");
                AssertEqual(AppSettings.DefaultMaxConcurrentConversions, result.MaxConcurrentConversions, "Performance options go back to their defaults.");
                AssertEqual("en-US", result.UiLanguage, "The language is kept.");
                AssertEqual("Light", result.Theme, "The theme is kept.");
                AssertEqual(@"C:\Tools\csokit.exe", result.ExternalCsoKitPath, "The chosen CsoKit path is kept.");
                AssertEqual(@"C:\Tools\igir.exe", result.ExternalIgirPath, "The chosen igir path is kept.");
                AssertEqual("zar", result.Xbox360Format, "The Xbox 360 format is kept.");
                AssertEqual("2026-11-01T10:00:00Z", result.RedumpLastSyncedUtc, "Redump sync data is kept.");
                AssertFalse(result.EnableDiskSpaceGuard, "A setting with no control is kept.");
                AssertEqual(4, result.ReservedLogicalCores, "Another setting with no control is kept.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    // Only one Options window is open at a time; a second request (tray menu, automation) brings it forward.
    private static void TestOnlyOneOptionsWindowOpens()
    {
        string modals = File.ReadAllText(FindRepoFile(Path.Combine("Views", "MainWindow", "MW.Modals.cs")));
        AssertTrue(modals.Contains("if (_openOptionsDialog is not null)", StringComparison.Ordinal)
            && modals.Contains("_openOptionsDialog.Activate();", StringComparison.Ordinal), "An open Options window is brought forward instead of opening another.");
        AssertTrue(modals.Contains("_openOptionsDialog = dialog;", StringComparison.Ordinal)
            && modals.Contains("_openOptionsDialog = null;", StringComparison.Ordinal), "The open window is tracked while it is shown and released when it closes.");
    }

    // Opening Options with a Redump database shows the database time but is not a change: Apply stays off and the
    // stored sync time is kept.
    private static void TestOpeningOptionsWithDatabaseIsNotAChange()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new AppSettings { RedumpLastSyncedUtc = "2026-11-01T10:00:00Z" };
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(source);
                options.ShowDatabaseFileTime("2026-11-15T08:30:00Z");

                AssertFalse(options.HasPendingChanges, "Showing the database time does not enable Apply.");
                AssertEqual("2026-11-01T10:00:00Z", options.BuildResultSettings(source).RedumpLastSyncedUtc, "The stored sync time is kept.");

                options.SetDatabaseLastSyncedUtc("2026-11-20T09:00:00Z");
                AssertTrue(options.HasPendingChanges, "A real import is a change.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    // The Redump DAT file and local folder rows show only while Redump is on; a stale value there must not block OK.
    // The download URL has no control at all and never blocks.
    private static void TestHiddenRedumpFieldsDoNotBlockSaving()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                string missing = Path.Combine(Path.GetTempPath(), "hq-missing-" + Guid.NewGuid().ToString("N"));
                var source = new AppSettings
                {
                    EnableDeepIntegrityCheck = false,
                    RedumpDatXmlPath = Path.Combine(missing, "gone.dat"),
                    RedumpLocalLibraryRoot = "relative\\folder",
                    RedumpDatabaseDownloadUrl = "http://example.invalid/db.zip"
                };
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(source);
                AssertTrue(options.CanConfirm, "Hidden Redump fields do not block OK.");
                AssertTrue(options.GetFirstErrorMessage() is null, "No error is reported for hidden fields.");

                options.EnableDeepIntegrityCheck = true;
                AssertFalse(options.CanConfirm, "Once the rows show, a bad DAT path or folder is reported.");

                options.EnableDeepIntegrityCheck = false;
                AssertTrue(options.CanConfirm, "Turning Redump off again clears those errors.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    // A CsoKit or igir path chosen in the Add-ons tab waits for Apply or OK like every other option; Cancel drops it.
    private static void TestChosenToolPathsWaitForApply()
    {
        string tools = File.ReadAllText(FindRepoFile(Path.Combine("Ui", "Shell", "HqOptsTools.cs")));
        AssertFalse(tools.Contains("AppSettingsService", StringComparison.Ordinal) || tools.Contains("_currentSettings.External", StringComparison.Ordinal),
            "Browse in the Add-ons tab no longer saves settings or changes them in place.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new AppSettings { ExternalCsoKitPath = @"C:\Old\csokit.exe" };
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(source);
                AssertFalse(options.HasPendingChanges, "Nothing is pending after opening.");

                options.ExternalCsoKitPath = @"C:\New\csokit.exe";
                options.ExternalIgirPath = @"C:\New\igir.exe";
                AssertTrue(options.HasPendingChanges, "A chosen tool path turns Apply on.");
                AssertEqual(@"C:\Old\csokit.exe", source.ExternalCsoKitPath, "The current settings are untouched until Apply.");

                AppSettings result = options.BuildResultSettings(source);
                AssertEqual(@"C:\New\csokit.exe", result.ExternalCsoKitPath, "Apply saves the chosen CsoKit path.");
                AssertEqual(@"C:\New\igir.exe", result.ExternalIgirPath, "Apply saves the chosen igir path.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    // Scanning or preparing the Redump DAT folder shows its result under the buttons.
    private static void TestRedumpScanShowsItsResult()
    {
        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "RedumpView.xaml")));
        AssertTrue(view.Contains("Text=\"{Binding RedumpLocalLibraryScanSummary}\"", StringComparison.Ordinal), "The scan result is shown in the Redump tab.");
    }
}