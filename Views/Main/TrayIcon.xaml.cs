using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HakamiqChdTool.App.Views.Main;

public partial class TrayNotifyIconView : UserControl, IDisposable
{
    private static readonly Uri TrayIconUri = new(
        "pack://application:,,,/Resources/HakamiqLogo.ico",
        UriKind.Absolute);

    private bool _disposed;

    public TrayNotifyIconView()
    {
        InitializeComponent();

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            InitializeTrayIconSource();
        }
    }

    public event RoutedEventHandler? TrayMouseDoubleClicked;

    public event RoutedEventHandler? ShowRequested;

    public event RoutedEventHandler? ExitRequested;

    public TaskbarIcon NotifyIcon => TrayNotifyIcon;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            TrayMouseDoubleClicked = null;
            ShowRequested = null;
            ExitRequested = null;

            TrayNotifyIcon.IconSource = null;
            TrayNotifyIcon.Dispose();
        }

        _disposed = true;
    }

    private void InitializeTrayIconSource()
    {
        BitmapFrame iconSource = BitmapFrame.Create(
            TrayIconUri,
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);

        if (iconSource.CanFreeze)
        {
            iconSource.Freeze();
        }

        TrayNotifyIcon.IconSource = iconSource;
    }

    private void ForwardTrayMouseDoubleClick(object sender, RoutedEventArgs e) =>
        TrayMouseDoubleClicked?.Invoke(sender, e);

    private void ForwardShowRequested(object sender, RoutedEventArgs e) =>
        ShowRequested?.Invoke(sender, e);

    private void ForwardExitRequested(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(sender, e);
}