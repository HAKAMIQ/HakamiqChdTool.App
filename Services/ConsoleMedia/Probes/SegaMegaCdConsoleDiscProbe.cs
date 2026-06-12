using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class SegaMegaCdConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (context.ContainsText("SEGADISCSYSTEM")
            || context.ContainsText("SEGA MEGA DRIVE")
            || context.ContainsText("SEGA GENESIS"))
        {
            return ConsoleDiscIdentityResult.Create(
                "SEGA CD / Mega-CD",
                90,
                "LocConsoleDiscIdentity_SegaMegaCdHeader",
                "SEGADISCSYSTEM");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
