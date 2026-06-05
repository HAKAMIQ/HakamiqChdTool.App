using System;

namespace HakamiqChdTool.App.Services;

public sealed record ChdLogicalProbeResult
{
    public static ChdLogicalProbeResult ToolUnavailable(string messageCode) => new()
    {
        MessageCode = messageCode
    };

    public static ChdLogicalProbeResult Failed(string messageCode, int exitCode = 1, bool wasCancelled = false) => new()
    {
        ExitCode = exitCode,
        WasCancelled = wasCancelled,
        MessageCode = messageCode
    };

    public bool IsSuccess { get; init; }

    public bool IsToolAvailable { get; init; }

    public bool WasCancelled { get; init; }

    public int ExitCode { get; init; }

    public string MessageCode { get; init; } = string.Empty;

    public string ToolPath { get; init; } = string.Empty;

    public long PhysicalBytes { get; init; }

    public long LogicalBytes { get; init; }

    public int HunkBytes { get; init; }

    public int TotalHunks { get; init; }

    public long DecodedCacheBytes { get; init; }

    public TimeSpan Elapsed { get; init; }

    public bool HasLogicalGeometry =>
        IsSuccess
        && LogicalBytes > 0
        && HunkBytes > 0
        && TotalHunks > 0;
}
