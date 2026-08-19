using System;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record BinCueRescueTrackPlan(
    int TrackNumber,
    string SourceBinPath,
    BinTrackKind Kind)
{
    public int TrackNumber { get; init; } =
        TrackNumber > 0
            ? TrackNumber
            : throw new ArgumentOutOfRangeException(
                nameof(TrackNumber),
                TrackNumber,
                null);

    public string SourceBinPath { get; init; } =
        !string.IsNullOrWhiteSpace(SourceBinPath)
            ? SourceBinPath
            : throw new ArgumentException(
                "A source BIN path is required.",
                nameof(SourceBinPath));

    public string CueTrackMode =>
        Kind switch
        {
            BinTrackKind.Raw2352Mode1 => "MODE1/2352",
            BinTrackKind.Raw2352Mode2 => "MODE2/2352",
            BinTrackKind.Raw2352AudioCandidate => "AUDIO",
            _ => string.Empty
        };

    public bool IsDataTrack =>
        Kind is
            BinTrackKind.Raw2352Mode1
            or BinTrackKind.Raw2352Mode2;

    public bool IsAudioTrack =>
        Kind == BinTrackKind.Raw2352AudioCandidate;
}