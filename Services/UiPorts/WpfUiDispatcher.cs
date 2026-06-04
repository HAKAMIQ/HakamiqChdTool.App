using HakamiqChdTool.UiPorts.Dispatching;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Services.UiPorts;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool IsShutdownStarted => _dispatcher.HasShutdownStarted;

    public bool IsShutdownFinished => _dispatcher.HasShutdownFinished;

    public bool CheckAccess()
    {
        return _dispatcher.CheckAccess();
    }

    public void BeginInvoke(Action action, UiDispatchPriority priority = UiDispatchPriority.Background)
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
        UiDispatchPriority priority = UiDispatchPriority.Background,
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
        UiDispatchPriority priority = UiDispatchPriority.Background,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsShutdownStarted || IsShutdownFinished)
        {
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        }

        return _dispatcher.InvokeAsync(action, MapPriority(priority), cancellationToken).Task;
    }

    private static DispatcherPriority MapPriority(UiDispatchPriority priority)
    {
        return priority switch
        {
            UiDispatchPriority.Send => DispatcherPriority.Send,
            UiDispatchPriority.Normal => DispatcherPriority.Normal,
            UiDispatchPriority.Background => DispatcherPriority.Background,
            UiDispatchPriority.ContextIdle => DispatcherPriority.ContextIdle,
            UiDispatchPriority.ApplicationIdle => DispatcherPriority.ApplicationIdle,
            UiDispatchPriority.DataBind => DispatcherPriority.DataBind,
            UiDispatchPriority.Render => DispatcherPriority.Render,
            _ => DispatcherPriority.Background
        };
    }
}
