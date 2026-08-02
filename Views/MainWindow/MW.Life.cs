using HakamiqChdTool.App.Ui.WpfAdapters;
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
    private static readonly TimeSpan QueueDisposeShutdownTimeout = TimeSpan.FromSeconds(45);
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
            TrayNotifyIconHost.Dispose();
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

        ShutdownStepResult queueDisposeResult = await RunBackgroundShutdownStepAsync(
            "Dispose queue.",
            async () =>
            {
                await _queue.DisposeAsync().ConfigureAwait(false);
            },
            QueueDisposeShutdownTimeout).ConfigureAwait(false);

        bool queueQuiesced = queueDisposeResult == ShutdownStepResult.Completed;
        if (queueQuiesced)
        {
            await RunBackgroundShutdownStepAsync(
                "Clean pending conversion workspaces after queue shutdown.",
                () => Task.Run(TryCleanupPendingWorkspacesAfterQueueShutdown),
                PendingWorkspaceCleanupShutdownTimeout).ConfigureAwait(false);
        }
        else
        {
            Log.Warning(
                "Skipping pending workspace cleanup because queue shutdown did not complete. Result={Result}",
                queueDisposeResult);
        }

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

        if (queueQuiesced)
        {
            await RunBackgroundShutdownStepAsync(
                "Clean up runtime tool session.",
                async () =>
                {
                    await Task.Run(_runtimeTools.TryCleanupCurrentSession).ConfigureAwait(false);
                },
                RuntimeSessionCleanupShutdownTimeout).ConfigureAwait(false);
        }
        else
        {
            Log.Warning(
                "Skipping runtime tool cleanup because queue shutdown did not complete. Result={Result}",
                queueDisposeResult);
        }

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

    private static async Task<ShutdownStepResult> RunBackgroundShutdownStepAsync(
        string stepName,
        Func<Task> action,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        Task? stepTask = null;

        try
        {
            stepTask = action();

            if (timeout.HasValue)
            {
                await stepTask.WaitAsync(timeout.Value).ConfigureAwait(false);
            }
            else
            {
                await stepTask.ConfigureAwait(false);
            }

            return ShutdownStepResult.Completed;
        }
        catch (TimeoutException ex)
        {
            Log.Warning(
                ex,
                "Shutdown background step timed out: {StepName}",
                stepName);

            if (stepTask is not null)
            {
                ObserveLateShutdownTask(stepTask, stepName);
            }

            return ShutdownStepResult.TimedOut;
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(
                ex,
                "Shutdown background step cancelled: {StepName}",
                stepName);
            return ShutdownStepResult.Cancelled;
        }
        catch (ObjectDisposedException ex)
        {
            Log.Debug(
                ex,
                "Shutdown background step skipped because dependency was disposed: {StepName}",
                stepName);
            return ShutdownStepResult.Failed;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Shutdown background step failed: {StepName}",
                stepName);
            return ShutdownStepResult.Failed;
        }
    }

    private static void ObserveLateShutdownTask(Task task, string stepName)
    {
        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                string name = (string)state!;
                if (completedTask.Exception is not null)
                {
                    completedTask.Exception.Handle(static _ => true);
                    Log.Debug(
                        completedTask.Exception,
                        "Shutdown background step faulted after its timeout: {StepName}",
                        name);
                }
            },
            stepName,
            System.Threading.CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private enum ShutdownStepResult
    {
        Completed = 0,
        TimedOut = 1,
        Cancelled = 2,
        Failed = 3
    }
}
