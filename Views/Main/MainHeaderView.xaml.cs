using System.Windows;
using System.Windows.Controls;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Ui.WpfAdapters;
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

        SyncThemeCycleButtonFromService();
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

    private void ThemeCycleButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.ToggleTheme();
        SyncThemeCycleButtonFromService();
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
        if (HeaderThemeText is null)
        {
            return;
        }

        bool isDarkTheme = ThemeService.Instance.IsDarkTheme;
        HeaderThemeButton.ToolTip = isDarkTheme ? "مظهر فاتح" : "مظهر داكن";

        HeaderThemeText.Text = isDarkTheme ? "☾" : "☀";
        HeaderThemeText.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol");
        HeaderThemeText.FontSize = 20;
        HeaderThemeText.FontWeight = FontWeights.SemiBold;
        HeaderThemeText.HorizontalAlignment = HorizontalAlignment.Center;
        HeaderThemeText.VerticalAlignment = VerticalAlignment.Center;
        HeaderThemeText.Foreground = isDarkTheme
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 197, 66));
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
