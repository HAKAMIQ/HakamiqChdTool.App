using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Core.Chd.Commands;

public static partial class ChdmanProgressParser
{
    private static readonly TimeSpan RegexTimeout =
        TimeSpan.FromMilliseconds(250);

    private static readonly Regex ProgressRegex = new(
        @"Compressing,\s*(?<percent>100(?:\.0+)?|\d{1,2}(?:\.\d+)?)\s*%\s+complete(?:\.\.\.)?\s*\(ratio\s*=\s*(?<ratio>\d+(?:\.\d+)?)\s*%\)",
        RegexOptions.Compiled
        | RegexOptions.CultureInvariant
        | RegexOptions.IgnoreCase,
        RegexTimeout);

    public static bool TryParse(
        string? line,
        out double percent,
        out double ratio)
    {
        percent = 0d;
        ratio = 0d;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            MatchCollection matches =
                ProgressRegex.Matches(line);

            if (matches.Count == 0)
            {
                return false;
            }

            Match match = matches[^1];

            if (!double.TryParse(
                    match.Groups["percent"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out percent))
            {
                percent = 0d;
                return false;
            }

            if (!double.TryParse(
                    match.Groups["ratio"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out ratio))
            {
                percent = 0d;
                ratio = 0d;
                return false;
            }

            percent = Math.Clamp(percent, 0d, 100d);
            ratio = Math.Max(0d, ratio);
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            percent = 0d;
            ratio = 0d;
            return false;
        }
    }
}
