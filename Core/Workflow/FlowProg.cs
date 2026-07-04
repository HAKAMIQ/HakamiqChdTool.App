using HakamiqChdTool.App.Core.Queue;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowProgressContract
{
    public const double ConversionFinalizingPercent = 99;
    public const double ExtractionFinalizingPercent = 98;

    public static void ReportFinalizing(
        IQueueItemStateSink sink,
        ChdTaskRequest request,
        QueueItemStage stage,
        string statusLine,
        double percent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        double safePercent = Math.Clamp(percent, 95, 99);
        sink.ReportProgress(safePercent, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, safePercent);
        sink.ReportStage(stage, statusLine);
    }
}
