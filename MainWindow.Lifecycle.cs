using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
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
            await RunBackgroundShutdownStepAsync("Wait for startup update check.", async () =>
            {
                await _startupUpdateCheckTask.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        await RunBackgroundShutdownStepAsync("Wait for runtime tool deferred cleanup.", async () =>
        {
            await _runtimeTools.WaitForDeferredCleanupAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);


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

        await RunBackgroundShutdownStepAsync("Dispose queue.", async () =>
        {
            await _queue.DisposeAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        await RunBackgroundShutdownStepAsync("Clean pending conversion workspaces after queue shutdown.", () =>
            Task.Run(TryCleanupPendingWorkspacesAfterQueueShutdown)).ConfigureAwait(false);

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

        await RunBackgroundShutdownStepAsync("Clean up runtime tool session.", async () =>
        {
            await Task.Run(_runtimeTools.TryCleanupCurrentSession).ConfigureAwait(false);
        }).ConfigureAwait(false);

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

    private void TryCleanupPendingWorkspacesAfterQueueShutdown()
    {
        try
        {
            string[] outputRoots = CollectKnownOutputRootsForPendingWorkspaceCleanup();
            if (outputRoots.Length == 0)
            {
                return;
            }

            string[] pendingRoots = BuildPendingWorkspaceRoots(outputRoots);
            if (pendingRoots.Length > 0)
            {
                OrphanedWorkItemScanResult scanResult = _orphanedScanner.Scan(
                    TimeSpan.Zero,
                    pendingRoots);

                if (scanResult.HasItems)
                {
                    _orphanedCleanup.Clean(scanResult);
                }
            }

            foreach (string outputRoot in outputRoots)
            {
                WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
            }
        }
        catch (Exception ex) when (IsExpectedPendingWorkspaceShutdownCleanupException(ex))
        {
            Log.Debug(ex, "Pending workspace shutdown cleanup was skipped after a non-fatal error.");
        }
    }

    private string[] CollectKnownOutputRootsForPendingWorkspaceCleanup()
    {
        var outputRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _queueRowStore.GetRowsSnapshot())
        {
            string originalPath = row.OriginalPath;
            string sourcePath = string.IsNullOrWhiteSpace(row.SourcePath)
                ? row.OriginalPath
                : row.SourcePath;

            TryAddOutputRootForPendingWorkspaceCleanup(outputRoots, originalPath, sourcePath);

            if (!string.Equals(originalPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                TryAddOutputRootForPendingWorkspaceCleanup(outputRoots, originalPath, originalPath);
            }
        }

        return [.. outputRoots];
    }

    private void TryAddOutputRootForPendingWorkspaceCleanup(
        HashSet<string> outputRoots,
        string originalPath,
        string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(originalPath) || string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        try
        {
            string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(originalPath, sourcePath, _settings);
            if (!string.IsNullOrWhiteSpace(outputRoot))
            {
                outputRoots.Add(Path.GetFullPath(outputRoot));
            }
        }
        catch (Exception ex) when (IsExpectedPendingWorkspaceShutdownCleanupException(ex))
        {
            Log.Debug(ex, "Failed to resolve output root for pending workspace shutdown cleanup. OriginalPath={OriginalPath}; SourcePath={SourcePath}", originalPath, sourcePath);
        }
    }

    private string[] BuildPendingWorkspaceRoots(IEnumerable<string> outputRoots)
    {
        var pendingRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string outputRoot in outputRoots)
        {
            try
            {
                string pendingRoot = PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(outputRoot, _settings);
                if (Directory.Exists(pendingRoot))
                {
                    pendingRoots.Add(Path.GetFullPath(pendingRoot));
                }
            }
            catch (Exception ex) when (IsExpectedPendingWorkspaceShutdownCleanupException(ex))
            {
                Log.Debug(ex, "Failed to resolve pending workspace root for shutdown cleanup. OutputRoot={OutputRoot}", outputRoot);
            }
        }

        return [.. pendingRoots];
    }

    private static bool IsExpectedPendingWorkspaceShutdownCleanupException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

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

            await Dispatcher.InvokeAsync(action, DispatcherPriority.Send);
        }
        catch (TaskCanceledException ex) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Log.Debug(ex, "Shutdown UI step cancelled because Dispatcher is shutting down: {StepName}", stepName);
        }
        catch (InvalidOperationException ex) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Log.Debug(ex, "Shutdown UI step skipped because Dispatcher is shutting down: {StepName}", stepName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown UI step failed: {StepName}", stepName);
        }
    }

    private static async Task RunBackgroundShutdownStepAsync(string stepName, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Shutdown background step cancelled: {StepName}", stepName);
        }
        catch (ObjectDisposedException ex)
        {
            Log.Debug(ex, "Shutdown background step skipped because dependency was disposed: {StepName}", stepName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown background step failed: {StepName}", stepName);
        }
    }
}
