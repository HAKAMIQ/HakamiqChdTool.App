using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal enum BinCueRescuePlatformHint
{
    None = 0,
    Raw2352Mode1Data = 1,
    Raw2352Mode2Data = 2
}

internal enum BinCueRescueWarningCode
{
    None = 0,
    MultipleOrderedBinTracksAssumed = 1,
    LeaderCueWriteTargetMissing = 2
}

internal sealed record BinCueRescuePlan(
    BinCueRescueDecision Decision,
    string? AdjacentCuePath,
    string? LeaderCueWriteTarget,
    IReadOnlyList<BinCueRescueTrackPlan> OrderedTracks,
    BinCueRescuePlatformHint? DetectedPlatformHint,
    bool IsAmbiguous,
    IReadOnlyList<BinCueRescueRefusalReason> Refusals,
    IReadOnlyList<BinCueRescueWarningCode> Warnings)
{
    public IReadOnlyList<BinCueRescueTrackPlan> OrderedTracks { get; init; } =
        OrderedTracks is null
            ? []
            : [.. OrderedTracks];

    public IReadOnlyList<BinCueRescueRefusalReason> Refusals { get; init; } =
        Refusals is null
            ? []
            : [.. Refusals];

    public IReadOnlyList<BinCueRescueWarningCode> Warnings { get; init; } =
        Warnings is null
            ? []
            : [.. Warnings];

    public bool CanUseAdjacentCue =>
        Decision == BinCueRescueDecision.UseAdjacentCue
        && !IsAmbiguous
        && !IsRefused
        && !string.IsNullOrWhiteSpace(AdjacentCuePath);

    public bool CanGenerateTempCue =>
        Decision == BinCueRescueDecision.GenerateTempCue
        && !IsAmbiguous
        && !IsRefused
        && !string.IsNullOrWhiteSpace(LeaderCueWriteTarget)
        && OrderedTracks.Count > 0
        && OrderedTracks.All(track => track.IsDataTrack || track.IsAudioTrack);

    public bool IsRefused =>
        Decision == BinCueRescueDecision.Refuse
        || Refusals.Count > 0;
}