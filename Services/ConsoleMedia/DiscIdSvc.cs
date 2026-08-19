using HakamiqChdTool.App.Services.ConsoleMedia.Probes;

namespace HakamiqChdTool.App.Services.ConsoleMedia;

internal sealed class ConsoleDiscIdentityService
{
    private readonly IConsoleDiscIdentityProbe[] _probes =
    [
        new PlayStationConsoleDiscProbe(),
        new SegaSaturnConsoleDiscProbe(),
        new SegaMegaCdConsoleDiscProbe(),
        new DreamcastConsoleDiscProbe(),
        new NeoGeoCdConsoleDiscProbe(),
        new ThreeDoConsoleDiscProbe(),
        new PcEngineCdConsoleDiscProbe(),
        new PathHintConsoleDiscProbe()
    ];

    public static ConsoleDiscIdentityService Shared { get; } =
        new();

    private ConsoleDiscIdentityService()
    {
    }

    public ConsoleDiscIdentityResult Detect(string path)
    {
        if (!ConsoleDiscScanContext.TryCreate(
                path,
                out ConsoleDiscScanContext context))
        {
            return ConsoleDiscIdentityResult.Unknown();
        }

        ConsoleDiscIdentityResult best =
            ConsoleDiscIdentityResult.Unknown();

        foreach (IConsoleDiscIdentityProbe probe in _probes)
        {
            ConsoleDiscIdentityResult result =
                probe.Probe(context);

            if (result.Confidence > best.Confidence)
            {
                best = result;
            }
        }

        return best.IsIdentified
            ? best
            : ConsoleDiscIdentityResult.Unknown(best.ReasonKey);
    }
}