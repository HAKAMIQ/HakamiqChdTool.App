using System;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal sealed record CueRescueWriteResult
{
    private CueRescueWriteResult(
        bool succeeded,
        string? cuePath,
        string? tempDirectoryToCleanup)
    {
        if (succeeded)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cuePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(tempDirectoryToCleanup);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(cuePath))
            {
                throw new ArgumentException(
                    "Failure result cannot include a generated CUE path.",
                    nameof(cuePath));
            }

            if (tempDirectoryToCleanup is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    tempDirectoryToCleanup);
            }
        }

        Succeeded = succeeded;
        CuePath = cuePath;
        TempDirectoryToCleanup = tempDirectoryToCleanup;
    }

    public bool Succeeded { get; }

    public string? CuePath { get; }

    public string? TempDirectoryToCleanup { get; }

    public static CueRescueWriteResult Success(
        string cuePath,
        string tempDirectoryToCleanup)
    {
        return new CueRescueWriteResult(
            true,
            cuePath,
            tempDirectoryToCleanup);
    }

    public static CueRescueWriteResult Fail(
        string? tempDirectoryToCleanup = null)
    {
        return new CueRescueWriteResult(
            false,
            null,
            tempDirectoryToCleanup);
    }
}