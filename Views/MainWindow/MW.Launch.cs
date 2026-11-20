using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Shell;
using HakamiqChdTool.App.Startup;
using HakamiqChdTool.App.Views.Main;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

/// <summary>
/// Files sent from Explorer or a command line. Requests wait until startup has finished and the queue
/// accepts new items (not while it runs), then switch to the requested mode and go through the same
/// intake as dropped files.
/// </summary>
public partial class MainWindow
{
    private const string LaunchWaitingFooterKey = "LocLaunch_WaitingFooter";
    private const string LaunchFailedFooterKey = "LocLaunch_AddFailedFooter";
    private const string ExplorerMenuFailedFooterKey = "LocLaunch_ExplorerMenuFailedFooter";

    private readonly Queue<LaunchRequest> _pendingLaunches = new();
    private bool _launchIntakeReady;
    private bool _drainingLaunches;
    private bool _launchStartPending;

    /// <summary>Set for a launch with --exit-when-done: the app closes after the next full run.</summary>
    internal bool ExitWhenDone { get; set; }

    internal void HandleLaunchRequest(LaunchRequest request, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        if (activate)
        {
            ShowTrayWindow();
        }
        if (!request.HasPaths)
        {
            return;
        }

        _pendingLaunches.Enqueue(request);
        if (_launchIntakeReady && IsQueueInteractionLocked)
        {
            SetFooterStatus(ArabicUi.Get(LaunchWaitingFooterKey));
        }

        ScheduleLaunchDrain();
    }

    private void MarkLaunchIntakeReady()
    {
        _launchIntakeReady = true;
        ApplyWatchFolderSetting();
        ScheduleLaunchDrain();
    }

    private void ScheduleLaunchDrain()
    {
        if (_pendingLaunches.Count == 0 || _drainingLaunches || !_launchIntakeReady)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = DrainLaunchesAsync()));
    }

    private async Task DrainLaunchesAsync()
    {
        if (_drainingLaunches || !_launchIntakeReady || IsQueueInteractionLocked)
        {
            return;
        }

        _drainingLaunches = true;
        try
        {
            while (_pendingLaunches.Count > 0 && !IsQueueInteractionLocked && !_shutdownStarted && !_shutdownCompleted)
            {
                LaunchRequest first = _pendingLaunches.Dequeue();
                var paths = new List<string>(first.Paths);
                _launchStartPending |= first.Start;

                // Explorer starts one process per selected file; requests for the same mode are added together.
                while (_pendingLaunches.TryPeek(out LaunchRequest? next) && next.Mode == first.Mode)
                {
                    LaunchRequest merged = _pendingLaunches.Dequeue();
                    _launchStartPending |= merged.Start;
                    paths.AddRange(merged.Paths);
                }

                string[] normalized = NormalizeLaunchPaths(paths);
                if (normalized.Length == 0)
                {
                    continue;
                }

                SelectLaunchMode(first.Mode);
                await IngestLaunchedPathsAsync(normalized).ConfigureAwait(true);
            }
        }
        finally
        {
            _drainingLaunches = false;
        }

        if (_pendingLaunches.Count == 0)
        {
            StartLaunchedRunIfRequested();
        }
    }

    /// <summary>--start: runs the queue once the launched items are in. With --exit-when-done and nothing
    /// to run, the app closes with exit code 3.</summary>
    private void StartLaunchedRunIfRequested()
    {
        if (!_launchStartPending || IsQueueInteractionLocked || _shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        _launchStartPending = false;
        if (CountQueuedRowsForSelectedOperationMode() > 0 && _viewModel.StartProcessingCommand.CanExecute(null))
        {
            _ = _viewModel.StartProcessingCommand.ExecuteAsync(null);
            return;
        }

        if (ExitWhenDone)
        {
            Log.Information("Launch asked to start, but nothing could run. Exiting with code {ExitCode}.", LaunchArgs.ExitNothingToDo);
            RequestLaunchExit(LaunchArgs.ExitNothingToDo);
        }
    }

    private void RequestLaunchExit(int exitCode)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_shutdownStarted && !_shutdownCompleted)
            {
                System.Windows.Application.Current?.Shutdown(exitCode);
            }
        }));
    }

    internal void ShowLaunchUsage(string error) => App.ShowLaunchUsageMessage(IsVisible ? this : null, error);

    private static string[] NormalizeLaunchPaths(IEnumerable<string> paths)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (TaskQueueDropPathNormalizer.TryNormalizeRootPath(path, out string? root) && seen.Add(root))
            {
                normalized.Add(root);
            }
        }

        return [.. normalized];
    }

    private void SelectLaunchMode(LaunchMode mode)
    {
        OperationModePanel panel = ActionRail.OperationModePanel;
        RadioButton? target = mode switch
        {
            LaunchMode.Convert when !panel.IsConvertMode => panel.ConvertRadio,
            LaunchMode.Extract when !panel.IsExtractMode => panel.ExtractRadio,
            LaunchMode.Verify when !panel.IsVerifyMode => panel.VerifyRadio,
            _ => null
        };

        if (target is null)
        {
            return;
        }

        target.IsChecked = true;
        OperationModeRadio_Click(target, new RoutedEventArgs());
    }

    private async Task IngestLaunchedPathsAsync(string[] normalized)
    {
        QueueExecutionProfile profile = GetSelectedInputExecutionProfileFromUi();
        try
        {
            if (profile == QueueExecutionProfile.Standard)
            {
                await _viewModel
                    .IngestPathsAsync(normalized, QueueIngestKind.Mixed, QueueIntakeSource.UserInitiatedAdd)
                    .ConfigureAwait(true);
                return;
            }

            _ = await _viewModel
                .IngestQuickPathsAsync(normalized, QueueIngestKind.Mixed, profile, QueueIntakeSource.UserInitiatedAdd)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Launch intake was cancelled.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Launch intake failed.");
            SetFooterStatus(ArabicUi.Get(LaunchFailedFooterKey));
        }
    }

    /// <summary>Brings the Explorer menu in line with the setting, the app location and the UI language.</summary>
    internal static bool SyncExplorerMenu(bool enabled)
    {
        ExplorerMenuRegistration registration = ExplorerMenuRegistration.ForCurrentUser();
        if (!enabled)
        {
            return !registration.IsRegistered() || registration.TryUnregister();
        }

        string? exePath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(exePath)
            && registration.TryRegister(exePath, CreateExplorerMenuText());
    }

    private static ExplorerMenuText CreateExplorerMenuText() => new(
        ArabicUi.Get("LocShell_ConvertToChd"),
        ArabicUi.Get("LocShell_ExtractChd"),
        ArabicUi.Get("LocShell_VerifyChd"),
        ArabicUi.Get("LocShell_FolderMenu"));

    private void ApplyExplorerMenuSetting(bool previousEnabled, bool languageChanged)
    {
        bool enabled = _settings.ExplorerMenuEnabled;
        if (enabled == previousEnabled && !(enabled && languageChanged))
        {
            return;
        }

        if (!SyncExplorerMenu(enabled))
        {
            SetFooterStatus(ArabicUi.Get(ExplorerMenuFailedFooterKey));
        }
    }
}
