using System;

namespace HakamiqChdTool.App.Core.Workflow.Progress;

internal sealed record WorkflowRuntimeProgressSample(
    WorkflowRuntimeProgressMode Mode,
    long CurrentBytes,
    long TotalBytes,
    double? Percent,
    double BytesPerSecond,
    DateTimeOffset Timestamp);
