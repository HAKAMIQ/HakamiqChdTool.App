namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowPreflightResult
{
    private static readonly WorkflowPreflightResult Successful = new([]);

    private WorkflowPreflightResult(IReadOnlyList<WorkflowPreflightIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<WorkflowPreflightIssue> Issues { get; }

    public bool IsBlocker => Issues.Any(static issue => issue.Severity == WorkflowPreflightSeverity.Blocker);

    public bool HasWarnings => Issues.Any(static issue => issue.Severity == WorkflowPreflightSeverity.Warning);

    public WorkflowPreflightIssue? FirstBlocker =>
        Issues.FirstOrDefault(static issue => issue.Severity == WorkflowPreflightSeverity.Blocker);

    public static WorkflowPreflightResult Success() => Successful;

    public static WorkflowPreflightResult WithIssues(IEnumerable<WorkflowPreflightIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        List<WorkflowPreflightIssue> materialized =
        [
            .. issues.Where(static issue => issue.Severity != WorkflowPreflightSeverity.Pass)
        ];

        return materialized.Count == 0
            ? Successful
            : new WorkflowPreflightResult([.. materialized]);
    }
}
