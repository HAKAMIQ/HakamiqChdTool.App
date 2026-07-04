using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

public sealed class ArchiveContentPreviewResult
{
    public bool WasCancelled { get; init; }

    public bool CanUnpackThenConvert { get; init; }

    public bool RequiresPassword { get; init; }

    public bool IsUnreadable { get; init; }

    public bool ContainsOnlyChd { get; init; }

    public string MessageResourceKey { get; init; } = string.Empty;

    public IReadOnlyList<string> ConvertibleLeaderExtensions { get; init; } = [];

    public static ArchiveContentPreviewResult Cancelled()
    {
        return new ArchiveContentPreviewResult
        {
            WasCancelled = true
        };
    }
}