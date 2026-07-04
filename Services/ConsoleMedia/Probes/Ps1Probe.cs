using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.ConsoleMedia;

namespace HakamiqChdTool.App.Services.ConsoleMedia.Probes;

internal sealed class PlayStationConsoleDiscProbe : IConsoleDiscIdentityProbe
{
    public ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context)
    {
        if (DiscRawSerialProbe.TryProbe(context.Path, out DiscRawSerialProbeResult serial))
        {
            return ConsoleDiscIdentityResult.Create(
                serial.Platform,
                96,
                "LocConsoleDiscIdentity_PlayStationSerial",
                serial.Serial,
                ConsoleDiscIdentityEvidenceKind.RawSerial);
        }

        if (context.ContainsText("BOOT2") && context.ContainsText("SYSTEM.CNF"))
        {
            return ConsoleDiscIdentityResult.Create(
                "PlayStation 2",
                88,
                "LocConsoleDiscIdentity_PlayStationSystemCnf",
                "BOOT2/SYSTEM.CNF");
        }

        if (context.ContainsText("SYSTEM.CNF")
            && (context.ContainsText("SLUS_") || context.ContainsText("SCUS_") || context.ContainsText("SLES_") || context.ContainsText("SCES_") || context.ContainsText("SLPS_") || context.ContainsText("SLPM_")))
        {
            return ConsoleDiscIdentityResult.Create(
                "PlayStation 1",
                88,
                "LocConsoleDiscIdentity_PlayStationSystemCnf",
                "SYSTEM.CNF");
        }

        return ConsoleDiscIdentityResult.Unknown();
    }
}
