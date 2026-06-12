using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class NeoGeoCdConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (context.ContainsText("NEO-GEO") || context.ContainsText("NEOGEO"))
        {
            return ConsoleDiscIdentityResult.Create(
                "Neo Geo CD",
                82,
                "LocConsoleDiscIdentity_NeoGeoCdHeader",
                "NEO-GEO");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
