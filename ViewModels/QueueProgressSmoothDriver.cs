using System;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App.ViewModels;

public sealed class QueueProgressSmoothDriver : IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan SegmentDuration = TimeSpan.FromSeconds(0.3);

    private double _displayValue;
    private DispatcherTimer? _timer;
    private double _segmentStart;
    private double _segmentTarget;
    private DateTime _segmentStartUtc;
    private bool _animating;
    private bool _disposed;

    public event EventHandler<double>? DisplayValueChanged;

    public double DisplayValue => _displayValue;

    public void SnapTo(double value)
    {
        if (_disposed)
        {
            return;
        }

        RunOnUiDispatcher(StopAndSnapCore, Math.Clamp(value, 0, 100));
    }

    public void AnimateTo(double targetPercent)
    {
        if (_disposed)
        {
            return;
        }

        RunOnUiDispatcher(AnimateToCore, Math.Clamp(targetPercent, 0, 100));
    }

    public void Dispose()
    {
        Dispatcher? dispatcher = _timer?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            DisposeCore();
            GC.SuppressFinalize(this);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            DisposeCore();
            GC.SuppressFinalize(this);
            return;
        }

        dispatcher.Invoke(DisposeCore, DispatcherPriority.Send);
        GC.SuppressFinalize(this);
    }

    private void AnimateToCore(double targetPercent)
    {
        if (_disposed)
        {
            return;
        }

        if (Math.Abs(_displayValue - targetPercent) < 0.25)
        {
            StopTimer();
            PublishValue(targetPercent);
            return;
        }

        _segmentStart = _displayValue;
        _segmentTarget = targetPercent;
        _segmentStartUtc = DateTime.UtcNow;
        _animating = true;
        EnsureTimer();
        Tick();
    }

    private void StopAndSnapCore(double value)
    {
        if (_disposed)
        {
            return;
        }

        StopTimer();
        _animating = false;
        PublishValue(value);
    }

    private void EnsureTimer()
    {
        if (_timer is not null || _disposed)
        {
            return;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
        {
            Interval = FrameInterval
        };

        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Tick();
    }

    private void Tick()
    {
        if (!_animating || _disposed)
        {
            return;
        }

        double elapsedSeconds = (DateTime.UtcNow - _segmentStartUtc).TotalSeconds;
        double t = Math.Clamp(elapsedSeconds / SegmentDuration.TotalSeconds, 0, 1);
        double eased = 1 - (1 - t) * (1 - t);

        if (t >= 1)
        {
            _animating = false;
            StopTimer();
            PublishValue(_segmentTarget);
            return;
        }

        double value = _segmentStart + ((_segmentTarget - _segmentStart) * eased);
        PublishValue(value);
    }

    private void PublishValue(double value)
    {
        if (_disposed)
        {
            return;
        }

        _displayValue = Math.Clamp(value, 0, 100);
        DisplayValueChanged?.Invoke(this, _displayValue);
    }

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _animating = false;
        StopTimer();
        DisplayValueChanged = null;
    }

    private static void RunOnUiDispatcher(Action<double> action, double value)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action(value);
            return;
        }

        _ = dispatcher.BeginInvoke(
            new Action(() => action(value)),
            DispatcherPriority.Background);
    }
}