using System;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record BinCueRescueTrackPlan(
    int TrackNumber,
    string SourceBinPath,
    BinTrackKind Kind,
    string CueTrackMode,
    bool IsDataTrack,
    bool IsAudioTrack)
{
    public int TrackNumber { get; init; } =
        TrackNumber > 0
            ? TrackNumber
            : throw new ArgumentOutOfRangeException(nameof(TrackNumber), TrackNumber, null);

    public string SourceBinPath { get; init; } =
        !string.IsNullOrWhiteSpace(SourceBinPath)
            ? SourceBinPath
            : throw new ArgumentException("A source BIN path is required.", nameof(SourceBinPath));

    public string CueTrackMode { get; init; } = NormalizeCueTrackMode(
        CueTrackMode,
        IsDataTrack,
        IsAudioTrack);

    public bool IsDataTrack { get; init; } =
        IsDataTrack && IsAudioTrack
            ? throw new ArgumentException("A BIN/CUE track cannot be both data and audio.")
            : IsDataTrack;

    public bool IsAudioTrack { get; init; } =
        IsDataTrack && IsAudioTrack
            ? throw new ArgumentException("A BIN/CUE track cannot be both data and audio.")
            : IsAudioTrack;

    private static string NormalizeCueTrackMode(
        string? cueTrackMode,
        bool isDataTrack,
        bool isAudioTrack)
    {
        string normalized = cueTrackMode?.Trim() ?? string.Empty;

        if (!isDataTrack && !isAudioTrack)
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A CUE track mode is required for usable BIN/CUE tracks.", nameof(cueTrackMode));
        }

        return normalized;
    }
}