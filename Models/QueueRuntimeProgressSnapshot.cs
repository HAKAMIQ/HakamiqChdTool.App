namespace HakamiqChdTool.App.Models;

public enum QueueRuntimeProgressKind
{
    None = 0,
    ChdmanOperation = 1
}

public sealed record QueueRuntimeProgressSnapshot
{
    public static QueueRuntimeProgressSnapshot Empty { get; } = new();

    public QueueRuntimeProgressKind Kind { get; init; } = QueueRuntimeProgressKind.None;

    public string PrimaryMessageKey { get; init; } = string.Empty;

    public long CurrentBytes { get; init; }

    public long TotalBytes { get; init; }

    public double BytesPerSecond { get; init; }

    public double Percent { get; init; }

    public TimeSpan Elapsed { get; init; }

    public TimeSpan EstimatedRemaining { get; init; }

    public string NextStageMessageKey { get; init; } = string.Empty;

    public bool ShowActivitySpinner { get; init; }

    public bool HasRuntimeDetail => Kind != QueueRuntimeProgressKind.None;
}
