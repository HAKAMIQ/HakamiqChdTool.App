using System;
using System.Text.RegularExpressions;
using HakamiqChdTool.App.Core.Verification;

namespace HakamiqChdTool.App.Core.Chd;

// Disc structure from `chdman info` metadata tags only. No size or text heuristics: a CHD
// without exactly one kind of disc metadata is Unknown, so verification fails closed.
public static partial class ChdMetaTags
{
    public static DiscStructure DetectStructure(string? chdmanInfoOutput)
    {
        if (string.IsNullOrWhiteSpace(chdmanInfoOutput))
        {
            return DiscStructure.Unknown;
        }

        bool cd = false;
        bool gdRom = false;
        bool dvd = false;

        foreach (Match match in MetadataTag().Matches(chdmanInfoOutput))
        {
            switch (match.Groups["tag"].Value)
            {
                case "CHT2" or "CHTR" or "CHCD":
                    cd = true;
                    break;

                case "CHGD" or "CHGT":
                    gdRom = true;
                    break;

                case "DVD ":
                    dvd = true;
                    break;
            }
        }

        return (cd, gdRom, dvd) switch
        {
            (true, false, false) => DiscStructure.CdTracks,
            (false, true, false) => DiscStructure.GdRomTracks,
            (false, false, true) => DiscStructure.SingleImage2048,
            _ => DiscStructure.Unknown
        };
    }

    [GeneratedRegex(@"^\s*Metadata:\s*Tag\s*=\s*['""](?<tag>.{4})['""]", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataTag();
}
