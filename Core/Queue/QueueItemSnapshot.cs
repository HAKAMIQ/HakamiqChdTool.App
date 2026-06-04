using System;

namespace HakamiqChdTool.App.Core.Queue;

public sealed record QueueItemSnapshot
{
    public required Guid ItemId { get; init; }

    public required string OriginalPath { get; init; }

    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public required string DetectedPlatform { get; init; }

    public required string RequestedAction { get; init; }
}