using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Views;
using HakamiqChdTool.App.Ui.Shell;
using Serilog;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HakamiqChdTool.App.Startup;

internal sealed class MainWindowStartupCoordinator
{
    private readonly Window _owner;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IResourceTextProvider _resourceTextProvider;
    private readonly RuntimeToolService _runtimeTools;
    private readonly AppSettings _settings;
    private readonly AppSettingsService _settingsService;
    private readonly RedumpAutoSyncStartupService _redumpAutoSync = new();
    private readonly OrphanedWorkItemScanner _orphanedScanner;
    private readonly OrphanedWorkItemCleanupService _orphanedCleanup;
    private readonly Action<string> _setFooterStatus;
    private readonly Action _applySettingsToUi;
    private readonly Action _updateUiState;
    private readonly Action _updateHeaderModeText;
    private bool _mainWindowContentRendered;
    private int _redumpAutoSyncQueued;

    public MainWindowStartupCoordinator(
        Window owner,
        IUiDispatcher uiDispatcher,
        IResourceTextProvider resourceTextProvider,
        RuntimeToolService runtimeTools,
        AppSettings settings,
        AppSettingsService settingsService,
        OrphanedWorkItemScanner orphanedScanner,
        OrphanedWorkItemCleanupService orphanedCleanup,
        Action<string> setFooterStatus,
        Action applySettingsToUi,
        Action updateUiState,
        Action updateHeaderModeText)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _resourceTextProvider = resourceTextProvider ?? throw new ArgumentNullException(nameof(resourceTextProvider));
        _runtimeTools = runtimeTools ?? throw new ArgumentNullException(nameof(runtimeTools));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _orphanedScanner = orphanedScanner ?? throw new ArgumentNullException(nameof(orphanedScanner));
        _orphanedCleanup = orphanedCleanup ?? throw new ArgumentNullException(nameof(orphanedCleanup));
        _setFooterStatus = setFooterStatus ?? throw new ArgumentNullException(nameof(setFooterStatus));
        _applySettingsToUi = applySettingsToUi ?? throw new ArgumentNullException(nameof(applySettingsToUi));
        _updateUiState = updateUiState ?? throw new ArgumentNullException(nameof(updateUiState));
        _updateHeaderModeText = updateHeaderModeText ?? throw new ArgumentNullException(nameof(updateHeaderModeText));

