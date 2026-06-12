namespace HakamiqChdTool.App.Core.Workflow;

internal sealed record WorkflowPreparedInput(
    string SourcePath,
    string RequestedAction,
    string DetectedPlatform,
    string? TempDirectoryToCleanup,
    double LastProgressPercent,
    string? LastOutputPath,
    string? LastLogPath,
    bool AlwaysCleanupTempDirectory = false);
