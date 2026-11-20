using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services.Watch;
using HakamiqChdTool.App.Startup;
using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App;

/// <summary>
/// The watch folder: new disc images and archives in the chosen folder are converted while the app is open.
/// They go through the launch path (convert mode, start when the queue is free) without taking the focus.
/// </summary>
public partial class MainWindow
{
    private const string WatchFolderUnavailableFooterKey = "LocWatch_UnavailableFooter";
    private const string WatchFolderAddedFooterKey = "LocWatch_AddedFooter";

    private WatchFolderService? _watchFolder;

    private void ApplyWatchFolderSetting()
    {
        string? folder = _settings.WatchFolderEnabled ? _settings.WatchFolderPath : null;
        if (_watchFolder is not null && string.Equals(_watchFolder.Folder, folder?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopWatchFolder();
        if (!_settings.WatchFolderEnabled)
        {
            return;
        }

        _watchFolder = WatchFolderService.TryStart(folder, OnWatchFolderFilesReady);
        if (_watchFolder is null)
        {
            SetFooterStatus(ArabicUi.Get(WatchFolderUnavailableFooterKey));
        }
    }

    private void StopWatchFolder()
    {
        _watchFolder?.Dispose();
        _watchFolder = null;
    }

    private void OnWatchFolderFilesReady(IReadOnlyList<string> files)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_shutdownStarted || _shutdownCompleted)
            {
                return;
            }

            SetFooterStatus(ArabicUi.Get(WatchFolderAddedFooterKey));
            HandleLaunchRequest(new LaunchRequest(LaunchMode.Convert, files, Start: true), activate: false);
        }));
    }
}
