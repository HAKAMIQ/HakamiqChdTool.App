using System;

using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.Features;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
        private bool TryValidateAppFeatureChanges(AppSettings pendingSettings)
        {
            ArgumentNullException.ThrowIfNull(pendingSettings);

            if (pendingSettings.EnableDeepIntegrityCheck
                && !RequireAppFeature(AppFeature.RedumpDeepIntegrity))
            {
                EnforceFeatureAvailabilityOnViewModel(showDialog: false);
                return false;
            }

            if (pendingSettings.ApplyStandardNamingBasedOnHash
                && !RequireAppFeature(AppFeature.StandardNamingSuggestion))
            {
                EnforceFeatureAvailabilityOnViewModel(showDialog: false);
                return false;
            }

            if (pendingSettings.EnableRedumpAutoSync
                && !RequireAppFeature(AppFeature.RedumpDatabaseImport))
            {
                EnforceFeatureAvailabilityOnViewModel(showDialog: false);
                return false;
            }

            if (!pendingSettings.SuppressStorageAdvisorDialog
                && !RequireAppFeature(AppFeature.StorageAdvisor))
            {
                EnforceFeatureAvailabilityOnViewModel(showDialog: false);
                return false;
            }

            if (RequiresPostProcessingAutomation(pendingSettings)
                && !RequireAppFeature(AppFeature.PostProcessingAutomation))
            {
                EnforceFeatureAvailabilityOnViewModel(showDialog: false);
                return false;
            }

            return true;
        }

        private static bool RequiresPostProcessingAutomation(AppSettings settings) =>
            settings.CopyMatchingSbi
            || settings.EnableAutoM3uGeneration
            || settings.OverwriteExistingM3uPlaylists;

        private void EnforceFeatureAvailabilityOnViewModel(bool showDialog)
        {
            ApplyFeatureAvailabilityToViewModel();

            if (!_appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity))
            {
                _owner.ViewModel.EnableDeepIntegrityCheck = false;
            }

            if (!_appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion))
            {
                _owner.ViewModel.ApplyStandardNamingBasedOnHash = false;
            }

            if (!_appFeatureService.IsEnabled(AppFeature.RedumpDatabaseImport))
            {
                _owner.ViewModel.EnableRedumpAutoSync = false;
            }

            if (!_appFeatureService.IsEnabled(AppFeature.StorageAdvisor))
            {
                _owner.ViewModel.ShowStorageAdvisorDialog = false;
            }

            if (!_appFeatureService.IsEnabled(AppFeature.PostProcessingAutomation))
            {
                _owner.ViewModel.CopyMatchingSbi = false;
                _owner.ViewModel.EnableAutoM3uGeneration = false;
                _owner.ViewModel.OverwriteExistingM3uPlaylists = false;
            }

            _ = showDialog;
        }

        private void ApplyFeatureAvailabilityToViewModel()
        {
            _owner.ViewModel.CanUsePostProcessingAutomation = _appFeatureService.IsEnabled(AppFeature.PostProcessingAutomation);
            _owner.ViewModel.CanUseRedumpDeepIntegrity = _appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity);
            _owner.ViewModel.CanUseRedumpDatabaseImport = _appFeatureService.IsEnabled(AppFeature.RedumpDatabaseImport);
            _owner.ViewModel.CanUseStandardNamingSuggestion = _appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion);
            _owner.ViewModel.CanUseStorageAdvisor = _appFeatureService.IsEnabled(AppFeature.StorageAdvisor);
        }

        private bool RequireAppFeature(AppFeature feature) =>
            _appFeatureService.IsEnabled(feature);
}
