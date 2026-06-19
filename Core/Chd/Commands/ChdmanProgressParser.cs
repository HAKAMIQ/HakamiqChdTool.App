using System.Globalization;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Core.Chd.Commands;

public static partial class ChdmanProgressParser
{
    private static readonly Regex ProgressRegex = new(
        @"Compressing,\s+(?<percent>\d+(?:\.\d+)?)%\s+complete\.\.\.\s+\(ratio=(?<ratio>\d+(?:\.\d+)?)%\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(
        string? line,
        out double percent,
        out double ratio)
    {
        percent = 0;
        ratio = 0;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        Match match = ProgressRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        percent = double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture);
        ratio = double.Parse(match.Groups["ratio"].Value, CultureInfo.InvariantCulture);
        return true;
    }
}
