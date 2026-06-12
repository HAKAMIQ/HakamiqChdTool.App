using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class PcEngineCdConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (context.ContainsText("PC Engine") || context.ContainsText("TurboGrafx"))
        {
            return ConsoleDiscIdentityResult.Create(
                "PC Engine CD / TurboGrafx-CD",
                74,
                "LocConsoleDiscIdentity_PcEngineCdHeader",
                "PC Engine");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
