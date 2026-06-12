using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class UiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public UiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool IsShutdownStarted => _dispatcher.HasShutdownStarted;

    public bool IsShutdownFinished => _dispatcher.HasShutdownFinished;

    public bool CheckAccess()
    {
        return _dispatcher.CheckAccess();
    }

    public void BeginInvoke(Action action, UiPriority priority = UiPriority.Background)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsShutdownStarted || IsShutdownFinished)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(action, MapPriority(priority));
    }

    public Task InvokeAsync(
        Action action,
        UiPriority priority = UiPriority.Background,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsShutdownStarted || IsShutdownFinished)
        {
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action, MapPriority(priority), cancellationToken).Task;
    }

    public Task<T> InvokeAsync<T>(
        Func<T> action,
        UiPriority priority = UiPriority.Background,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsShutdownStarted || IsShutdownFinished)
        {
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        }

        return _dispatcher.InvokeAsync(action, MapPriority(priority), cancellationToken).Task;
    }

    private static DispatcherPriority MapPriority(UiPriority priority)
    {
        return priority switch
        {
            UiPriority.Send => DispatcherPriority.Send,
            UiPriority.Normal => DispatcherPriority.Normal,
            UiPriority.Background => DispatcherPriority.Background,
            UiPriority.ContextIdle => DispatcherPriority.ContextIdle,
            UiPriority.ApplicationIdle => DispatcherPriority.ApplicationIdle,
            UiPriority.DataBind => DispatcherPriority.DataBind,
            UiPriority.Render => DispatcherPriority.Render,
            _ => DispatcherPriority.Background
        };
    }
}
