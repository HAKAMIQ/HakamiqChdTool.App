using System.Windows;
using System.Windows.Controls;
using HakamiqChdTool.App.Localization;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace HakamiqChdTool.App.Views.Main;

public partial class MainHeaderView : UserControl
{
    public event RoutedEventHandler? MinimizeRequested;
    public event RoutedEventHandler? MaximizeRestoreRequested;
    public event RoutedEventHandler? CloseRequested;

    public MainHeaderView()
    {
        InitializeComponent();
    }

    private void ForwardMinimizeRequested(object sender, RoutedEventArgs e)
    {
        MinimizeRequested?.Invoke(this, e);
    }

    private void ForwardMaximizeRestoreRequested(object sender, RoutedEventArgs e)
    {
        MaximizeRestoreRequested?.Invoke(this, e);
    }

    private void ForwardCloseRequested(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, e);
    }

    public void SetMaximizeRestoreState(WindowState windowState)
    {
        SetMaximizeRestoreState(windowState == WindowState.Maximized);
    }

    public void SetMaximizeRestoreState(bool isMaximized)
    {
        CaptionMaximizeRestoreButton.ToolTip = ArabicUi.Get(isMaximized ? "LocUi_Header_Restore" : "LocUi_Header_Maximize");
        CaptionMaximizeRestorePath.Data = TryFindGeometry(isMaximized ? "Icon.Restore" : "Icon.Maximize");
    }

    public void SyncThemeCycleButtonFromService()
    {
    }

    private static Geometry TryFindGeometry(string key)
    {
        return WpfApplication.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }
}
