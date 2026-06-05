using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        if (_shutdownStarted || _shutdownCompleted || _windowLifetimeCts.IsCancellationRequested)
        {
            return;
        }

        ApplicationRestartContext? restartContext = ApplicationRestartService.ConsumeRestartContext();
        if (restartContext is not null)
        {
            ApplicationRestartService.TryRestoreMainWindowBounds(this, restartContext);
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
                MainWindowMessages.StartupRuntimeErrorBody(RuntimeDiagnosticFormatter.SummarizeException(ex)));

            Close();
        }
    }

    private void QueueRestartContextWindowRestore(ApplicationRestartContext? restartContext)
    {
        if (restartContext is null
            || !string.Equals(
                restartContext.ReopenWindow,
                ApplicationRestartContext.AdvancedOptionsWindowName,
                StringComparison.Ordinal))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_shutdownStarted
                    || _shutdownCompleted
                    || _windowLifetimeCts.IsCancellationRequested
                    || Dispatcher.HasShutdownStarted
                    || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                OpenAdvancedOptionsDialog(restartContext.AdvancedOptionsTabKey);
            }),
            DispatcherPriority.ContextIdle);
    }
}
