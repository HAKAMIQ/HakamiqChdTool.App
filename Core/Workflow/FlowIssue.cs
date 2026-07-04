namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowPreflightIssue(
    WorkflowPreflightSeverity severity,
    string messageCode,
    string driveRoot,
    long requiredBytes,
    long availableBytes,
    string purpose)
{
    public WorkflowPreflightSeverity Severity { get; } = severity;

    public string MessageCode { get; } = messageCode ?? string.Empty;

    public string DriveRoot { get; } = driveRoot ?? string.Empty;

    public long RequiredBytes { get; } = Math.Max(0, requiredBytes);

    public long AvailableBytes { get; } = Math.Max(0, availableBytes);

    public string Purpose { get; } = purpose ?? string.Empty;
}
