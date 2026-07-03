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
    public event RoutedEventHandler? LanguageToggleRequested;

    public MainHeaderView()
    {
        InitializeComponent();

        UpdateLanguageToggleButton();
        AppLanguageService.Instance.LanguageChanged += AppLanguageService_LanguageChanged;
        Unloaded += MainHeaderView_Unloaded;
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

    private void ForwardLanguageToggleRequested(object sender, RoutedEventArgs e)
    {
        LanguageToggleRequested?.Invoke(this, e);
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

    public void RefreshLanguageToggleButton()
    {
        UpdateLanguageToggleButton();
    }

    private void AppLanguageService_LanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateLanguageToggleButton();
            return;
        }

        Dispatcher.Invoke(UpdateLanguageToggleButton);
    }

    private void MainHeaderView_Unloaded(object sender, RoutedEventArgs e)
    {
        AppLanguageService.Instance.LanguageChanged -= AppLanguageService_LanguageChanged;
    }

    private void UpdateLanguageToggleButton()
    {
        bool isArabic = AppLanguageService.IsRightToLeftLanguage(AppLanguageService.Instance.CurrentLanguageName);
        HeaderLanguageText.Text = isArabic ? "EN" : "AR";
        HeaderLanguageButton.ToolTip = isArabic ? "Switch to English" : "التبديل إلى العربية";
    }

    private static Geometry TryFindGeometry(string key)
    {
        return WpfApplication.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }
}
