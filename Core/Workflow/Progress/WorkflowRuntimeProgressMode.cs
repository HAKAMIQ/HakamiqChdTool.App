namespace HakamiqChdTool.App.Core.Workflow.Progress;

internal enum WorkflowRuntimeProgressMode
{
    Unknown = 0,
    ReportedByTool = 1,
    EstimatedFromOutputBytes = 2,
    Indeterminate = 3
}
