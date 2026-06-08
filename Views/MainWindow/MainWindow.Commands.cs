using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const string RedumpScanCancelledFooterKey = "LocRedump_ScanCancelledFooter";
    private const string RedumpSelectedScanFailedFooterKey = "LocRedump_SelectedScanFailedFooter";
    private const string RedumpScanTitleKey = "LocRedump_ScanTitle";
    private const string RedumpAllScanStartedFooterKey = "LocRedump_AllScanStartedFooter";
    private const string RedumpItemScanFailedContinueFooterFormatKey = "LocRedump_ItemScanFailedContinueFooterFormat";
    private const string RedumpAllScanStoppedFooterFormatKey = "LocRedump_AllScanStoppedFooterFormat";
    private const string RedumpAllScanCompletedWithFailuresFooterFormatKey = "LocRedump_AllScanCompletedWithFailuresFooterFormat";
    private const string RedumpAllScanCompletedFooterFormatKey = "LocRedump_AllScanCompletedFooterFormat";

    private const string RedumpApplyNameTitleKey = "LocRedump_ApplyNameTitle";
    private const string RedumpApplyNameCancelledFooterKey = "LocRedump_ApplyNameCancelledFooter";
    private const string RedumpApplyNameFailedFooterKey = "LocRedump_ApplyNameFailedFooter";
    private const string RedumpApplyNameNoApplicableFooterKey = "LocRedump_NoApplicableNameFooter";
    private const string RedumpApplyNameConfirmQuestionKey = "LocRedump_ConfirmRenameQuestion";
    private const string RedumpApplyNameSuccessFooterKey = "LocRedump_ApplyNameSuccessFooter";
    private const string RedumpApplyNameConfirmTextKey = "LocRedumpDetails_ApplyName";
    private const string CommonCancelKey = "LocCommon_Cancel";

    private async Task RunIntegrityContextAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        if (IsQueueInteractionLocked || item is null)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        string? path = ResolveQueueItemProbePath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            ShowRedumpNotice(
                MainWindowMessages.IntegrityNoDiskFileTitle,
                ArabicUi.Get(MainWindowMessages.IntegrityNoDiskFileBody));
            return;
        }

        await RunDeepIntegrityValidationAsync(item, path).ConfigureAwait(true);
    }

    private Task RunProcessSelectedInternalAsync(TaskQueueItemViewModel? item) =>
        _coordinator.ProcessSelectedAsync(item ?? TasksDataGrid.SelectedItem as TaskQueueItemViewModel);

    private Task RunVerifySelectedChdInternalAsync(TaskQueueItemViewModel? item) =>
        _coordinator.VerifySelectedChdAsync(item ?? TasksDataGrid.SelectedItem as TaskQueueItemViewModel);

    public bool CanRunRedumpIntegrityForSelectedQueueItem(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null
            || IsQueueInteractionLocked
            || !_settings.EnableDeepIntegrityCheck
            || !_appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity))
        {
            return false;
        }

        string path = ResolveRedumpProbePath(item);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public async Task RunRedumpIntegrityForSelectedQueueItemAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null || IsQueueInteractionLocked || !_settings.EnableDeepIntegrityCheck)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        string path = ResolveRedumpProbePath(item);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        try
        {
            await RunDeepIntegrityValidationAsync(item, path).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetFooterStatus(Ui(RedumpScanCancelledFooterKey));
        }
        catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
        {
            SetFooterStatus(Ui(RedumpSelectedScanFailedFooterKey));
            ShowRedumpNotice(
                RedumpScanTitleKey,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    public bool CanRunRedumpIntegrityForAnyQueueItem()
    {
        return !IsQueueInteractionLocked
            && _settings.EnableDeepIntegrityCheck
            && _appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity)
            && _queueView.Count > 0;
    }

    public async Task RunRedumpIntegrityForAllQueueItemsAsync()
    {
        if (IsQueueInteractionLocked || !_settings.EnableDeepIntegrityCheck)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        Guid[] itemIds = _queueRowStore.Rows
            .Select(row => row.ItemId)
            .ToArray();

        if (itemIds.Length == 0)
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        int eligibleCount = 0;
        int scannedCount = 0;
        int failedCount = 0;

        SetFooterStatus(Ui(RedumpAllScanStartedFooterKey));

        try
        {
            foreach (Guid itemId in itemIds)
            {
                if (_windowLifetimeCts.IsCancellationRequested || IsQueueInteractionLocked)
                {
                    break;
                }

                TaskQueueItemViewModel? item = _viewport.TryGetMaterialized(itemId);
                bool realizedForScan = false;

                if (item is null)
                {
                    int rowIndex = _queueRowStore.IndexOf(itemId);
                    if (rowIndex >= 0)
                    {
                        item = _viewport.Realize(rowIndex);
                        realizedForScan = item is not null;
                    }
                }

                try
                {
                    if (item is null)
                    {
                        continue;
                    }

                    string path = ResolveRedumpProbePath(item);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    eligibleCount++;

                    try
                    {
                        await RunDeepIntegrityValidationAsync(item, path).ConfigureAwait(true);
                        scannedCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
                    {
                        failedCount++;
                        SetFooterStatus(UiFormat(RedumpItemScanFailedContinueFooterFormatKey, failedCount));
                    }
                }
                finally
                {
                    if (realizedForScan)
                    {
                        _viewport.ReleaseById(itemId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetFooterStatus(Ui(RedumpScanCancelledFooterKey));
            return;
        }

        if (eligibleCount == 0)
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        if (_windowLifetimeCts.IsCancellationRequested || IsQueueInteractionLocked)
        {
            SetFooterStatus(UiFormat(RedumpAllScanStoppedFooterFormatKey, scannedCount));
            return;
        }

        if (failedCount > 0)
        {
            SetFooterStatus(UiFormat(RedumpAllScanCompletedWithFailuresFooterFormatKey, scannedCount, failedCount));
            return;
        }

        SetFooterStatus(UiFormat(RedumpAllScanCompletedFooterFormatKey, scannedCount));
    }

    public bool CanApplyRedumpSuggestedName(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        if (item is null
            || IsQueueInteractionLocked
            || !item.CanApplyRedumpSuggestedName
            || !_appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion))
        {
            return false;
        }

        return RedumpStandardNamingService.Evaluate(item).IsApplicable;
    }

    public async void ShowRedumpDetails(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        if (item is null)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        var dialog = new RedumpDetailsDialog(item, CanApplyRedumpSuggestedName(item))
        {
            Owner = this
        };

        bool? result = dialog.ShowDialog();
        if (result != true || !dialog.ApplyNameRequested)
        {
            return;
        }

        try
        {
            await ApplyRedumpSuggestedNameAsync(item).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetFooterStatus(Ui(RedumpApplyNameCancelledFooterKey));
        }
        catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
        {
            SetFooterStatus(Ui(RedumpApplyNameFailedFooterKey));
            ShowRedumpNotice(
                RedumpApplyNameTitleKey,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    public async Task ApplyRedumpSuggestedNameAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        if (!RequireAppFeature(AppFeature.StandardNamingSuggestion))
        {
            return;
        }

        if (!CanApplyRedumpSuggestedName(item) || item is null)
        {
            SetFooterStatus(Ui(RedumpApplyNameNoApplicableFooterKey));
            return;
        }

        string oldPath = item.SourcePath;
        string suggestedName = item.SuggestedStandardName;
        RedumpStandardNamingSuggestion namingSuggestion = RedumpStandardNamingService.Evaluate(oldPath, suggestedName);

        if (!namingSuggestion.IsApplicable)
        {
            string error = string.IsNullOrWhiteSpace(namingSuggestion.ErrorMessageKey)
                ? Ui(RedumpApplyNameNoApplicableFooterKey)
                : ArabicUi.ResolveDisplayString(namingSuggestion.ErrorMessageKey);

            SetFooterStatus(Ui(RedumpApplyNameNoApplicableFooterKey));
            ShowRedumpNotice(RedumpApplyNameTitleKey, error);
            return;
        }

        var confirmVm = new RenameConfirmationViewModel(
            namingSuggestion.SourceFileName + Environment.NewLine + "→ " + namingSuggestion.SafeFileName,
            Ui(RedumpApplyNameTitleKey),
            Ui(RedumpApplyNameConfirmQuestionKey),
            Ui(RedumpApplyNameConfirmTextKey),
            Ui(CommonCancelKey),
            Ui(RedumpApplyNameTitleKey));

        var confirmDialog = new RenameConfirmationDialog(confirmVm)
        {
            Owner = this
        };

        if (confirmDialog.ShowDialog() != true)
        {
            return;
        }

        namingSuggestion = RedumpStandardNamingService.Evaluate(oldPath, suggestedName);
        if (!namingSuggestion.IsApplicable)
        {
            string error = string.IsNullOrWhiteSpace(namingSuggestion.ErrorMessageKey)
                ? Ui(RedumpApplyNameNoApplicableFooterKey)
                : ArabicUi.ResolveDisplayString(namingSuggestion.ErrorMessageKey);

            SetFooterStatus(Ui(RedumpApplyNameNoApplicableFooterKey));
            ShowRedumpNotice(RedumpApplyNameTitleKey, error);
            return;
        }

        try
        {
            (bool Success, string NewPath, string Error) result = await Task.Run(
                    () =>
                    {
                        bool ok = NamingCorrectionEngine.TryApplyRedumpSuggestedRename(
                            oldPath,
                            namingSuggestion.SafeFileName,
                            out string newPath,
                            out string error);

                        return (ok, newPath, error);
                    },
                    _windowLifetimeCts.Token)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                string error = string.IsNullOrWhiteSpace(result.Error)
                    ? Ui(RedumpApplyNameFailedFooterKey)
                    : ArabicUi.ResolveDisplayString(result.Error);

                SetFooterStatus(Ui(RedumpApplyNameFailedFooterKey));
                ShowRedumpNotice(RedumpApplyNameTitleKey, error);
                return;
            }

            IntegrityValidationState integrityState = item.IntegrityState;
            string integrityStatus = item.IntegrityStatusMessage;
            string integrityTooltip = item.IntegrityDetailTooltip;

            ApplyPathResetAndSync(item, result.NewPath);
            item.SetIntegrityPresentation(integrityState, integrityStatus, integrityTooltip);
            item.SuggestedStandardName = Path.GetFileName(result.NewPath);
            item.IsNamingCompliant = true;
            SyncRowFromViewModel(item);

            SetFooterStatus(Ui(RedumpApplyNameSuccessFooterKey));
            _viewModel.NotifyQueueCommandsCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            SetFooterStatus(Ui(RedumpApplyNameCancelledFooterKey));
        }
        catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
        {
            SetFooterStatus(Ui(RedumpApplyNameFailedFooterKey));
            ShowRedumpNotice(
                RedumpApplyNameTitleKey,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    private void ShowRedumpNotice(string titleKey, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var dialog = new RedumpNoticeDialog(
            Ui(titleKey),
            message)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private static string ResolveRedumpProbePath(TaskQueueItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return item.SourcePath;
        }

        if (!string.IsNullOrWhiteSpace(item.OriginalPath))
        {
            return item.OriginalPath;
        }

        return string.Empty;
    }

    private static bool IsExpectedRedumpRuntimeException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
    }

    private static string Ui(string key) => ArabicUi.Get(key);

    private static string UiFormat(string key, params object[] args) => ArabicUi.Format(key, args);
}
