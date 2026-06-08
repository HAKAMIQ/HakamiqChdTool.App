using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    internal void OpenAdvancedOptionsDialog(string? initialTabKey = null)
    {
        var dialog = new AdvancedOptionsWindow(_settings, _appFeatureService)
        {
            Owner = this
        };

        if (!string.IsNullOrWhiteSpace(initialTabKey))
        {
            dialog.SelectTab(initialTabKey);
        }

        bool appliedDuringDialog = false;

        void ApplySettings(AppSettings settings)
        {
            string previousLanguage = AppLanguageService.NormalizeLanguageName(_settings.UiLanguage);
            string requestedLanguage = AppLanguageService.NormalizeLanguageName(settings.UiLanguage);
            bool languageChanged = !string.Equals(previousLanguage, requestedLanguage, StringComparison.OrdinalIgnoreCase);

            _settings.CopyFrom(settings);
            _queue.UpdateMaxConcurrentItems(_settings.MaxConcurrentConversions);
            _appFeatureService.ApplyFeatureAvailability(_settings);
            _settings.UiLanguage = requestedLanguage;

            if (languageChanged)
            {
                AppLanguageService.Instance.SetLanguage(requestedLanguage);
                _settings.UiLanguage = AppLanguageService.Instance.CurrentLanguageName;

                ApplySettingsToUiWithoutLanguageChange();
                UpdateHeaderModeText();
                UpdateUiState();
                PersistSettings();

                ApplicationRestartContext restartContext = ApplicationRestartService.CreateRestartContext(
                    this,
                    ApplicationRestartContext.AdvancedOptionsWindowName,
                    dialog.ActiveTabKey);

                _ = ApplicationRestartService.TryRestartCurrentApplication(restartContext);
            }
            else
            {
                ApplySettingsToUi();
                UpdateHeaderModeText();
                UpdateUiState();
                PersistSettings();
            }

            appliedDuringDialog = true;
        }

        void Dialog_SettingsApplied(object? sender, AdvancedOptionsAppliedEventArgs e)
        {
            ApplySettings(e.Settings);
        }

        dialog.SettingsApplied += Dialog_SettingsApplied;

        bool? result;
        try
        {
            result = dialog.ShowDialog();
        }
        finally
        {
            dialog.SettingsApplied -= Dialog_SettingsApplied;
        }

        if (result == true && !appliedDuringDialog)
        {
            ApplySettings(dialog.ResultSettings);
        }
    }

    internal void OpenAboutDialog()
    {
        var aboutInfo = AboutInfoFactory.Create(_appMetadata);
        var aboutVm = new AboutWindowViewModel(aboutInfo, _externalLinkService);
        var dialog = new AboutWindow(aboutVm)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }
}
