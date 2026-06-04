using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Licensing;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using System;
using System.Linq;
using System.Windows;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private bool ShowRenameConfirmationDialog(string newFileName)
    {
        if (string.IsNullOrWhiteSpace(newFileName))
        {
            return false;
        }

        var viewModel = new RenameConfirmationViewModel(newFileName);
        var dialog = new RenameConfirmationDialog(viewModel)
        {
            Owner = this
        };

        return dialog.ShowDialog() == true;
    }

    private StorageAdvisorDialogResult ShowStorageAdvisorDialog(StorageAdvisorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StorageAdvisorPresentation presentation = StorageAdvisorPresenter.Present(result);
        if (!presentation.ShouldShowDialog || _settings.SuppressStorageAdvisorDialog)
        {
            return StorageAdvisorDialogResult.ContinueRecommended;
        }

        var dialog = new StorageAdvisorDialog(presentation)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();

        if (dialog.DoNotShowAgain && !_settings.SuppressStorageAdvisorDialog)
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

        if (string.IsNullOrWhiteSpace(resolvedTitle) || string.IsNullOrWhiteSpace(resolvedMessage))
        {
            return;
        }

        var dialog = new RedumpNoticeDialog(resolvedTitle, resolvedMessage)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();
    }

    private bool RequirePremiumFeature(PremiumFeature feature) =>
        _featureAccessService.CanUseFeature(feature);

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

    private void OperationModeRadio_Click(object sender, RoutedEventArgs e)
    {
        QueueWorkspace.OperationModeKey = ActionRail.DropHintModeKey;
        ApplySelectedOperationModeToWaitingRows();
        UpdateUiState();
    }

    private void ApplySelectedOperationModeToWaitingRows()
    {
        QueueOperationMode selectedMode = GetSelectedQueueOperationMode();
        if (selectedMode == QueueOperationMode.None || _queueRowStore.Count == 0)
        {
            return;
        }

        Guid[] rowIds =
        [
            .. _queueRowStore.Rows.Select(row => row.ItemId)
        ];

        foreach (Guid rowId in rowIds)
        {
            QueueRowData? row = _queueRowStore.GetById(rowId);
            if (row is null)
            {
                continue;
            }

            string operationPath = ResolveOperationModePath(row, selectedMode);
            bool nextVisible = QueueOperationModeResolver.IsPathVisibleForMode(operationPath, selectedMode);
            bool canRetarget = CanRetargetQueuedRow(row, selectedMode, operationPath);
            QueueOperationModeResolution resolution = QueueOperationModeResolver.ResolveRequestedActionForMode(operationPath, selectedMode);

            _queueRowStore.Mutate(row.ItemId, target =>
            {
                if (target.IsVisibleInCurrentOperationMode != nextVisible)
                {
                    target.IsVisibleInCurrentOperationMode = nextVisible;
                }

                if (!canRetarget || !nextVisible || !resolution.IsSupportedForSelectedMode)
                {
                    return;
                }

                target.RequestedAction = resolution.RequestedAction;
                if (selectedMode == QueueOperationMode.Verify)
                {
                    target.SourcePath = operationPath;
                    target.OutputPath = string.Empty;
                }

                target.CurrentState = TaskQueueStateCodes.Pending;
                target.StatusDetail = MainWindowMessages.ReadyForProcessing;
                target.Progress = 0;
                target.IsIndeterminate = false;
                target.IsProgressActive = false;
                target.FinalResult = TaskFinalResultCodes.None;
            });
        }

        _queueView.RefreshView();
        _queueUiAggregates.Rebuild(_queueRowStore.Rows);
        ResetSelectionAfterQueueFilterChanged();
    }

    private void ResetSelectionAfterQueueFilterChanged()
    {
        if (_queueView.Count == 0)
        {
            TasksDataGrid.SelectedItem = null;
            _viewModel.SelectedTask = null;
            return;
        }

        if (TasksDataGrid.SelectedItem is TaskQueueItemViewModel selected
            && _queueRowStore.GetById(selected.QueueItemId) is { IsVisibleInCurrentOperationMode: true })
        {
            return;
        }

        TasksDataGrid.SelectedIndex = 0;
        _viewModel.SelectedTask = TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
    }

    private static bool CanRetargetQueuedRow(QueueRowData row, QueueOperationMode selectedMode, string operationPath)
    {
        if (selectedMode == QueueOperationMode.Verify
            && IsExistingChdPath(operationPath)
            && !row.HasActiveQueueBinding)
        {
            return true;
        }

        if (TaskQueueStateCodes.IsTerminal(row.CurrentState))
        {
            return false;
        }

        if (IsTerminalFinalResult(row.FinalResult))
        {
            return false;
        }

        return string.Equals(row.CurrentState, TaskQueueStateCodes.Pending, StringComparison.Ordinal)
            || string.Equals(row.CurrentState, TaskQueueStateCodes.Ready, StringComparison.Ordinal)
            || string.Equals(row.CurrentState, TaskQueueStateCodes.AwaitingOperationSelection, StringComparison.Ordinal);
    }

    private static string ResolveOperationModePath(QueueRowData row, QueueOperationMode selectedMode)
    {
        if (selectedMode == QueueOperationMode.Verify)
        {
            if (IsExistingChdPath(row.SourcePath))
            {
                return row.SourcePath;
            }

            if (IsExistingChdPath(row.OriginalPath))
            {
                return row.OriginalPath;
            }

            if (IsExistingChdPath(row.OutputPath))
            {
                return row.OutputPath;
            }
        }

        return row.OriginalPath;
    }

    private static bool IsExistingChdPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && System.IO.File.Exists(path)
        && string.Equals(System.IO.Path.GetExtension(path), ".chd", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalFinalResult(string? finalResult)
    {
        if (string.IsNullOrWhiteSpace(finalResult)
            || string.Equals(finalResult, TaskFinalResultCodes.None, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(finalResult, TaskFinalResultCodes.Healthy, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.Moved, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.Extracted, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.SkippedExists, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.Failed, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.FailedConvert, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.FailedVerify, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.FailedExtract, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.Cancelled, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.PasswordRequired, StringComparison.Ordinal)
            || string.Equals(finalResult, TaskFinalResultCodes.Unsupported, StringComparison.Ordinal);
    }

    private QueueOperationMode GetSelectedQueueOperationMode()
    {
        if (ActionRail.IsVerifyMode)
        {
            return QueueOperationMode.Verify;
        }

        if (ActionRail.IsExtractMode)
        {
            return QueueOperationMode.Extract;
        }

        if (ActionRail.IsConvertMode)
        {
            return QueueOperationMode.Convert;
        }

        return QueueOperationMode.None;
    }
}
