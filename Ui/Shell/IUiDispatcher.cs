using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Ui.Shell;

public interface IUiDispatcher
{
    bool IsShutdownStarted { get; }

    bool IsShutdownFinished { get; }

    bool CheckAccess();

    void BeginInvoke(Action action, UiPriority priority = UiPriority.Background);

    Task InvokeAsync(Action action, UiPriority priority = UiPriority.Background, CancellationToken cancellationToken = default);

    Task<T> InvokeAsync<T>(Func<T> action, UiPriority priority = UiPriority.Background, CancellationToken cancellationToken = default);
}
