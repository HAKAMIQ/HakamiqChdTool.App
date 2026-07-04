using System;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowStageProgressGate
{
    private const double MaximumRawStep = 2.5;
    private const double MaximumFirstRawStep = 0.75;
    private const double MaximumSoftStep = 0.35;
    private const double MinimumProgressDelta = 0.01;
    private const double ElapsedRatePercentPerSecond = 3.25;
    private const double ElapsedInitialAllowance = 3.0;

    private readonly object _syncRoot = new();
    private readonly double _minimum;
    private readonly double _maximum;
    private readonly int _suspiciousFirstRawPercent;
    private readonly TimeSpan _suspiciousFirstWindow;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private double _lastMapped;
    private bool _hasEmitted;
    private bool _acceptedLowBaseline;

    public WorkflowStageProgressGate(
        double minimum,
        double maximum,
        int suspiciousFirstRawPercent,
        TimeSpan suspiciousFirstWindow)
    {
        _minimum = Math.Clamp(minimum, 0, 100);
        _maximum = Math.Clamp(maximum, _minimum, 100);
        _suspiciousFirstRawPercent = Math.Clamp(suspiciousFirstRawPercent, 1, 100);
        _suspiciousFirstWindow = suspiciousFirstWindow < TimeSpan.Zero ? TimeSpan.Zero : suspiciousFirstWindow;
        _lastMapped = _minimum;
    }

    public bool TryMap(int rawPercent, out double mappedPercent)
    {
        lock (_syncRoot)
        {
            rawPercent = Math.Clamp(rawPercent, 0, 100);
            mappedPercent = _lastMapped;

            if (rawPercent < _suspiciousFirstRawPercent)
            {
                _acceptedLowBaseline = true;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool earlyHighFrame = !_acceptedLowBaseline
                && rawPercent >= _suspiciousFirstRawPercent
                && now - _startedAt < _suspiciousFirstWindow;

            if (earlyHighFrame)
            {
                return false;
            }

            double target = Math.Min(MapRawToStage(rawPercent), GetElapsedCeiling(now));
            if (target <= _lastMapped)
            {
                return false;
            }

            double allowedStep = _hasEmitted ? MaximumRawStep : MaximumFirstRawStep;
            double candidate = Math.Min(target, _lastMapped + allowedStep);
            candidate = Math.Clamp(candidate, _minimum, GetPreCompletionCeiling());

            return TryCommit(candidate, out mappedPercent);
        }
    }

    public bool TryAdvanceSoftly(out double mappedPercent)
    {
        lock (_syncRoot)
        {
            mappedPercent = _lastMapped;

            double target = GetElapsedCeiling(DateTimeOffset.UtcNow);
            if (target <= _lastMapped)
            {
                return false;
            }

            double candidate = Math.Min(target, _lastMapped + MaximumSoftStep);
            candidate = Math.Clamp(candidate, _minimum, GetPreCompletionCeiling());

            return TryCommit(candidate, out mappedPercent);
        }
    }

    public bool TryCompleteStage(out double mappedPercent)
    {
        lock (_syncRoot)
        {
            mappedPercent = _lastMapped;

            if (_lastMapped >= _maximum)
            {
                return false;
            }

            _lastMapped = _maximum;
            _hasEmitted = true;
            mappedPercent = _maximum;

            return true;
        }
    }

    private bool TryCommit(double candidate, out double mappedPercent)
    {
        mappedPercent = _lastMapped;

        if (candidate <= _lastMapped || Math.Abs(candidate - _lastMapped) < MinimumProgressDelta)
        {
            return false;
        }

        _lastMapped = candidate;
        _hasEmitted = true;
        mappedPercent = candidate;

        return true;
    }

    private double MapRawToStage(int rawPercent)
    {
        rawPercent = Math.Clamp(rawPercent, 0, 100);

        double ratio = rawPercent / 100.0;
        return Math.Clamp(_minimum + ((_maximum - _minimum) * ratio), _minimum, _maximum);
    }

    private double GetElapsedCeiling(DateTimeOffset now)
    {
        double elapsedSeconds = Math.Max(0, (now - _startedAt).TotalSeconds);
        double elapsedCeiling = _minimum + ElapsedInitialAllowance + (elapsedSeconds * ElapsedRatePercentPerSecond);

        return Math.Clamp(elapsedCeiling, _minimum, GetPreCompletionCeiling());
    }

    private double GetPreCompletionCeiling() =>
        Math.Max(_minimum, _maximum - 2);
}