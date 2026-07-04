using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class SegaSaturnConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (context.ContainsText("SEGA SEGASATURN"))
        {
            return ConsoleDiscIdentityResult.Create(
                "SEGA Saturn",
                96,
                "LocConsoleDiscIdentity_SegaSaturnHeader",
                "SEGA SEGASATURN");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
