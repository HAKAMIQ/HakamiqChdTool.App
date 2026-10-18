using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Text pairs the shell and the Library window render. WCAG 2.1 AA for normal text is 4.5:1.
    private static readonly (string Foreground, string Background)[] ThemeTextPairs =
    [
        ("Text.Primary", "Layer.Canvas"), ("Text.Primary", "Layer.Surface"), ("Text.Primary", "Layer.Card"),
        ("Text.Secondary", "Layer.Canvas"), ("Text.Secondary", "Layer.Surface"), ("Text.Secondary", "Layer.Card"),
        ("Text.Muted", "Layer.Canvas"), ("Text.Muted", "Layer.Card"),
        ("Success", "Layer.Canvas"), ("Success", "Layer.Card"),
        ("Warning", "Layer.Canvas"), ("Warning", "Layer.Card"),
        ("Error", "Layer.Canvas"),
        ("Accent.Text", "Layer.Canvas"), ("Accent.Text", "Layer.Card"),
        ("Accent.Foreground", "Accent"),
        ("Accent.SelectedForeground", "Accent.SelectedBackground"),
        ("SuccessBadgeForeground", "SuccessBadge"), ("WarningBadgeForeground", "WarningBadge"), ("ErrorBadgeForeground", "ErrorBadge")
    ];

    private static void TestThemeTextContrastMeetsAa()
    {
        foreach (string theme in new[] { "Light", "Dark" })
        {
            string xaml = File.ReadAllText(FindRepoFile(Path.Combine("Resources", "Themes", theme + ".xaml")));
            Dictionary<string, string> colors = [];

            foreach (Match match in Regex.Matches(xaml, @"<Color x:Key=""Color\.([^""]+)"">#([0-9A-Fa-f]{8})</Color>"))
            {
                colors[match.Groups[1].Value] = match.Groups[2].Value;
            }

            foreach ((string foreground, string background) in ThemeTextPairs)
            {
                AssertTrue(colors.ContainsKey(foreground) && colors.ContainsKey(background), $"{theme}: token {foreground} / {background} exists.");
                double ratio = ContrastRatio(colors[foreground], colors[background]);
                AssertTrue(ratio >= 4.5, $"{theme}: {foreground} on {background} is {ratio:F2}:1, below the 4.5:1 AA minimum.");
            }
        }
    }

    // Control boundaries and state surfaces: WCAG 2.1 non-text contrast is 3:1, text on hover/pressed stays at 4.5:1.
    private static readonly (string Foreground, string Background, double Minimum)[] ThemeStatePairs =
    [
        ("Border.Strong", "Layer.Canvas", 3.0), ("Border.Strong", "Layer.Surface", 3.0), ("Border.Strong", "Layer.Card", 3.0),
        ("Text.Primary", "HoverSurface", 4.5), ("Text.Secondary", "HoverSurface", 4.5), ("Accent.Text", "HoverSurface", 4.5),
        ("Text.Primary", "PressedSurface", 4.5), ("Text.Secondary", "PressedSurface", 4.5)
    ];

    private static void TestThemeStateAndBorderContrast()
    {
        foreach (string theme in new[] { "Light", "Dark" })
        {
            string xaml = File.ReadAllText(FindRepoFile(Path.Combine("Resources", "Themes", theme + ".xaml")));
            Dictionary<string, string> colors = [];

            foreach (Match match in Regex.Matches(xaml, @"<Color x:Key=""Color\.([^""]+)"">#([0-9A-Fa-f]{8})</Color>"))
            {
                colors[match.Groups[1].Value] = match.Groups[2].Value;
            }

            foreach ((string foreground, string background, double minimum) in ThemeStatePairs)
            {
                AssertTrue(colors.ContainsKey(foreground) && colors.ContainsKey(background), $"{theme}: token {foreground} / {background} exists.");
                double ratio = ContrastRatio(colors[foreground], colors[background]);
                AssertTrue(ratio >= minimum, $"{theme}: {foreground} on {background} is {ratio:F2}:1, below the {minimum}:1 minimum.");
            }
        }
    }

    private static double ContrastRatio(string argbA, string argbB)
    {
        double a = RelativeLuminance(argbA);
        double b = RelativeLuminance(argbB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(string argb)
    {
        double Channel(int offset)
        {
            double c = int.Parse(argb.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(2) + 0.7152 * Channel(4) + 0.0722 * Channel(6);
    }
}
