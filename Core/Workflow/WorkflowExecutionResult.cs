using HakamiqChdTool.App.Core.Queue;
using System;

namespace HakamiqChdTool.App.Core.Workflow;

public enum WorkflowExecutionOutcome
{
    Success = 0,
    Skipped = 1,
    Failure = 2,
    Cancelled = 3
}

public sealed class WorkflowExecutionResult
{
    public WorkflowExecutionOutcome Outcome { get; }

    public QueueItemTerminalOutcome? TerminalSuccessOutcome { get; }

    public QueueItemFailureKind? TerminalFailureKind { get; }

    public string? StatusDetail { get; }

    public string? OutputPath { get; }

    public string? LogPath { get; }

    private WorkflowExecutionResult(
        WorkflowExecutionOutcome outcome,
        QueueItemTerminalOutcome? terminalSuccessOutcome,
        QueueItemFailureKind? terminalFailureKind,
        string? statusDetail,
        string? outputPath,
        string? logPath)
    {
        Outcome = outcome;
        TerminalSuccessOutcome = terminalSuccessOutcome;
        TerminalFailureKind = terminalFailureKind;
        StatusDetail = statusDetail;
        OutputPath = outputPath;
        LogPath = logPath;
    }

    public static WorkflowExecutionResult Success(
        QueueItemTerminalOutcome outcome,
        string? statusDetail = null,
        string? outputPath = null,
        string? logPath = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown QueueItemTerminalOutcome value.");
        }

        if (outcome == QueueItemTerminalOutcome.SkippedExists)
        {
            throw new ArgumentException(
                "Use Skipped(...) for skipped terminal results.",
                nameof(outcome));
        }

        return new WorkflowExecutionResult(
            WorkflowExecutionOutcome.Success,
            outcome,
            terminalFailureKind: null,
            statusDetail,
            outputPath,
            logPath);
    }

    public static WorkflowExecutionResult Skipped(
        QueueItemTerminalOutcome outcome,
        string? statusDetail = null,
        string? outputPath = null,
        string? logPath = null)
    {
        if (outcome != QueueItemTerminalOutcome.SkippedExists)
        {
            throw new ArgumentException(
                "Skipped(...) accepts only QueueItemTerminalOutcome.SkippedExists.",
                nameof(outcome));
        }

        return new WorkflowExecutionResult(
            WorkflowExecutionOutcome.Skipped,
            outcome,
            terminalFailureKind: null,
            statusDetail,
            outputPath,
            logPath);
    }

    public static WorkflowExecutionResult Failure(
        QueueItemFailureKind kind,
        string? statusDetail = null,
        string? outputPath = null,
        string? logPath = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown QueueItemFailureKind value.");
        }

        if (kind == QueueItemFailureKind.Cancelled)
        {
            throw new ArgumentException(
                "Use Cancelled(...) for cancellation.",
                nameof(kind));
        }

        return new WorkflowExecutionResult(
            WorkflowExecutionOutcome.Failure,
            terminalSuccessOutcome: null,
            kind,
            statusDetail,
            outputPath,
            logPath);
    }

    public static WorkflowExecutionResult Cancelled(
        string? statusDetail = null,
        string? outputPath = null,
        string? logPath = null)
    {
        return new WorkflowExecutionResult(
            WorkflowExecutionOutcome.Cancelled,
            terminalSuccessOutcome: null,
            QueueItemFailureKind.Cancelled,
            statusDetail,
            outputPath,
            logPath);
    }
}