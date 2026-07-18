using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow.Progress;
using HakamiqChdTool.App.Models;
using System;
using System.Diagnostics;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowChdmanRuntimeProgressReporter(
    IQueueItemStateSink sink,
    string primaryMessageKey,
    long totalBytes)
{
    private static readonly TimeSpan MinimumEmitInterval =
        TimeSpan.FromMilliseconds(750);

    private static readonly TimeSpan MaximumEstimatedRemaining =
        TimeSpan.FromDays(30);

    private readonly IQueueItemStateSink _sink =
        sink ?? throw new ArgumentNullException(nameof(sink));

    private readonly string _primaryMessageKey =
        string.IsNullOrWhiteSpace(primaryMessageKey)
            ? string.Empty
            : primaryMessageKey;

    private readonly long _totalBytes =
        Math.Max(0L, totalBytes);

    private readonly Stopwatch _elapsed =
        Stopwatch.StartNew();

    private readonly object _gate = new();

    private long _currentBytes;
    private double _bytesPerSecond;
    private double _percent;
    private bool _hasPercent;
    private long _lastEmitTicks;

    public void ReportPercent(int percent)
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            double normalizedPercent =
                Math.Clamp(percent, 0d, 100d);

            if (normalizedPercent > _percent)
            {
                _percent = normalizedPercent;
            }

            _hasPercent = _percent > 0d;

            snapshot = TryBuildSnapshotLocked(
                force: normalizedPercent >= 100d);
        }

        Report(snapshot);
    }

    public void ReportEstimatedRuntime(
        WorkflowRuntimeProgressSample sample)
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            bool acquiredFirstMetrics =
                MergeRuntimeMetricsLocked(
                    sample.CurrentBytes,
                    sample.BytesPerSecond);

            if (sample.Percent is double percent
                && double.IsFinite(percent)
                && percent > 0d)
            {
                double normalizedPercent =
                    Math.Clamp(percent, 0d, 99d);

                if (normalizedPercent > _percent)
                {
                    _percent = normalizedPercent;
                }

                _hasPercent = _percent > 0d;
            }

            snapshot = TryBuildSnapshotLocked(
                force: acquiredFirstMetrics);
        }

        Report(snapshot);
    }

    public void ReportPerformance(
        PerformanceSample sample)
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            bool acquiredFirstMetrics =
                MergeRuntimeMetricsLocked(
                    sample.OutputBytes,
                    sample.OutputWriteBytesPerSecond);

            snapshot = TryBuildSnapshotLocked(
                force: acquiredFirstMetrics);
        }

        Report(snapshot);
    }

    public void ReportCurrent()
    {
        QueueRuntimeProgressSnapshot? snapshot;

        lock (_gate)
        {
            snapshot =
                TryBuildSnapshotLocked(force: true);
        }

        Report(snapshot);
    }

    private bool MergeRuntimeMetricsLocked(
        long currentBytes,
        double bytesPerSecond)
    {
        long normalizedCurrentBytes =
            Math.Max(0L, currentBytes);

        double normalizedBytesPerSecond =
            double.IsFinite(bytesPerSecond)
                ? Math.Max(0d, bytesPerSecond)
                : 0d;

        bool hadRuntimeMetrics =
            _currentBytes > 0L
            || _bytesPerSecond > 0d;

        if (normalizedCurrentBytes > _currentBytes)
        {
            _currentBytes = normalizedCurrentBytes;
        }

        if (normalizedBytesPerSecond > 0d)
        {
            _bytesPerSecond =
                normalizedBytesPerSecond;
        }

        bool hasRuntimeMetrics =
            _currentBytes > 0L
            || _bytesPerSecond > 0d;

        return !hadRuntimeMetrics
            && hasRuntimeMetrics;
    }

    private QueueRuntimeProgressSnapshot?
        TryBuildSnapshotLocked(bool force)
    {
        long nowTicks =
            Stopwatch.GetTimestamp();

        TimeSpan sinceLast =
            _lastEmitTicks <= 0L
                ? MinimumEmitInterval
                : Stopwatch.GetElapsedTime(
                    _lastEmitTicks,
                    nowTicks);

        if (!force
            && sinceLast < MinimumEmitInterval)
        {
            return null;
        }

        _lastEmitTicks = nowTicks;

        TimeSpan elapsed = _elapsed.Elapsed;
        TimeSpan remaining =
            EstimateRemaining(elapsed);

        return new QueueRuntimeProgressSnapshot
        {
            Kind =
                QueueRuntimeProgressKind
                    .ChdmanOperation,

            PrimaryMessageKey =
                _primaryMessageKey,

            CurrentBytes =
                _currentBytes,

            TotalBytes =
                _totalBytes,

            BytesPerSecond =
                _bytesPerSecond,

            Percent =
                _hasPercent
                    ? _percent
                    : 0d,

            Elapsed =
                elapsed,

            EstimatedRemaining =
                remaining,

            ShowActivitySpinner =
                true
        };
    }

    private TimeSpan EstimateRemaining(
        TimeSpan elapsed)
    {
        if (_hasPercent
            && _percent > 0d
            && _percent < 100d)
        {
            double remainingSeconds =
                elapsed.TotalSeconds
                * ((100d - _percent) / _percent);

            return double.IsFinite(
                       remainingSeconds)
                   && remainingSeconds > 0d
                ? TimeSpan.FromSeconds(
                    Math.Min(
                        remainingSeconds,
                        MaximumEstimatedRemaining
                            .TotalSeconds))
                : TimeSpan.Zero;
        }

        if (_totalBytes > 0L
            && _currentBytes > 0L
            && _currentBytes < _totalBytes
            && _bytesPerSecond > 0d)
        {
            double remainingSeconds =
                (_totalBytes - _currentBytes)
                / _bytesPerSecond;

            return double.IsFinite(
                       remainingSeconds)
                   && remainingSeconds > 0d
                ? TimeSpan.FromSeconds(
                    Math.Min(
                        remainingSeconds,
                        MaximumEstimatedRemaining
                            .TotalSeconds))
                : TimeSpan.Zero;
        }

        return TimeSpan.Zero;
    }

    private void Report(
        QueueRuntimeProgressSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        _sink.ReportRuntimeProgress(snapshot);
    }
}