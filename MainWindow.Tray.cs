using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Windows;

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

    private void TryNotifyTrayForBatchCompletion(
        bool wasCancelled,
        bool processedSelectionOnly,
        SessionRunMetrics metrics)
    {
        if (processedSelectionOnly || _shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        if (IsVisible && WindowState != WindowState.Minimized && IsActive)
        {
            return;
        }

        string title = ArabicUi.Get(
            wasCancelled
                ? MainWindowMessages.TraySessionCancelledTitle
                : MainWindowMessages.TraySessionCompletedTitle);

        string message = ArabicUi.Format(
            MainWindowMessages.Fmt_TraySessionBalloon,
            metrics.Completed,
            metrics.Failed);

        if (_shutdownStarted || _shutdownCompleted)
        {
            return;
        }

        TrayNotifyIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
    }
}