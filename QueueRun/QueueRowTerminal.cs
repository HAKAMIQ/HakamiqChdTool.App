using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App.QueueRun;

/// <summary>
/// What a window does with a queue item that reached a terminal status: the row is no longer bound to the queue, and a
/// cancelled item (stopped, or cancelled before it started, when no workflow reported it) is shown as cancelled.
/// Shared by the main window and the quick window.
/// </summary>
internal static class QueueRowTerminal
{
    public static bool IsTerminal(ChdQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return TaskQueueStateCodes.IsTerminal(item.Status);
    }

    public static void ApplyCancelled(QueueRowStore rows, ChdQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(item);

        rows.Mutate(item.Id, row =>
        {
            if (string.Equals(item.Status, TaskQueueStateCodes.Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                row.CurrentState = TaskQueueStateCodes.Cancelled;
                row.FinalResult = TaskFinalResultCodes.Cancelled;
                row.StatusDetail = string.IsNullOrWhiteSpace(item.Error)
                    ? "LocQueue_OperationCancelled"
                    : item.Error;

                row.IsProgressActive = false;
                row.IsIndeterminate = false;
                row.RuntimeProgressKind = QueueRuntimeProgressKind.None;
                row.RuntimeProgressPrimaryMessageKey = string.Empty;
                row.RuntimeProgressShowActivitySpinner = false;
            }
        });
    }
}
