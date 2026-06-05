using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed record ShellQueueIntakePlanSnapshot(
    int TotalItems,
    int ReadyItems,
    int ReviewItems,
    int RejectedItems,
    int DuplicateItems,
    IReadOnlyList<ShellQueueItemSnapshot> ReadyItemsSnapshot,
    IReadOnlyList<ShellQueueItemSnapshot> ReviewItemsSnapshot,
    IReadOnlyList<ShellQueueItemSnapshot> RejectedItemsSnapshot)
{
    public int ProcessableItems => ReadyItems + ReviewItems;

    public bool HasProcessableItems => ProcessableItems > 0;

    public bool RequiresPreExecutionReview => ReviewItems > 0;

    public static ShellQueueIntakePlanSnapshot Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        [],
        [],
        []);
}
