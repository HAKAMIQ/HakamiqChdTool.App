using System.IO;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The Danger button (Clear log, Orphan clean) takes its colours from the active theme like every
    // other control, instead of fixed brushes that a theme switch cannot reach.
    private static void TestDangerButtonUsesThemeBrushes()
    {
        string styles = File.ReadAllText(FindRepoFile(Path.Combine("Resources", "Themes", "Controls", "ButtonStyles.xaml")));
        Match style = Regex.Match(styles, @"<Style x:Key=""DangerButtonStyle"".*?</Style>", RegexOptions.Singleline);
        AssertTrue(style.Success, "DangerButtonStyle exists.");
        foreach (string key in new[] { "ButtonDangerBrush", "ButtonDangerForegroundBrush", "ButtonDangerHoverBrush", "ButtonDangerPressedBrush" })
        {
            AssertTrue(style.Value.Contains("{DynamicResource " + key + "}", System.StringComparison.Ordinal), $"DangerButtonStyle uses the themed {key}.");
            foreach (string theme in new[] { "Light", "Dark" })
            {
                string palette = File.ReadAllText(FindRepoFile(Path.Combine("Resources", "Themes", theme + ".xaml")));
                AssertTrue(palette.Contains("x:Key=\"" + key + "\"", System.StringComparison.Ordinal), $"{theme} defines {key}.");
            }
        }

        AssertFalse(Regex.IsMatch(style.Value, @"Color=""#|StaticResource FluentButton\.Danger"), "DangerButtonStyle has no fixed colours.");
        AssertFalse(styles.Contains("FluentButton.Danger.", System.StringComparison.Ordinal), "The fixed Danger brushes are gone.");
    }
}
