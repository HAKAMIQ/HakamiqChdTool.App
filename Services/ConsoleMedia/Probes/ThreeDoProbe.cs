using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class ThreeDoConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (context.ContainsText("3DO") && (context.ContainsText("Opera") || context.ContainsText("3DO CD-ROM")))
        {
            return ConsoleDiscIdentityResult.Create(
                "3DO Interactive Multiplayer",
                76,
                "LocConsoleDiscIdentity_ThreeDoHeader",
                "3DO");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
