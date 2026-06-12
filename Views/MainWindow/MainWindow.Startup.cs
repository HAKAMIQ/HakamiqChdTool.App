using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        _ = HandleMainWindowLoadedAsync();
    }

    private async Task HandleMainWindowLoadedAsync()
    {
        if (_shutdownStarted ||
            _shutdownCompleted ||
            _windowLifetimeCts.IsCancellationRequested)
        {
            return;
        }

        ApplicationRestartContext? restartContext = ApplicationRestartService.ConsumeRestartContext();
        if (restartContext is not null)
        {
            ApplicationRestartService.TryRestoreMainWindowBounds(
                this,
                restartContext);
        }

        try
        {
            _startupUpdateCheckTask = await _startupCoordinator
                .InitializeAsync(_windowLifetimeCts.Token)
                .ConfigureAwait(true);

            QueueRestartContextWindowRestore(restartContext);
        }
        catch (OperationCanceledException) when (_windowLifetimeCts.IsCancellationRequested)
        {
            Close();
        }
        catch (Exception ex)
        {
            ShowNoticeDialog(
                MainWindowMessages.StartupRuntimeErrorTitle,
                MainWindowMessages.StartupRuntimeErrorBody(
                    RuntimeDiagnosticFormatter.SummarizeException(ex)));

            Close();
        }
    }

    private void QueueRestartContextWindowRestore(ApplicationRestartContext? restartContext)
    {
        if (restartContext is null ||
            !string.Equals(
                restartContext.ReopenWindow,
                ApplicationRestartContext.OptionsWindowName,
                StringComparison.Ordinal))
        {
            return;
        }

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_shutdownStarted ||
                        _shutdownCompleted ||
                        _windowLifetimeCts.IsCancellationRequested ||
                        Dispatcher.HasShutdownStarted ||
                        Dispatcher.HasShutdownFinished)
                    {
                        return;
                    }

                    OpenOptionsDialog(restartContext.OptionsTabKey);
                }),
                DispatcherPriority.ContextIdle);
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
    }
}