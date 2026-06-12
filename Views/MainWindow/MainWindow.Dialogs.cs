using System;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private bool ShowRenameConfirmationDialog(string newFileName)
    {
        if (string.IsNullOrWhiteSpace(newFileName))
        {
            return false;
        }

        var viewModel = new RenameConfirmationViewModel(newFileName.Trim());
        var dialog = new RenameConfirmationDialog(viewModel)
        {
            Owner = this
        };

        return dialog.ShowDialog() == true;
    }

    private StorageAdvisorDialogResult ShowStorageAdvisorDialog(StorageAdvisorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StorageAdvisorView presentation = StorageAdvisorPresenter.Present(result);
        if (!presentation.ShouldShowDialog)
        {
            return StorageAdvisorDialogResult.ContinueRecommended;
        }

        var dialog = new StorageAdvisorDialog(presentation)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();

        if (dialog.DoNotShowAgain)
        {
            _settings.SuppressStorageAdvisorDialog = true;
            PersistSettings();
        }

        return dialog.AdvisorResult;
    }

    private void ShowNoticeDialog(string title, string message)
    {
        string resolvedTitle = ResolveDialogText(title);
        string resolvedMessage = ResolveDialogText(message);

        if (string.IsNullOrWhiteSpace(resolvedTitle) ||
            string.IsNullOrWhiteSpace(resolvedMessage))
        {
            return;
        }

        var dialog = new RedumpNoticeDialog(
            resolvedTitle,
            resolvedMessage)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();
    }

    private bool RequireAppFeature(AppFeature feature)
    {
        return _appFeatureService.IsEnabled(feature);
    }

    private bool ShowCloseWhileProcessingConfirmationDialog()
    {
        var dialog = new ClearTaskLogConfirmationDialog(
            ArabicUi.Get(MainWindowMessages.CloseWhileProcessingTitle),
            ArabicUi.Get(MainWindowMessages.CloseWhileProcessingPrompt),
            ArabicUi.Get("LocCommon_Close"),
            ArabicUi.Get(MainWindowMessages.RenameDialogCancel))
        {
            Owner = this
        };

        return dialog.ShowDialog() == true;
    }
}