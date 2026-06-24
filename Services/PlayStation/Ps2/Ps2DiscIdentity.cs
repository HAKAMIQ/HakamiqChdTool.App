namespace HakamiqChdTool.App.Services.PlayStation.Ps2;

internal sealed record Ps2DiscIdentity(
    bool IsPlayStation2,
    int Confidence,
    string Serial,
    string Region,
    Ps2DiscMediaKind MediaKind,
    bool IsPathHintOnly)
{
    public static Ps2DiscIdentity Unknown { get; } = new(
        IsPlayStation2: false,
        Confidence: 0,
        Serial: string.Empty,
        Region: string.Empty,
        MediaKind: Ps2DiscMediaKind.Unknown,
        IsPathHintOnly: false);
}
