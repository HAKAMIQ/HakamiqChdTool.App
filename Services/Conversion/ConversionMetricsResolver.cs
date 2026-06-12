using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

internal static class ConversionMetricsResolver
{
    private static readonly Regex LogicalSizeRegex = new(
        @"\bLogical\s+size:\s*([0-9][0-9,]*)\s*(?:bytes)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParseLogicalSizeBytes(string? text, out long bytes)
    {
        bytes = 0L;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        MatchCollection matches = LogicalSizeRegex.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        for (int index = matches.Count - 1; index >= 0; index--)
        {
            string normalized = matches[index].Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) && parsed > 0)
            {
                bytes = parsed;
                return true;
            }
        }

        return false;
    }
}
