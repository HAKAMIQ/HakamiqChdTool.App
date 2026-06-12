using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed record ShellState(
    string ProductName,
    string ShellMode,
    string HeaderTitle,
    string HeaderSubtitle,
    string QueueTitle,
    string QueueSummaryText,
    int TotalQueueItems,
    int PendingCount,
    int ActiveCount,
    int CompletedCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<ShellQueueItem> QueueItems,
    IReadOnlyList<ShellStatus> StatusItems,
    string FooterText,
    DateTimeOffset RefreshedAt)
{
    public static ShellState Empty(string productName, string shellMode)
    {
        return new ShellState(
            productName,
            shellMode,
            productName,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<ShellQueueItem>(),
            Array.Empty<ShellStatus>(),
            string.Empty,
            DateTimeOffset.Now);
    }
}
