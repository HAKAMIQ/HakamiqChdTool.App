namespace HakamiqChdTool.App.Core.Workflow.Progress;

internal interface IWorkflowRuntimeProgressEstimator
{
    WorkflowRuntimeProgressSample Capture();
}
