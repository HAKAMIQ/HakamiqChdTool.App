using System;

namespace HakamiqChdTool.App.Services;

public sealed class ChdInfoResult
{
    public bool IsSuccess { get; init; }
    public bool WasCancelled { get; init; }
    public int ExitCode { get; init; }
    public string MediaType { get; init; } = "Unknown";
    public string SuggestedExtractCommand { get; init; } = string.Empty;
    public long? LogicalBytes { get; init; }

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