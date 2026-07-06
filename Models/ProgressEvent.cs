using System;

namespace HakamiqChdTool.App.Models;

public enum ProgressOperationType
{
    Conversion = 0,
    RedumpScan = 1,
    RedumpScanAll = 2,
    RedumpDatabaseBuild = 3,
    TemporaryNormalization = 4,
    Hashing = 5
}

public sealed record ProgressEvent
{
    public Guid OperationId { get; init; } = Guid.Empty;

    public ProgressOperationType OperationType { get; init; } = ProgressOperationType.Conversion;

    public string ItemName { get; init; } = string.Empty;

    public int CurrentStep { get; init; }

    public int TotalSteps { get; init; }

    public long CurrentBytes { get; init; }

    public long TotalBytes { get; init; }

    public double Percent { get; init; }

    public double SpeedBytesPerSecond { get; init; }

    public TimeSpan? Eta { get; init; }

    public string MessageKey { get; init; } = string.Empty;

    public bool CanCancel { get; init; }
}
