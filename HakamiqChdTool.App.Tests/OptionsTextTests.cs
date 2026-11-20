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
