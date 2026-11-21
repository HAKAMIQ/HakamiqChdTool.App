using System;
using System.Windows;

using HakamiqChdTool.App.Services;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        MainHeader?.SetMaximizeRestoreState(WindowState == WindowState.Maximized);

        if (WindowState == WindowState.Minimized && _coordinator.IsProcessing)
        {
            Hide();
        }
    }

    private void TrayHost_MouseDoubleClicked(object sender, RoutedEventArgs e)
    {
        ShowTrayWindow();
    }

    private void TrayHost_ShowRequested(object sender, RoutedEventArgs e)
    {
        ShowTrayWindow();
    }

    private void TrayHost_ExitRequested(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        if (!IsVisible && _coordinator.IsProcessing && !_coordinator.CancellationRequested)
        {
            ShowTrayWindow();
        }

        Close();
    }

    private void ShowTrayWindow()
    {
        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Shows the run result from the tray when the window is not in front. It uses the same title and
    /// line as the bar above the queue, so the balloon also carries the space saved and the run time.
    /// </summary>
    private void TryNotifyTrayForBatchCompletion(
        bool processedSelectionOnly,
        RunSummaryCard runSummary)
    {
        if (processedSelectionOnly || _shutdownStarted || _shutdownCompleted)
        {
            Log.Debug("Tray run notification skipped. SelectionOnly={SelectionOnly} Shutdown={Shutdown}", processedSelectionOnly, _shutdownStarted || _shutdownCompleted);
            return;
        }

        if (IsVisible && WindowState != WindowState.Minimized && IsActive)
        {
            Log.Debug("Tray run notification skipped because the window is in front.");
            return;
        }

        bool iconReady = TrayNotifyIcon.IsTaskbarIconCreated;
        TrayNotifyIcon.ShowBalloonTip(runSummary.Title, runSummary.Message, BalloonIcon.Info);
        Log.Information("Tray run notification sent. TrayIconCreated={TrayIconCreated} WindowState={WindowState}", iconReady, WindowState);
    }
}