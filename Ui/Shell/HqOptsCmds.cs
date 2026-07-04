using System.Windows;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Views;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
        public void RestoreDefaults(object sender, RoutedEventArgs e)
        {
            _owner.ViewModel.ApplyProgramDefaults();
            EnforceFeatureAvailabilityOnViewModel(showDialog: false);
        }

        public void Apply(object sender, RoutedEventArgs e)
        {
            _ = TryApplySettings();
        }

        public void Ok(object sender, RoutedEventArgs e)
        {
            if (!TryApplySettings())
            {
                return;
            }

            _owner.DialogResult = true;
            _owner.Close();
        }

        public void Cancel(object sender, RoutedEventArgs e)
        {
            _owner.DialogResult = false;
            _owner.Close();
        }

        private bool TryApplySettings()
        {
            _owner.ViewModel.ValidateForSave();

            if (!_owner.ViewModel.CanConfirm)
            {
                string firstError = ResolveKeyOrText(_owner.ViewModel.GetFirstErrorMessage());
                if (string.IsNullOrWhiteSpace(firstError))
                {
                    firstError = ResolveUiText(InvalidFieldFallbackKey);
                }

                ShowNoticeDialog(InvalidDataTitleKey, firstError);
                return false;
            }

            if (!_owner.ViewModel.HasPendingChanges)
            {
                return true;
            }

            string previousLanguage = AppLanguageService.Instance.CurrentLanguageName;

            AppSettings pendingSettings = _owner.ViewModel.BuildResultSettings(_currentSettings);
            if (!TryValidateAppFeatureChanges(pendingSettings))
            {
                return false;
            }

            _owner.ResultSettings = pendingSettings;
            _owner.NotifySettingsApplied(_owner.ResultSettings.Clone());
            _owner.ViewModel.AcceptAppliedSettings(_owner.ResultSettings);

            RefreshAfterAppliedSettings(previousLanguage);
            return true;
        }

        private void ShowNoticeDialog(string title, string message)
        {
            string resolvedTitle = ResolveKeyOrText(title);
            string resolvedMessage = ResolveKeyOrText(message);

            if (string.IsNullOrWhiteSpace(resolvedTitle) || string.IsNullOrWhiteSpace(resolvedMessage))
            {
                return;
            }

            var dialog = new RedumpNoticeDialog(resolvedTitle, resolvedMessage)
            {
                Owner = _owner
            };

            _ = dialog.ShowDialog();
        }
}
