using HakamiqChdTool.App.Core.Queue;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowResultBuilder
{
    public static WorkflowExecutionResult Failure(
        QueueItemFailureKind kind,
        string statusDetail,
        string? outputPath,
        string? logPath) =>
        WorkflowExecutionResult.Failure(kind, statusDetail, outputPath, logPath);

    public static WorkflowExecutionResult Success(
        QueueItemTerminalOutcome outcome,
        string statusDetail,
        string? outputPath,
        string? logPath) =>
        WorkflowExecutionResult.Success(outcome, statusDetail, outputPath, logPath);

    public static WorkflowExecutionResult Skipped(
        QueueItemTerminalOutcome outcome,
        string statusDetail,
        string? outputPath,
        string? logPath) =>
        WorkflowExecutionResult.Skipped(outcome, statusDetail, outputPath, logPath);
}
