using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.WpfShell;

public interface IUiDispatcher
{
    bool IsShutdownStarted { get; }

    bool IsShutdownFinished { get; }

    bool CheckAccess();

    void BeginInvoke(Action action, UiDispatchPriority priority = UiDispatchPriority.Background);

    Task InvokeAsync(Action action, UiDispatchPriority priority = UiDispatchPriority.Background, CancellationToken cancellationToken = default);

    Task<T> InvokeAsync<T>(Func<T> action, UiDispatchPriority priority = UiDispatchPriority.Background, CancellationToken cancellationToken = default);
}
