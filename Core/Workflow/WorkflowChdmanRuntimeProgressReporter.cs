using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow.Progress;
using HakamiqChdTool.App.Models;
using System;
using System.Diagnostics;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowChdmanRuntimeProgressReporter
{
    private static readonly TimeSpan MinimumEmitInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MaximumEstimatedRemaining = TimeSpan.FromDays(30);

    private readonly IQueueItemStateSink _sink;
    private readonly string _primaryMessageKey;
    private readonly long _totalBytes;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly object _gate = new();

    private long _currentBytes;
    private double _bytesPerSecond;
    private double _percent;
    private bool _hasPercent;
    private long _lastEmitTicks;

    public WorkflowChdmanRuntimeProgressReporter(
        IQueueItemStateSink sink,
        string primaryMessageKey,
        long totalBytes)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _primaryMessageKey = string.IsNullOrWhiteSpace(primaryMessageKey)
            ? string.Empty
            : primaryMessageKey;
        _totalBytes = Math.Max(0, totalBytes);
    }

    public void ReportPercent(int percent)
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            _percent = Math.Clamp(percent, 0, 100);
            _hasPercent = _percent > 0d;
            snapshot = TryBuildSnapshotLocked(force: percent >= 100);
        }

        Report(snapshot);
    }


    public void ReportEstimatedRuntime(WorkflowRuntimeProgressSample sample)
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            _currentBytes = Math.Max(0L, sample.CurrentBytes);
            _bytesPerSecond = double.IsFinite(sample.BytesPerSecond)
                ? Math.Max(0d, sample.BytesPerSecond)
                : 0d;

            if (sample.Percent is double percent && double.IsFinite(percent) && percent > 0d)
            {
                _percent = Math.Clamp(percent, 0d, 99d);
                _hasPercent = true;
            }

            snapshot = TryBuildSnapshotLocked(force: false);
        }

        Report(snapshot);
    }

    public void ReportPerformance(PerformanceSample sample)
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            _currentBytes = Math.Max(0, sample.OutputBytes);
            _bytesPerSecond = double.IsFinite(sample.OutputWriteBytesPerSecond)
                ? Math.Max(0d, sample.OutputWriteBytesPerSecond)
                : 0d;
            snapshot = TryBuildSnapshotLocked(force: false);
        }

        Report(snapshot);
    }

    public void ReportCurrent()
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            snapshot = TryBuildSnapshotLocked(force: true);
        }

        Report(snapshot);
    }

    private QueueRuntimeProgressSnapshot? TryBuildSnapshotLocked(bool force)
    {
        long nowTicks = Stopwatch.GetTimestamp();
        TimeSpan sinceLast = _lastEmitTicks <= 0
            ? MinimumEmitInterval
            : Stopwatch.GetElapsedTime(_lastEmitTicks, nowTicks);

        if (!force && sinceLast < MinimumEmitInterval)
        {
            return null;
        }

        _lastEmitTicks = nowTicks;

        TimeSpan elapsed = _elapsed.Elapsed;
        TimeSpan remaining = EstimateRemaining(elapsed);

        return new QueueRuntimeProgressSnapshot
        {
            Kind = QueueRuntimeProgressKind.ChdmanOperation,
            PrimaryMessageKey = _primaryMessageKey,
            CurrentBytes = _currentBytes,
            TotalBytes = _totalBytes,
            BytesPerSecond = _bytesPerSecond,
            Percent = _hasPercent ? _percent : 0d,
            Elapsed = elapsed,
            EstimatedRemaining = remaining,
            ShowActivitySpinner = true
        };
    }

    private TimeSpan EstimateRemaining(TimeSpan elapsed)
    {
        if (_hasPercent && _percent > 0d && _percent < 100d)
        {
            double remainingSeconds = elapsed.TotalSeconds * ((100d - _percent) / _percent);
            return double.IsFinite(remainingSeconds) && remainingSeconds > 0d
                ? TimeSpan.FromSeconds(Math.Min(remainingSeconds, MaximumEstimatedRemaining.TotalSeconds))
                : TimeSpan.Zero;
        }

        if (_totalBytes > 0 && _currentBytes > 0 && _currentBytes < _totalBytes && _bytesPerSecond > 0d)
        {
            double remainingSeconds = (_totalBytes - _currentBytes) / _bytesPerSecond;
            return double.IsFinite(remainingSeconds) && remainingSeconds > 0d
                ? TimeSpan.FromSeconds(Math.Min(remainingSeconds, MaximumEstimatedRemaining.TotalSeconds))
                : TimeSpan.Zero;
        }

        return TimeSpan.Zero;
    }

    private void Report(QueueRuntimeProgressSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        _sink.ReportRuntimeProgress(snapshot);
    }
}
