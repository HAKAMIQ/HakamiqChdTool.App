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

    internal void HandleLaunchRequest(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        ShowTrayWindow();
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

                // Explorer starts one process per selected file; requests for the same mode are added together.
                while (_pendingLaunches.TryPeek(out LaunchRequest? next) && next.Mode == first.Mode)
                {
                    paths.AddRange(_pendingLaunches.Dequeue().Paths);
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
    }

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
