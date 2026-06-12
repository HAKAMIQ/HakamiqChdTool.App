using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Models.Chd;

public enum BottleneckKind
{
    Unknown,
    CpuBound,
    DiskIoSuspected,
    MemoryPressure,
    Balanced
}

public sealed record PerformanceSample(
    DateTimeOffset Timestamp,
    int ProcessId,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long SystemAvailableMemoryBytes,
    long OutputBytes,
    double OutputWriteBytesPerSecond,
    BottleneckKind Bottleneck,
    string StatusMessageKey,
    IReadOnlyList<object?> StatusMessageArgs);