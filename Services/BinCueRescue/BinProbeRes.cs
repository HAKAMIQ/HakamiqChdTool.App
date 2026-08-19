namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record BinSectorProbeResult(
    BinTrackKind Kind,
    bool HasConfirmedIso9660Pvd = false);