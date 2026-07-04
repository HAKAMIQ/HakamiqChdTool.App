namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record CueRescueWriteOptions(bool AllowConstrainedAbsoluteBinFallback)
{
    public static CueRescueWriteOptions Strict { get; } = new(false);

    public static CueRescueWriteOptions WithConstrainedAbsoluteFallback { get; } = new(true);
}
