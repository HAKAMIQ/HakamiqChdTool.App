using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed record ApplicationShellStateSnapshot(
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
    IReadOnlyList<ShellQueueItemSnapshot> QueueItems,
    IReadOnlyList<ShellStatusItem> StatusItems,
    string FooterText,
    DateTimeOffset RefreshedAt)
{
    public static ApplicationShellStateSnapshot Empty(string productName, string shellMode)
    {
        return new ApplicationShellStateSnapshot(
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
            Array.Empty<ShellQueueItemSnapshot>(),
            Array.Empty<ShellStatusItem>(),
            string.Empty,
            DateTimeOffset.Now);
    }
}
