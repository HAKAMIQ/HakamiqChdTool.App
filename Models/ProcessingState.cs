namespace HakamiqChdTool.App.Models;

public enum ProcessingState
{
    Idle,
    Queued,
    AwaitingOperation,
    Processing,
    Skipped,
    Completed,
    Failed
}