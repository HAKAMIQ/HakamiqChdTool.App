using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record BinCueRescuePlan(
    BinCueRescueDecision Decision,
    string? AdjacentCuePath,
    string? LeaderCueWriteTarget,
    IReadOnlyList<BinCueRescueTrackPlan> OrderedTracks,
    IReadOnlyList<BinCueRescueRefusalReason> Refusals)
{
    public IReadOnlyList<BinCueRescueTrackPlan> OrderedTracks { get; init; } =
        OrderedTracks is null
            ? []
            : [.. OrderedTracks];

    public IReadOnlyList<BinCueRescueRefusalReason> Refusals { get; init; } =
        Refusals is null
            ? []
            : [.. Refusals];

    public bool CanUseAdjacentCue =>
        Decision == BinCueRescueDecision.UseAdjacentCue
        && !IsRefused
        && !string.IsNullOrWhiteSpace(AdjacentCuePath);

    public bool CanGenerateTempCue =>
        Decision == BinCueRescueDecision.GenerateTempCue
        && !IsRefused
        && !string.IsNullOrWhiteSpace(LeaderCueWriteTarget)
        && OrderedTracks.Count > 0
        && OrderedTracks.All(
            track => track.IsDataTrack || track.IsAudioTrack);

    public bool IsRefused =>
        Decision == BinCueRescueDecision.Refuse
        || Refusals.Count > 0;
}