namespace HakamiqChdTool.App.Services;

public sealed class ChdConversionResult
{
    public bool IsSuccess { get; init; }
    public bool WasCancelled { get; init; }
    public int ExitCode { get; init; }
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
}