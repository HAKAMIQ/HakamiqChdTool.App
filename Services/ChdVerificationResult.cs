namespace HakamiqChdTool.App.Services;

public enum ChdVerificationStatus
{
    Valid,
    Invalid,
    Cancelled,
    ToolStartFailed,
    ToolExecutionFailed,
}

public sealed class ChdVerificationResult
{
    public ChdVerificationStatus Status { get; init; }
    public bool IsSuccess { get; init; }
    public bool WasCancelled { get; init; }
    public int ExitCode { get; init; }
    public string CommandLine { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
}
