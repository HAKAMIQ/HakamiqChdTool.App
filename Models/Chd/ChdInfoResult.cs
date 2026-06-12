using System;

namespace HakamiqChdTool.App.Models.Chd;

public sealed class ChdInfoResult
{
    public bool IsSuccess { get; init; }

    public bool WasCancelled { get; init; }

    public int ExitCode { get; init; }

    public string MediaType { get; init; } = "Unknown";

    public string SuggestedExtractCommand { get; init; } = string.Empty;

    public long? LogicalBytes { get; init; }

    public long? PhysicalBytes { get; init; }

    public int? HunkBytes { get; init; }

    public int? TotalHunks { get; init; }

    public long? DecodedCacheBytes { get; init; }

    public bool LogicalProbeAvailable { get; init; }

    public bool LogicalProbeSucceeded { get; init; }

    public string LogicalProbeMessageCode { get; init; } = string.Empty;

    public bool HasLogicalProbeGeometry =>
        LogicalProbeSucceeded
        && LogicalBytes.GetValueOrDefault() > 0
        && HunkBytes.GetValueOrDefault() > 0
        && TotalHunks.GetValueOrDefault() > 0;

    public double? CompressionRatio =>
        PhysicalBytes.HasValue && LogicalBytes.HasValue && LogicalBytes.Value > 0
            ? PhysicalBytes.Value / (double)LogicalBytes.Value
            : null;

    public double? StorageSavedRatio =>
        CompressionRatio.HasValue
            ? Math.Clamp(1.0 - CompressionRatio.Value, 0.0, 1.0)
            : null;

    public bool IsCdMedia =>
        string.Equals(MediaType, "CD-ROM", StringComparison.OrdinalIgnoreCase);

    public bool IsDvdMedia =>
        string.Equals(MediaType, "DVD-ROM", StringComparison.OrdinalIgnoreCase);

    public bool IsGdRom =>
        string.Equals(MediaType, "GD-ROM", StringComparison.OrdinalIgnoreCase);

    public string CommandLine { get; init; } = string.Empty;

    public string Output { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string LogPath { get; init; } = string.Empty;
}