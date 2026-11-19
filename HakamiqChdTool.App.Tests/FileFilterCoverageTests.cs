using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Core.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Every extension the queue accepts for conversion must be selectable in the "Add files" dialogs
    // in both languages, so a new input format cannot ship hidden behind "All files".
    private static void TestFileFiltersCoverEverySupportedInput()
    {
        IEnumerable<string> candidates = ChdPlatformProfiles.All
            .SelectMany(profile => profile.InputExtensions)
            .Concat([".bin", ".zip", ".rar", ".7z", ".zso", ".ccd", ".mds", ".ecm", ".img", ".hdd"])
            .Select(extension => extension.ToLowerInvariant())
            .Distinct();
        string[] supported = [.. candidates.Where(QueueInputClassifier.IsSupportedExtension).Order(StringComparer.Ordinal)];
        AssertTrue(supported.Length >= 15, "The supported extension list is not empty: " + string.Join(",", supported));

        foreach (string language in new[] { "EnglishStrings.xaml", "ArabicStrings.xaml" })
        {
            Dictionary<string, string> strings = LoadStrings(language);
            foreach (string key in new[] { "LocFilter_QuickConvertFiles", "LocInputDialog_Filter_Convert", "LocInputDialog_Filter_Supported" })
            {
                HashSet<string> patterns = ParseFirstFilterPatterns(strings[key]);
                string[] missing = [.. supported.Where(extension => !patterns.Contains("*" + extension))];
                AssertEqual(0, missing.Length, $"{language} {key} lists every supported input; missing: {string.Join(", ", missing)}");
            }

            AssertTrue(ParseFirstFilterPatterns(strings["LocInputDialog_Filter_Supported"]).Contains("*.chd"), language + ": the general filter also lists CHD.");

            string dropZone = strings["LocUi_DropZone_Convert"];
            foreach (string name in new[] { "ZSO", "CCD", "MDS", "ECM", "IMG" })
            {
                AssertTrue(dropZone.Contains(name, StringComparison.Ordinal), $"{language}: the drop-zone text names {name}.");
            }
        }

        AssertEqual(
            string.Join(";", ParseFirstFilterPatterns(LoadStrings("EnglishStrings.xaml")["LocInputDialog_Filter_Supported"]).Order(StringComparer.Ordinal)),
            string.Join(";", ParseFirstFilterPatterns(LoadStrings("ArabicStrings.xaml")["LocInputDialog_Filter_Supported"]).Order(StringComparer.Ordinal)),
            "Arabic and English filters offer the same file types.");
    }

    private static HashSet<string> ParseFirstFilterPatterns(string filter)
    {
        string[] parts = filter.Split('|');
        AssertTrue(parts.Length >= 2 && parts.Length % 2 == 0, "A file filter has description|pattern pairs: " + filter);
        return parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
