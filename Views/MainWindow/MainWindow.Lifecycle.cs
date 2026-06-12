using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private static readonly TimeSpan StartupUpdateCheckShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RuntimeDeferredCleanupShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QueueDisposeShutdownTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PendingWorkspaceCleanupShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RuntimeSessionCleanupShutdownTimeout = TimeSpan.FromSeconds(3);

    private async Task ShutdownAsync()
    {
        if (_shutdownCompleted)
        {
            return;
        }

        await RunUiShutdownStepAsync("Capture settings before shutdown.", () =>
        {
            CaptureThemeIntoSettings();
        }).ConfigureAwait(false);

        await RunBackgroundShutdownStepAsync("Persist settings before shutdown.", async () =>
        {
            _settingsService.CancelPendingSave();
            await _settingsService.SaveAsync(_settings).ConfigureAwait(false);
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Detach window event handlers.", () =>
        {
            _queue.ItemUpdated -= OnQueueItemUpdated;
            _queueRowStore.CollectionChanged -= OnQueueRowStoreCollectionChanged;
            _queueRowStore.RowMutated -= OnQueueRowStoreRowMutated;
            _viewport.VmMaterialized -= OnVmMaterialized;
            _viewport.VmReleased -= OnVmReleased;
            ThemeService.Instance.ThemeChanged -= ThemeService_ThemeChanged;
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Cancel window lifetime operations.", () =>
        {
            _windowLifetimeCts.Cancel();
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose tray notification icon.", () =>
        {
            TrayNotifyIcon.Dispose();
        }).ConfigureAwait(false);

        if (_startupUpdateCheckTask is not null)
        {
            await RunBackgroundShutdownStepAsync(
                "Wait for startup update check.",
                async () =>
                {
                    await _startupUpdateCheckTask.ConfigureAwait(false);
                },
                StartupUpdateCheckShutdownTimeout).ConfigureAwait(false);
        }

        await RunBackgroundShutdownStepAsync(
            "Wait for runtime tool deferred cleanup.",
            async () =>
            {
                await _runtimeTools.WaitForDeferredCleanupAsync().ConfigureAwait(false);
            },
            RuntimeDeferredCleanupShutdownTimeout).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose MainWindow view model.", () =>
        {
            _viewModel.Dispose();
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose session coordinator.", () =>
        {
            _coordinator.Dispose();
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose queue controller.", () =>
        {
            _queueController.Dispose();
        }).ConfigureAwait(false);

        await RunBackgroundShutdownStepAsync(
            "Dispose queue.",
            async () =>
            {
                await _queue.DisposeAsync().ConfigureAwait(false);
            },
            QueueDisposeShutdownTimeout).ConfigureAwait(false);

        await RunBackgroundShutdownStepAsync(
            "Clean pending conversion workspaces after queue shutdown.",
            () => Task.Run(TryCleanupPendingWorkspacesAfterQueueShutdown),
            PendingWorkspaceCleanupShutdownTimeout).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Release queue viewport resolver.", () =>
        {
            _viewport.SetVisibleIndexResolver(null);
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose queue view.", () =>
        {
            _queueView.Dispose();
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose queue viewport.", () =>
        {
            _viewport.Dispose();
        }).ConfigureAwait(false);

        await RunBackgroundShutdownStepAsync(
            "Clean up runtime tool session.",
            async () =>
            {
                await Task.Run(_runtimeTools.TryCleanupCurrentSession).ConfigureAwait(false);
            },
            RuntimeSessionCleanupShutdownTimeout).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Dispose window lifetime token source.", () =>
        {
            _windowLifetimeCts.Dispose();
        }).ConfigureAwait(false);

        await RunUiShutdownStepAsync("Mark shutdown as completed.", () =>
        {
            _shutdownCompleted = true;
        }).ConfigureAwait(false);
    }

    private async Task BeginDeterministicShutdownAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;

        try
        {
            await ShutdownAsync().ConfigureAwait(true);

            await RunUiShutdownStepAsync("Close window after deterministic shutdown.", () =>
            {
                Close();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Deterministic shutdown failed.");

            await RunUiShutdownStepAsync("Reset shutdown started flag after failure.", () =>
            {
                _shutdownStarted = false;
            }).ConfigureAwait(false);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        CaptureThemeIntoSettings();

        if (_shutdownCompleted)
        {
            return;
        }

        if (_shutdownStarted)
        {
            e.Cancel = true;
            return;
        }

        if (_coordinator.IsProcessing && !_coordinator.CancellationRequested)
        {
            if (ShowCloseWhileProcessingConfirmationDialog())
            {
                _coordinator.RequestCancel();
            }
            else
            {
                e.Cancel = true;
                PersistSettings();
                return;
            }
        }

        e.Cancel = true;
        _ = BeginDeterministicShutdownAsync();
    }

    private async Task RunUiShutdownStepAsync(string stepName, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            await Dispatcher.InvokeAsync(
                action,
                DispatcherPriority.Send);
        }
        catch (TaskCanceledException ex) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Log.Debug(
                ex,
                "Shutdown UI step cancelled because Dispatcher is shutting down: {StepName}",
                stepName);
        }
        catch (InvalidOperationException ex) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Log.Debug(
                ex,
                "Shutdown UI step skipped because Dispatcher is shutting down: {StepName}",
                stepName);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Shutdown UI step failed: {StepName}",
                stepName);
        }
    }

    private static async Task RunBackgroundShutdownStepAsync(
        string stepName,
        Func<Task> action,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            Task stepTask = action();

            if (timeout.HasValue)
            {
                await stepTask.WaitAsync(timeout.Value).ConfigureAwait(false);
            }
            else
            {
                await stepTask.ConfigureAwait(false);
            }
        }
        catch (TimeoutException ex)
        {
            Log.Warning(
                ex,
                "Shutdown background step timed out: {StepName}",
                stepName);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(
                ex,
                "Shutdown background step cancelled: {StepName}",
                stepName);
        }
        catch (ObjectDisposedException ex)
        {
            Log.Debug(
                ex,
                "Shutdown background step skipped because dependency was disposed: {StepName}",
                stepName);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Shutdown background step failed: {StepName}",
                stepName);
        }
    }
}