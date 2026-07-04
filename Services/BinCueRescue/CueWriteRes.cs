using System;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record CueRescueWriteResult
{
    private CueRescueWriteResult(
        bool succeeded,
        string? cuePath,
        string? tempDirectoryToCleanup,
        int trackCount,
        CueRescueWriteFailureReason refusalReason)
    {
        if (succeeded)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cuePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(tempDirectoryToCleanup);

            if (trackCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackCount), trackCount, "Track count must be greater than zero.");
            }

            if (refusalReason != CueRescueWriteFailureReason.None)
            {
                throw new ArgumentException("Success result cannot include a failure reason.", nameof(refusalReason));
            }
        }
        else
        {
            if (refusalReason == CueRescueWriteFailureReason.None)
            {
                throw new ArgumentException("Failure result must include a failure reason.", nameof(refusalReason));
            }

            if (!string.IsNullOrWhiteSpace(cuePath))
            {
                throw new ArgumentException("Failure result cannot include a generated CUE path.", nameof(cuePath));
            }

            if (trackCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackCount), trackCount, "Track count cannot be negative.");
            }
        }

        Succeeded = succeeded;
        CuePath = cuePath;
        TempDirectoryToCleanup = tempDirectoryToCleanup;
        TrackCount = trackCount;
        RefusalReason = refusalReason;
    }

    public bool Succeeded { get; }

    public string? CuePath { get; }

    public string? TempDirectoryToCleanup { get; }

    public int TrackCount { get; }

    public CueRescueWriteFailureReason RefusalReason { get; }

    public static CueRescueWriteResult Success(
        string cuePath,
        string tempDirectoryToCleanup,
        int trackCount)
    {
        return new CueRescueWriteResult(
            true,
            cuePath,
            tempDirectoryToCleanup,
            trackCount,
            CueRescueWriteFailureReason.None);
    }

    public static CueRescueWriteResult Fail(
        CueRescueWriteFailureReason refusalReason)
    {
        return new CueRescueWriteResult(
            false,
            null,
            null,
            0,
            refusalReason);
    }
}