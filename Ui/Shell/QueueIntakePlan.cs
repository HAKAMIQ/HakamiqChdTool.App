using System.Collections.Generic;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed record QueueIntakePlan(
    int TotalItems,
    int ReadyItems,
    int ReviewItems,
    int RejectedItems,
    int DuplicateItems,
    IReadOnlyList<ShellQueueItem> ReadyItemsSnapshot,
    IReadOnlyList<ShellQueueItem> ReviewItemsSnapshot,
    IReadOnlyList<ShellQueueItem> RejectedItemsSnapshot)
{
    public int ProcessableItems => ReadyItems + ReviewItems;

    public bool HasProcessableItems => ProcessableItems > 0;

    public bool RequiresPreExecutionReview => ReviewItems > 0;

    public static QueueIntakePlan Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        [],
        [],
        []);
}