        _owner.ContentRendered += MarkMainWindowContentRendered;
    }

    public async Task<Task> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsOwnerDispatcherUnavailable())
        {
            return Task.CompletedTask;
        }

        _setFooterStatus(MainWindowMessages.InitializingTools);

        await Task.Run(
                () => _runtimeTools.EnsureInitialized(cleanupStaleSessions: false),
                cancellationToken)
            .ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        Task deferredCleanupTask = _runtimeTools.StartDeferredCleanupAsync();
        ObserveDeferredTask(deferredCleanupTask, "Runtime tools deferred cleanup");

        if (IsOwnerDispatcherUnavailable())
        {
            return Task.CompletedTask;
        }

        _applySettingsToUi();
        _updateUiState();
        _updateHeaderModeText();
        ApplyPreferredWindowBounds();

        await OfferOrphanedWorkItemCleanupAsync(cancellationToken).ConfigureAwait(true);

        QueueRedumpAutoSyncAfterMainWindowShown(cancellationToken);

        return RunStartupUpdateCheckAsync(_owner, _uiDispatcher, cancellationToken);
    }

    private void MarkMainWindowContentRendered(object? sender, EventArgs e)
    {
        _mainWindowContentRendered = true;
        _owner.ContentRendered -= MarkMainWindowContentRendered;
    }

    private void QueueRedumpAutoSyncAfterMainWindowShown(CancellationToken cancellationToken)
    {
        if (!_redumpAutoSync.ShouldRun(_settings))
        {
            return;
        }

        if (Interlocked.Exchange(ref _redumpAutoSyncQueued, 1) != 0)
        {
            return;
        }

        if (_mainWindowContentRendered)
        {
            StartQueuedRedumpAutoSyncOnIdle(cancellationToken);
            return;
        }

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (handler is not null)
            {
                _owner.ContentRendered -= handler;
            }

            _mainWindowContentRendered = true;
            StartQueuedRedumpAutoSyncOnIdle(cancellationToken);
        };

        _owner.ContentRendered += handler;
    }

    private void StartQueuedRedumpAutoSyncOnIdle(CancellationToken cancellationToken)
    {
        if (IsOwnerDispatcherUnavailable())
        {
            return;
        }

        _uiDispatcher.BeginInvoke(
            () =>
            {
                if (cancellationToken.IsCancellationRequested
                    || IsOwnerDispatcherUnavailable()
                    || !_mainWindowContentRendered
                    || Interlocked.Exchange(ref _redumpAutoSyncQueued, 0) == 0)
                {
                    return;
                }

                Task redumpAutoSyncTask = StartRedumpAutoSyncIfConfiguredAsync(cancellationToken);
                ObserveDeferredTask(redumpAutoSyncTask, "Redump startup auto-sync");
            },
            UiPriority.ApplicationIdle);
    }

    private Task StartRedumpAutoSyncIfConfiguredAsync(CancellationToken cancellationToken)
    {
        if (!_redumpAutoSync.ShouldRun(_settings))
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            async () =>
            {
                RedumpAutoSyncStartupResult result = await _redumpAutoSync
                    .TrySyncAsync(_settings, cancellationToken)
                    .ConfigureAwait(false);

                if (result.Skipped)
                {
                    return;
                }

                if (result.Success && result.SyncedAtUtc.HasValue)
                {
                    await PersistRedumpAutoSyncSuccessAsync(result.SyncedAtUtc.Value, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (result.BackoffUntilUtc.HasValue)
                {
                    await PersistRedumpAutoSyncBackoffAsync(result.BackoffUntilUtc.Value, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (result.Success)
                {
                    await SetFooterStatusResourceAsync(
                            result.MessageKey,
                            cancellationToken,
                            result.ImportedSystems)
                        .ConfigureAwait(false);
                }
                else
                {
                    Log.Debug(
                        "Redump startup auto-sync did not complete. MessageKey={MessageKey}",
                        result.MessageKey);
                }
            },
            cancellationToken);
    }

    private Task SetFooterStatusResourceAsync(
        string resourceKey,
        CancellationToken cancellationToken,
        params object[] formatArguments)
    {
        if (IsOwnerDispatcherUnavailable())
        {
            return Task.CompletedTask;
        }

        return _uiDispatcher.InvokeAsync(
            () =>
            {
                if (IsOwnerDispatcherUnavailable())
                {
                    return;
                }

                string message = _resourceTextProvider.GetString(resourceKey);

                if (formatArguments.Length > 0)
                {
                    message = string.Format(
                        CultureInfo.CurrentCulture,
                        message,
                        formatArguments);
                }

                _setFooterStatus(message);
            },
            UiPriority.Background,
            cancellationToken);
    }

    private async Task PersistRedumpAutoSyncSuccessAsync(DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
    {
        if (IsOwnerDispatcherUnavailable())
        {
            return;
        }

        string normalized = syncedAtUtc.ToString("O", CultureInfo.InvariantCulture);

        await _uiDispatcher.InvokeAsync(
                () =>
                {
                    if (!IsOwnerDispatcherUnavailable())
                    {
                        _settings.RedumpLastSyncedUtc = normalized;
                        _settings.RedumpAutoSyncBackoffUntilUtc = string.Empty;
                    }
                },
                UiPriority.Background,
                cancellationToken)
            .ConfigureAwait(false);

        await Task.Run(() => _settingsService.Save(_settings), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PersistRedumpAutoSyncBackoffAsync(DateTimeOffset backoffUntilUtc, CancellationToken cancellationToken)
    {
        if (IsOwnerDispatcherUnavailable())
        {
            return;
        }

        string normalized = backoffUntilUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        await _uiDispatcher.InvokeAsync(
                () =>
                {
                    if (!IsOwnerDispatcherUnavailable())
                    {
                        _settings.RedumpAutoSyncBackoffUntilUtc = normalized;
                    }
                },
                UiPriority.Background,
                cancellationToken)
            .ConfigureAwait(false);

        await Task.Run(() => _settingsService.Save(_settings), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task OfferOrphanedWorkItemCleanupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsOwnerDispatcherUnavailable())
        {
            return;
        }

        OrphanedWorkItemScanResult scanResult;
        try
        {
            scanResult = await Task.Run(
                    _orphanedScanner.Scan,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Debug(ex, "Startup orphaned work item scan failed.");
            return;
        }

        if (!scanResult.HasItems || IsOwnerDispatcherUnavailable())
        {
            return;
        }

        string reclaimableSize = FormatByteSize(scanResult.TotalBytes);
        string workspaceRootSummary = BuildOrphanedCleanupRootSummary(scanResult);

        var dialog = new OrphanedCleanupDialog(scanResult, reclaimableSize, workspaceRootSummary)
        {
            Owner = _owner
        };

        bool? answer = dialog.ShowDialog();

        if (answer != true)
        {
            _setFooterStatus(_resourceTextProvider.GetString("LocOrphanedCleanup_SkippedFooter"));
            return;
        }

        _setFooterStatus(_resourceTextProvider.GetString("LocOrphanedCleanup_CleaningFooter"));

        CleanupStats cleanupStats;
        try
        {
            cleanupStats = await Task.Run(
                    () => _orphanedCleanup.Clean(scanResult),
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Debug(ex, "Startup orphaned work item cleanup failed.");
            _setFooterStatus(_resourceTextProvider.GetString("LocOrphanedCleanup_FailedFooter"));
            return;
        }

        string cleanedSize = FormatByteSize(cleanupStats.DeletedBytes);
        _setFooterStatus(string.Format(
            CultureInfo.CurrentCulture,
            _resourceTextProvider.GetString("LocOrphanedCleanup_CompletedFooter"),
            cleanedSize));
    }

    private static string BuildOrphanedCleanupRootSummary(OrphanedWorkItemScanResult scanResult)
    {
        string[] roots = scanResult.RootPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length > 0)
        {
            return string.Join(Environment.NewLine, roots);
        }

        try
        {
            return AppPaths.ProcessTempRoot;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Debug(ex, "Could not resolve cleanup workspace roots for orphaned work item cleanup dialog.");
            return string.Empty;
        }
    }

    private static string FormatByteSize(long bytes)
    {
        const double Unit = 1024;
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];

        double value = Math.Max(0, bytes);
        int suffixIndex = 0;

        while (value >= Unit && suffixIndex < suffixes.Length - 1)
        {
            value /= Unit;
            suffixIndex++;
        }

        string format = suffixIndex == 0 ? "0" : "0.##";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + suffixes[suffixIndex];
    }

    private void ApplyPreferredWindowBounds()
    {
        if (IsOwnerDispatcherUnavailable())
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;

        double preferredWidth = GetRequiredDoubleResource("Window.Main.PreferredWidth");
        double preferredHeight = GetRequiredDoubleResource("Window.Main.PreferredHeight");

        _owner.MaxWidth = workArea.Width;
        _owner.MaxHeight = workArea.Height;

        _owner.Width = Math.Min(preferredWidth, workArea.Width);
        _owner.Height = Math.Min(preferredHeight, workArea.Height);

        if (_owner.Width < _owner.MinWidth)
        {
            _owner.Width = workArea.Width;
        }

        if (_owner.Height < _owner.MinHeight)
        {
            _owner.Height = workArea.Height;
        }

        _owner.Left = workArea.Left + Math.Max(0, (workArea.Width - _owner.Width) / 2);
        _owner.Top = workArea.Top + Math.Max(0, (workArea.Height - _owner.Height) / 2);
    }

    private double GetRequiredDoubleResource(string resourceKey)
    {
        if (_resourceTextProvider.TryGetDouble(resourceKey, out double value))
        {
            return value;
        }

        throw new InvalidOperationException($"Required numeric UI resource '{resourceKey}' was not found or is not numeric.");
    }

    private bool IsOwnerDispatcherUnavailable()
    {
        return _uiDispatcher.IsShutdownStarted || _uiDispatcher.IsShutdownFinished;
    }

    private static async Task RunStartupUpdateCheckAsync(Window owner, IUiDispatcher uiDispatcher, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (uiDispatcher.IsShutdownStarted || uiDispatcher.IsShutdownFinished)
            {
                return;
            }

            Task updateTask = await uiDispatcher.InvokeAsync(
                    () => UpdateService.CheckSilentlyAndOfferRestartAsync(owner),
                    UiPriority.Background,
                    cancellationToken)
                .ConfigureAwait(false);

            await updateTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Startup update check cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Startup update check failed.");
        }
    }

    private static void ObserveDeferredTask(Task task, string operationName)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.IsCompletedSuccessfully || task.IsCanceled)
        {
            return;
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                if (completedTask.Exception is not null)
                {
                    Log.Debug(
                        completedTask.Exception,
                        "{OperationName} failed.",
                        operationName);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
