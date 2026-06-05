using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Windows;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void MainHeader_MinimizeRequested(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MainHeader_MaximizeRestoreRequested(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        MainHeader.SetMaximizeRestoreState(WindowState);
    }

    private void MainHeader_CloseRequested(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplySettingsToUi()
    {
        AppLanguageService.Instance.SetLanguage(_settings.UiLanguage);
        _settings.UiLanguage = AppLanguageService.Instance.CurrentLanguageName;

        ApplySettingsToUiWithoutLanguageChange();
    }

    private void ApplySettingsToUiWithoutLanguageChange()
    {
        ApplyThemeFromSettings();
        SyncThemeSelectorFromService();
        SyncFeatureVisibility();
        _viewModel.RefreshFeatureAccessDisplay();
        UpdateSidebarWorkflowSummary();
    }

    private void ApplyThemeFromSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.Theme))
        {
            return;
        }

        if (!string.Equals(_settings.Theme, ThemeService.Instance.CurrentThemeName, StringComparison.OrdinalIgnoreCase))
        {
            ThemeService.Instance.SetTheme(_settings.Theme);
        }
    }

    private void SyncThemeSelectorFromService()
    {
        MainHeader?.SyncThemeCycleButtonFromService();
    }

    private void SyncFeatureVisibility()
    {
        _viewModel.IsRedumpFeatureVisible =
            _settings.EnableDeepIntegrityCheck
            && _featureAccessService.CanUseFeature(PremiumFeature.RedumpDeepIntegrity);
        _viewModel.NotifyQueueCommandsCanExecuteChanged();
    }

    private void UpdateSidebarWorkflowSummary()
    {
        string afterKey = (_settings.DeleteTemporaryExtraction, _settings.DeleteFailedOutput) switch
        {
            (false, false) => "LocSidebar_AfterSuccess_Keep",
            (true, false) => "LocSidebar_AfterSuccess_TempOnly",
            (false, true) => "LocSidebar_AfterSuccess_FailedCleanup",
            _ => "LocSidebar_AfterSuccess_TempAndFailed"
        };

        SidebarWorkflowAfterSuccessText.Text = ArabicUi.Get(afterKey);
    }

    private void CaptureThemeIntoSettings()
    {
        _settings.Theme = ThemeService.Instance.CurrentThemeName;
        _settings.UiLanguage = AppLanguageService.Instance.CurrentLanguageName;
    }

    private void PersistSettings()
    {
        _ = _settingsService.SaveDebouncedAsync(_settings);
    }

    private void UpdateHeaderModeText()
    {
        string outputMode = _settings.UseCustomOutputRoot
            ? ArabicUi.Get(MainWindowUiKeys.Settings_OutputModeCustom)
            : ArabicUi.Get(MainWindowUiKeys.Settings_OutputModeNextToSource);

        string platformMode = _settings.OrganizeByPlatform
            ? ArabicUi.Get(MainWindowUiKeys.Settings_PlatformByPlatform)
            : ArabicUi.Get(MainWindowUiKeys.Settings_PlatformFlat);

        string verifyMode = _settings.VerifyAfterConversion
            ? ArabicUi.Get(MainWindowUiKeys.Settings_VerifyAfter)
            : ArabicUi.Get(MainWindowUiKeys.Settings_VerifyOff);

        string settingsLine = $"{outputMode} • {platformMode} • {verifyMode}";

        StatusBarSettingsText.Text = settingsLine;
        MainFooterStatusStrip.ToolTip = settingsLine;
    }

    private void RefreshFeatureAccessFromUi()
    {
        SyncFeatureVisibility();
        UpdateHeaderModeText();
        UpdateUiState();
    }

    private void ThemeService_ThemeChanged(object? sender, EventArgs e)
    {
        SyncThemeSelectorFromService();
        SyncFeatureVisibility();
    }
}
