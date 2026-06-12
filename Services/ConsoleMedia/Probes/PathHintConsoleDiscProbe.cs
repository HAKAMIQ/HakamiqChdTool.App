using System.Linq;
using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class PathHintConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    private static readonly (string Platform, int Confidence, string[] Hints)[] PlatformHints =
    [
        ("PlayStation 1", 68, ["playstation 1", "ps1", "psx"]),
        ("PlayStation 2", 68, ["playstation 2", "ps2"]),
        ("SEGA Saturn", 66, ["sega saturn", "saturn"]),
        ("SEGA CD / Mega-CD", 64, ["sega cd", "mega cd", "megacd"]),
        ("SEGA Dreamcast", 64, ["dreamcast"]),
        ("Neo Geo CD", 62, ["neo geo cd", "neogeo cd"]),
        ("PC Engine CD / TurboGrafx-CD", 62, ["pc engine cd", "turbografx cd", "turbo grafx cd"]),
        ("3DO Interactive Multiplayer", 62, ["3do"])
    ];

    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        foreach ((string platform, int confidence, string[] hints) in PlatformHints)
        {
            if (hints.Any(context.ContainsPathHint))
            {
                return ConsoleDiscIdentityResult.Create(
                    platform,
                    confidence,
                    "LocConsoleDiscIdentity_PathHint",
                    platform,
                    ConsoleDiscIdentityEvidenceKind.PathHint);
            }
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
