using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

public interface IChdWorkflowOrchestrator
{
    Task<WorkflowExecutionResult> ProcessAsync(ChdTaskRequest request, CancellationToken ct);
}