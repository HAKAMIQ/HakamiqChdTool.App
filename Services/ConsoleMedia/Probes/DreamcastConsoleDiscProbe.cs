using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class DreamcastConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (context.ContainsText("SEGA SEGAKATANA")
            || context.ContainsText("SEGA ENTERPRISES") && (context.ContainsText("MIL-CD") || context.ContainsText("GD-ROM")))
        {
            return ConsoleDiscIdentityResult.Create(
                "SEGA Dreamcast",
                88,
                "LocConsoleDiscIdentity_DreamcastHeader",
                "SEGA SEGAKATANA");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
