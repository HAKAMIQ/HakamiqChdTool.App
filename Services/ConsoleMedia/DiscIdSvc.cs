using System;
using System.Collections.Generic;
using HakamiqChdTool.App.Services.ConsoleMedia.Probes;

namespace HakamiqChdTool.App.Services.ConsoleMedia;

internal sealed class ConsoleDiscIdentityService
{
    private const int MinimumAcceptedConfidence = 60;

    public static ConsoleDiscIdentityService Shared { get; } = new();

    private readonly IReadOnlyList<IConsoleDiscIdentityProbe> _probes;

    public ConsoleDiscIdentityService()
        : this([
            new PlayStationConsoleDiscProbe(),
            new SegaSaturnConsoleDiscProbe(),
            new SegaMegaCdConsoleDiscProbe(),
            new DreamcastConsoleDiscProbe(),
            new NeoGeoCdConsoleDiscProbe(),
            new ThreeDoConsoleDiscProbe(),
            new PcEngineCdConsoleDiscProbe(),
            new PathHintConsoleDiscProbe()
        ])
    {
    }

    private ConsoleDiscIdentityService(IReadOnlyList<IConsoleDiscIdentityProbe> probes)
    {
        _probes = probes ?? throw new ArgumentNullException(nameof(probes));
    }

    public ConsoleDiscIdentityResult Detect(string path)
    {
        if (!ConsoleDiscScanContext.TryCreate(path, out ConsoleDiscScanContext context))
        {
            return ConsoleDiscIdentityResult.Unknown();
        }

        ConsoleDiscIdentityResult best = ConsoleDiscIdentityResult.Unknown();
        foreach (IConsoleDiscIdentityProbe probe in _probes)
        {
            ConsoleDiscIdentityResult result = probe.Probe(context);
            if (result.Confidence > best.Confidence)
            {
                best = result;
            }
        }

        return best.Confidence >= MinimumAcceptedConfidence
            ? best
            : ConsoleDiscIdentityResult.Unknown(best.ReasonKey);
    }
}
