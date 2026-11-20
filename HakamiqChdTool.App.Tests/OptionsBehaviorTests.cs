using System.Linq;
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

    // A target emulator that overrides an option says so on that option, and the hunk size cannot be chosen while
    // it is fixed.
    private static void TestTargetEmulatorOverridesAreShown()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(new AppSettings());
                AssertTrue(options.CanChooseHunkSize, "With no target the hunk size can be chosen.");

                // Outside the app the texts are not loaded, so the test checks that both rows are told to refresh.
                var changed = new List<string>();
                options.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

                options.SelectedTargetEmulator = options.TargetEmulatorOptions.Single(o => o.Key == HakamiqChdTool.App.Core.Chd.TargetEmulatorPolicy.Dvd2048);
                AssertFalse(options.CanChooseHunkSize, "DVD 2048 fixes the hunk size.");
                AssertTrue(changed.Contains(nameof(options.HunkPresetDescription)) && changed.Contains(nameof(options.CanChooseHunkSize)), "The hunk row refreshes its note.");

                changed.Clear();
                options.SelectedTargetEmulator = options.TargetEmulatorOptions.Single(o => o.Key == HakamiqChdTool.App.Core.Chd.TargetEmulatorPolicy.NoZstd);
                AssertTrue(options.CanChooseHunkSize, "No zstd leaves the hunk size alone.");
                AssertTrue(changed.Contains(nameof(options.CompressionPresetDescription)), "The compression row refreshes its note.");

                string vm = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "OptionsVM.cs")));
                AssertTrue(vm.Contains("ArabicUi.Get(\"LocAdv_HunkSetByTarget\")", StringComparison.Ordinal)
                    && vm.Contains("ArabicUi.Format(\"LocAdv_CompressionNoZstdByTarget\"", StringComparison.Ordinal), "The notes come from the string files.");
            }            catch (Exception ex)
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

    // Options rows: every tab uses the shared row, M3U replacement has a box, and a chosen tool can go back to the
    // bundled copy.
    private static void TestOptionsTabsUseSharedRows()
    {
        foreach (string view in new[] { "ProcView.xaml", "PathsView.xaml", "PerfView.xaml", "WinView.xaml", "RedumpView.xaml", "ExtTools.xaml" })
        {
            string text = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", view)));
            AssertTrue(text.Contains("<opt:OptSection", StringComparison.Ordinal) && text.Contains("<opt:OptRow", StringComparison.Ordinal), $"{view} uses the shared section and row.");
            AssertFalse(text.Contains("Win11SettingsSectionHeader", StringComparison.Ordinal), $"{view} has no full-width header band.");
        }

        string paths = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "PathsView.xaml")));
        AssertTrue(paths.Contains("IsChecked=\"{Binding OverwriteExistingM3uPlaylists, Mode=TwoWay", StringComparison.Ordinal)
            && paths.Contains("IsEnabled=\"{Binding EnableAutoM3uGeneration}\"", StringComparison.Ordinal), "Replacing M3U playlists has its own box, available while playlists are made.");

        string shell = File.ReadAllText(FindRepoFile(Path.Combine("Ui", "Shell", "HqOptsTools.cs")));
        AssertTrue(shell.Contains("_owner.ViewModel.ExternalCsoKitPath = string.Empty;", StringComparison.Ordinal)
            && shell.Contains("_owner.ViewModel.ExternalIgirPath = string.Empty;", StringComparison.Ordinal), "Use bundled clears the chosen path.");
    }

    // The search ignores case and Arabic letter forms, and is wired to the box in the window header. Filtering itself
    // is checked in the window matrix, where the real window has the app's resources.
    private static void TestOptionsSearchFiltersEveryTab()
    {
        AssertEqual("اعداد الملف", HakamiqChdTool.App.Ui.Shell.HqOptionsShell.NormalizeSearchText("إعداد الملف"), "Alef forms match.");
        AssertEqual("مجلد مخصصه", HakamiqChdTool.App.Ui.Shell.HqOptionsShell.NormalizeSearchText("مجلد مخصصة"), "Ta marbuta matches ha.");
        AssertEqual("xbox 360", HakamiqChdTool.App.Ui.Shell.HqOptionsShell.NormalizeSearchText("‎Xbox‎ ‎360‎"), "Direction marks and case do not matter.");

        string window = File.ReadAllText(FindRepoFile(Path.Combine("Views", "OptionsWindow.xaml")));
        AssertTrue(window.Contains("TextChanged=\"OptionsSearchBox_TextChanged\"", StringComparison.Ordinal)
            && window.Contains("AutomationProperties.Name=\"{DynamicResource LocOptions_Search}\"", StringComparison.Ordinal), "The search box filters as you type and has an accessible name.");
        string matrix = File.ReadAllText(FindRepoFile(Path.Combine("HakamiqChdTool.App.Tests", "WindowsMatrixTests.cs")));
        AssertTrue(matrix.Contains("CheckOptionsSearch(searchWindow", StringComparison.Ordinal), "The window matrix runs the search in the real window.");
    }
    // A watch folder that is on needs an existing folder; the error shows under the box and blocks OK.
    private static void TestWatchFolderNeedsAnExistingFolder()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(new AppSettings());
                AssertTrue(options.CanConfirm && options.WatchFolderError.Length == 0, "Off, the folder is not checked.");

                options.WatchFolderEnabled = true;
                AssertFalse(options.CanConfirm, "On with no folder blocks OK.");
                AssertTrue(options.WatchFolderError.Length > 0, "The error is shown under the box.");

                options.WatchFolderPath = Path.Combine(Path.GetTempPath(), "hq-missing-" + Guid.NewGuid().ToString("N"));
                AssertFalse(options.CanConfirm, "A folder that does not exist blocks OK.");

                string existing = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hq-watch-" + Guid.NewGuid().ToString("N"))).FullName;
                try
                {
                    options.WatchFolderPath = existing;
                    AssertTrue(options.CanConfirm && options.WatchFolderError.Length == 0, "An existing folder is accepted.");
                }
                finally
                {
                    Directory.Delete(existing);
                }

                options.CustomOutputRoot = string.Empty;
                options.UseCustomOutputRoot = true;
                AssertTrue(options.CustomOutputRootError.Length > 0, "The output folder error is shown under its box.");
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
}