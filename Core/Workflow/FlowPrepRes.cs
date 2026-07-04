namespace HakamiqChdTool.App.Core.Workflow;

internal sealed record WorkflowPreparationResult
{
    private WorkflowPreparationResult(
        WorkflowPreparedInput? preparedInput,
        WorkflowExecutionResult? failureResult)
    {
        if ((preparedInput is null) == (failureResult is null))
        {
            throw new ArgumentException("Preparation result must contain exactly one result branch.");
        }

        PreparedInput = preparedInput;
        FailureResult = failureResult;
    }

    public WorkflowPreparedInput? PreparedInput { get; }

    public WorkflowExecutionResult? FailureResult { get; }

    public bool IsPrepared => PreparedInput is not null;

    public static WorkflowPreparationResult Prepared(WorkflowPreparedInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new WorkflowPreparationResult(input, null);
    }

    public static WorkflowPreparationResult Failed(WorkflowExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new WorkflowPreparationResult(null, result);
    }
}