using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Button styles live in one dictionary (Resources/Themes/Controls/ButtonStyles.xaml) plus the
    // implicit style in Fluent/Controls.xaml. Every keyed button style there is in use, so a second
    // parallel set cannot drift back in unnoticed.
    private static void TestButtonStylesAreSingleSourceAndInUse()
    {
        string root = Path.GetDirectoryName(FindRepoFile("HakamiqChdTool.App.sln"))!;
        AssertFalse(File.Exists(Path.Combine(root, "Resources", "Style", "Fluent", "ButtonStyles.xaml")), "The unused Fluent button dictionary is gone.");

        string[] sources = [.. new[] { "Views", "Resources", "Ui", "ViewModels" }
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)];
        AssertFalse(sources.Any(text => text.Contains("Style/Fluent/ButtonStyles.xaml", System.StringComparison.Ordinal)), "No dictionary merges the removed file.");

        string styles = File.ReadAllText(Path.Combine(root, "Resources", "Themes", "Controls", "ButtonStyles.xaml"));
        string[] keys = [.. Regex.Matches(styles, @"<Style x:Key=""([^""]+)""").Select(match => match.Groups[1].Value)];
        AssertTrue(keys.Length >= 5, "Button styles were found: " + string.Join(", ", keys));
        foreach (string key in keys)
        {
            bool used = sources.Any(text => Regex.IsMatch(text, @"Resource\s+" + Regex.Escape(key) + @"\s*\}") || text.Contains("\"" + key + "\"", System.StringComparison.Ordinal));
            AssertTrue(used, $"Button style {key} is referenced somewhere.");
        }
    }
}
