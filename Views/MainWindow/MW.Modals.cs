using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Library;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.Windows;
using HakamiqChdTool.App.Ui.WpfAdapters;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    internal void OpenOptionsDialog(string? initialTabKey = null)
    {
        OptionsWindow dialog;
        try
        {
            dialog = new OptionsWindow(_settings)
            {
                Owner = this
            };

            if (!string.IsNullOrWhiteSpace(initialTabKey))
            {
                dialog.SelectTab(initialTabKey);
            }
        }
        catch (Exception ex)
        {
            global::Serilog.Log.Error(ex, "Options window construction failed.");
            SetFooterStatus(MainWindowMessages.StartupRuntimeErrorBody(RuntimeDiagnosticFormatter.SummarizeException(ex)));
            ShowNoticeDialog(
                MainWindowMessages.StartupRuntimeErrorTitle,
                MainWindowMessages.StartupRuntimeErrorBody(RuntimeDiagnosticFormatter.SummarizeException(ex)));
            return;
        }

        bool appliedDuringDialog = false;

        void ApplySettings(AppSettings settings)
        {
            string previousLanguage = AppLanguageService.NormalizeLanguageName(_settings.UiLanguage);
            string requestedLanguage = AppLanguageService.NormalizeLanguageName(settings.UiLanguage);
            bool languageChanged = !string.Equals(previousLanguage, requestedLanguage, StringComparison.OrdinalIgnoreCase);

            _settings.CopyFrom(settings);
            _queue.UpdateMaxConcurrentItems(_settings.MaxConcurrentConversions);
            _settings.UiLanguage = requestedLanguage;

            if (languageChanged)
            {
                AppLanguageService.Instance.SetLanguage(requestedLanguage);
                _settings.UiLanguage = AppLanguageService.Instance.CurrentLanguageName;

                ApplySettingsToUiWithoutLanguageChange();
                UpdateHeaderModeText();
                UpdateUiState();
                PersistSettings();

                global::Serilog.Log.Information(
                    "Options language changed live. PreviousLanguage={PreviousLanguage}, CurrentLanguage={CurrentLanguage}, ActiveTab={ActiveTab}",
                    previousLanguage,
                    _settings.UiLanguage,
                    dialog.ActiveTabKey);
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

        void Dialog_SettingsApplied(object? sender, OptionsAppliedEventArgs e)
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

    private LibraryAnalysisWindow? _libraryAnalysisWindow;

    // Read-only and non-modal; one window (and so one analysis session) at a time.
    internal void OpenLibraryAnalysisDialog()
    {
        if (_libraryAnalysisWindow is not null)
        {
            _libraryAnalysisWindow.Activate();
            return;
        }

        LibraryAnalysisWindow window;
        try
        {
            var service = new LibraryAnalysisService();
            var viewModel = new LibraryAnalysisViewModel(
                service.AnalyzeAsync,
                new OptionsPickerService(),
                confirm: key => MessageBox.Show(this, (string)FindResource(key),
                    (string)FindResource("LocLib_Title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                    == MessageBoxResult.OK);
            window = new LibraryAnalysisWindow(viewModel)
            {
                Owner = this
            };
        }
        catch (Exception ex)
        {
            global::Serilog.Log.Error(ex, "Library analysis window construction failed.");
            ShowNoticeDialog(
                MainWindowMessages.StartupRuntimeErrorTitle,
                MainWindowMessages.StartupRuntimeErrorBody(RuntimeDiagnosticFormatter.SummarizeException(ex)));
            return;
        }

        window.Closed += (_, _) => _libraryAnalysisWindow = null;
        _libraryAnalysisWindow = window;
        window.Show();
    }
}
