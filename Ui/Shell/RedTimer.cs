using HakamiqChdTool.App.ViewModels.Dialogs;
using System;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class RedumpFeedbackTimer : IRedumpDetailsFeedbackTimer
{
    private readonly DispatcherTimer _timer;
    private Action? _elapsed;

    public RedumpFeedbackTimer()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5d)
        };
        _timer.Tick += Timer_Tick;
    }

    public void Restart(Action elapsed)
    {
        _elapsed = elapsed ?? throw new ArgumentNullException(nameof(elapsed));
        _timer.Stop();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _elapsed = null;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _elapsed?.Invoke();
    }
}
