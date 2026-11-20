using System.IO;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Arabic UI text has no diacritics, and Advanced options labels carry no repeated or duplicate words.
    private static void TestOptionsTextIsPlainAndNotRepeated()
    {
        string arabicFile = File.ReadAllText(FindRepoFile(Path.Combine("Resources", "ArabicStrings.xaml")));
        AssertFalse(Regex.IsMatch(arabicFile, "[\\u064B-\\u0652\\u0670]"), "Arabic strings have no tashkeel.");

        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        foreach (string key in new[]
        {
            "LocAdv_TargetEmulatorAutoLabel", "LocAdv_CompressionPresetDefaultLabel", "LocAdv_HunkPresetDefaultLabel",
            "LocAdv_IsoCreateOverrideAutoLabel", "LocAdv_ProcessorAutoRecommendedLabel",
            "LocPerformance_PerformanceMode", "LocPerformance_ProcessorCount", "LocPerformance_ProcessPriority",
            "LocPerformance_CompressionPreset", "LocPerformance_HunkSize"
        })
        {
            AssertFalse(arabic[key].Contains('(') || english[key].Contains('('), $"{key} has no repeated marker or English duplicate in brackets.");
        }

        AssertEqual("Redump", arabic["LocOptions_TabRedump"], "The Redump tab uses the name shown inside it.");
        foreach (string key in new[] { "LocAdv_ProcessorLabel", "LocAdv_ProcessorCounts" })
        {
            AssertTrue(english.ContainsKey(key) && arabic.ContainsKey(key), $"{key} exists in both languages.");
        }
    }

    // Options that could not work are gone: "Manual" behaved exactly like Safe, and "Force CD" for an ISO was
    // always refused by the planner. Saved values load as Safe and Auto, and the delete-source boxes follow
    // "Verify after conversion" because they never delete without it.
    private static void TestOptionsOfferOnlyWorkingChoices()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                AssertFalse(options.PerformanceModeOptions.Any(o => o.Key == "Manual"), "There is no Manual performance mode.");
                AssertFalse(options.IsoCreateOverrideOptions.Any(o => o.Key == "CreateCd"), "There is no Force CD choice for ISO.");

                options.Load(new HakamiqChdTool.App.Models.AppSettings
                {
                    PerformanceMode = HakamiqChdTool.App.Models.Chd.ConversionPerformanceMode.Manual,
                    IsoCreateCommandOverride = HakamiqChdTool.App.Models.Chd.IsoCreateCommandOverride.CreateCd
                });
                AssertEqual("Safe", options.SelectedPerformanceMode?.Key, "A saved Manual mode loads as Safe.");
                AssertEqual("Auto", options.SelectedIsoCreateOverride?.Key, "A saved Force CD loads as Auto.");
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

        string paths = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "PathsView.xaml")));
        AssertEqual(2, Regex.Matches(paths, @"IsChecked=""\{Binding DeleteSourceAfterVerified\w+, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged\}""\s+IsEnabled=""\{Binding VerifyAfterConversion\}""").Count,
            "Both delete-source boxes are enabled only while Verify after conversion is on.");

        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        AssertTrue(english["LocRedumpSettings_ShowInMainDesc"].Contains("reads the whole source", StringComparison.Ordinal),
            "The Redump option says it reads each source before converting.");
        AssertTrue(english["LocProcessing_DeleteFailedOutputDesc"].Contains("CUE/BIN", StringComparison.Ordinal),
            "The failed-output option says what it really deletes.");

        string tools = File.ReadAllText(FindRepoFile(Path.Combine("Ui", "Shell", "HqOptsTools.cs")));
        AssertTrue(tools.Contains("|| !await IsPinnedIgirAsync(selectedPath))", StringComparison.Ordinal),
            "Browsing to an igir.exe that is not the pinned build is refused with a message, not saved.");
    }

    // The app hands the priority setting to the helper tools, tells the user when the watch folder stops,
    // no longer lets a hidden Redump system name re-label imports, shows Latin digits in number lists and
    // says where a hunk preset does not apply.
    private static void TestOptionsFollowUpWiring()
    {
        string app = File.ReadAllText(FindRepoFile("App.xaml.cs"));
        string modals = File.ReadAllText(FindRepoFile(Path.Combine("Views", "MainWindow", "MW.Modals.cs")));
        AssertTrue(app.Contains("HelperToolPriority.Mode = Settings.ChdmanPriorityMode;", StringComparison.Ordinal)
            && modals.Contains("HelperToolPriority.Mode = _settings.ChdmanPriorityMode;", StringComparison.Ordinal),
            "The priority setting reaches the helper tools at startup and after Options is saved.");

        string watch = File.ReadAllText(FindRepoFile(Path.Combine("Views", "MainWindow", "MW.Watch.cs")));
        AssertTrue(watch.Contains("onLost: OnWatchFolderLost", StringComparison.Ordinal)
            && watch.Contains("SetFooterStatus(ArabicUi.Get(WatchFolderUnavailableFooterKey));", StringComparison.Ordinal),
            "A lost watch folder stops watching and shows the footer message.");

        string redump = File.ReadAllText(FindRepoFile(Path.Combine("Ui", "Shell", "HqOptsRed.cs")));
        AssertFalse(redump.Contains("_owner.ViewModel.RedumpSystemName.Trim()", StringComparison.Ordinal),
            "A DAT import is named after the chosen file, not a hidden setting.");

        string perf = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "PerfView.xaml")));
        foreach (string box in new[] { "ProcessorCountComboBox", "ConcurrentConversionCountComboBox" })
        {
            AssertTrue(Regex.IsMatch(perf, "x:Name=\"" + box + "\"\\s+NumberSubstitution.Substitution=\"European\""), $"{box} shows Latin digits.");
        }

        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        foreach (string key in new[] { "LocAdv_HunkPresetSmallDescription", "LocAdv_HunkPresetBalancedDescription", "LocAdv_HunkPresetLargeDescription" })
        {
            AssertTrue(english[key].Contains("PS2 DVD and PSP", StringComparison.Ordinal) && arabic[key].Contains("PS2 DVD", StringComparison.Ordinal),
                $"{key} says the preset is not applied to PS2 DVD and PSP images.");
        }
    }

    // The Redump download button was collapsed, zero-sized and not clickable; it and its handler are gone.
    // Startup auto-sync (off by default) still uses the saved URL.
    private static void TestHiddenRedumpDownloadUiIsRemoved()
    {
        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "RedumpView.xaml")));
        string shell = File.ReadAllText(FindRepoFile(Path.Combine("Ui", "Shell", "HqOptsRed.cs")));
        AssertFalse(view.Contains("DownloadDatabaseButton", StringComparison.Ordinal) || shell.Contains("DownloadDatabaseAsync", StringComparison.Ordinal),
            "The hidden download button and its handler are removed.");

        string autoSync = File.ReadAllText(FindRepoFile(Path.Combine("Services", "RedAutoSync.cs")));
        AssertTrue(autoSync.Contains("settings.RedumpDatabaseDownloadUrl", StringComparison.Ordinal), "Startup auto-sync still reads the saved URL.");
        AssertFalse(new HakamiqChdTool.App.Models.AppSettings().EnableRedumpAutoSync, "Startup auto-sync stays off by default.");
    }

    // The hidden Redump catalog used to rewrite the saved download URL every time Options opened. With it gone,
    // opening and saving Options keeps the URL and the stored catalog values exactly as they were.
    private static void TestOptionsKeepTheSavedRedumpUrl()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var saved = new HakamiqChdTool.App.Models.AppSettings
                {
                    RedumpDatabaseDownloadUrl = "https://github.com/example/sample-dats/archive/main.zip",
                    RedumpPlatformMode = "Platform",
                    RedumpPlatformKey = "psx",
                    RedumpArtifactKind = "Cuesheets"
                };
                var options = new HakamiqChdTool.App.ViewModels.OptionsViewModel();
                options.Load(saved);
                AssertEqual(saved.RedumpDatabaseDownloadUrl, options.RedumpDatabaseDownloadUrl, "Opening Options keeps the saved URL.");

                HakamiqChdTool.App.Models.AppSettings result = options.BuildResultSettings(saved);
                AssertTrue(result.RedumpDatabaseDownloadUrl == saved.RedumpDatabaseDownloadUrl
                    && result.RedumpPlatformMode == "Platform" && result.RedumpPlatformKey == "psx" && result.RedumpArtifactKind == "Cuesheets",
                    "Saving Options keeps the URL and the stored catalog values.");
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

    // All Arabic text stays plain: no "يرجى" (a direct imperative is used), no "سيتم + verbal noun" in sentences,
    // and no English word repeated in brackets after its Arabic word.
    private static void TestArabicTextStaysPlain()
    {
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        string[] pleaseForms = [.. arabic.Where(pair => pair.Value.Contains("يرجى", StringComparison.Ordinal)).Select(pair => pair.Key)];
        AssertEqual(0, pleaseForms.Length, "No Arabic string uses يرجى: " + string.Join(", ", pleaseForms));

        string[] futurePassive = [.. arabic.Where(pair => Regex.IsMatch(pair.Value, "سيتم \\S+ ") && pair.Value.Length > 20).Select(pair => pair.Key)];
        AssertEqual(0, futurePassive.Length, "No Arabic sentence uses سيتم with a verbal noun: " + string.Join(", ", futurePassive));

        var duplicate = new Regex(@"[؀-ۿ]\s\((Hash|Integrity|Metadata|scrambled|Serial|Hunk|Hunk Size|Portable|Reparse Point|Fallback|Legacy|Wrong-profile|Performance Mode|Threads|Priority|Preset|Full Path|Sector Layout)\)");
        string[] repeated = [.. arabic.Where(pair => duplicate.IsMatch(pair.Value)).Select(pair => pair.Key)];
        AssertEqual(0, repeated.Length, "No English duplicate in brackets: " + string.Join(", ", repeated));
    }

    // The CPU name, igir version and Windows integration hints render readably in Arabic.
    private static void TestOptionsWindowLayoutFixes()
    {
        string perf = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "PerfView.xaml")));
        AssertTrue(perf.Contains("<Run Text=\"{Binding ProcessorName, Mode=OneWay}\" FlowDirection=\"LeftToRight\" />", StringComparison.Ordinal),
            "The CPU name is its own left-to-right run, so Arabic text cannot reorder it.");
        AssertTrue(perf.Contains("x:Name=\"ExplorerMenuDescriptionText\"", StringComparison.Ordinal)
            && perf.Contains("Text=\"{DynamicResource LocOptions_ExplorerMenuDesc}\"", StringComparison.Ordinal),
            "The Explorer menu hint (Show more options on Windows 11) is shown, not only a tooltip.");
        AssertTrue(perf.Contains("x:Name=\"WatchFolderDescriptionText\"", StringComparison.Ordinal), "The watch folder hint is shown.");
        AssertTrue(perf.Contains("Visibility=\"{Binding WatchFolderEnabled, Converter={StaticResource BooleanToVisibilityConverter}}\"", StringComparison.Ordinal),
            "The watch folder box appears only when the option is on.");

        string tools = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "ExtTools.xaml")));
        AssertTrue(tools.Contains("Text=\"5.8.1\"", StringComparison.Ordinal) && tools.Contains("NumberSubstitution.Substitution=\"European\"", StringComparison.Ordinal),
            "The igir version uses Latin digits with a gap after the name.");
        AssertTrue(tools.Contains("Text=\"{Binding ExternalToolsIgirVersion}\" Visibility=\"{Binding ExternalToolsIgirIsAvailable", StringComparison.Ordinal),
            "The igir version is hidden when igir is unavailable, so 'unavailable' is not shown twice.");
        AssertFalse(tools.Contains("<Grid Grid.Row=\"2\" FlowDirection=\"LeftToRight\">", StringComparison.Ordinal), "The igir status row follows the app direction.");
    }
}
