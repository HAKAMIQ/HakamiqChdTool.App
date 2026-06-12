using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Ui.Shell;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HakamiqChdTool.App;

public partial class MainWindow
{

    private const string RedumpApplyNameTitleKey = "LocRedump_ApplyNameTitle";

    private const string RedumpApplyNameCancelledFooterKey = "LocRedump_ApplyNameCancelledFooter";

    private const string RedumpApplyNameFailedFooterKey = "LocRedump_ApplyNameFailedFooter";

    private const string RedumpApplyNameNoApplicableFooterKey = "LocRedump_NoApplicableNameFooter";

    private const string RedumpApplyNameConfirmQuestionKey = "LocRedump_ConfirmRenameQuestion";

    private const string RedumpApplyNameSuccessFooterKey = "LocRedump_ApplyNameSuccessFooter";

    private const string RedumpApplyNameConfirmTextKey = "LocRedumpDetails_ApplyName";


    public bool CanApplyRedumpSuggestedName(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null ||
            IsQueueInteractionLocked ||
            !item.CanApplyRedumpSuggestedName ||
            !_appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion))
        {
            return false;
        }

        return RedumpNameService.Evaluate(item).IsApplicable;
    }


    public async Task ApplyRedumpSuggestedNameAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (!RequireAppFeature(AppFeature.StandardNamingSuggestion))
        {
            return;
        }

        if (item is null || !CanApplyRedumpSuggestedName(item))
        {
            SetFooterStatus(Ui(RedumpApplyNameNoApplicableFooterKey));
            return;
        }

        string oldPath = item.SourcePath;
        string suggestedName = item.SuggestedStandardName;

        RedumpNameSuggestion namingSuggestion = RedumpNameService.Evaluate(
            oldPath,
            suggestedName);

        if (!namingSuggestion.IsApplicable)
        {
            ShowRedumpNamingUnavailable(namingSuggestion);
            return;
        }

        var confirmVm = new RenameConfirmationViewModel(
            namingSuggestion.SourceFileName + Environment.NewLine + " -> " + namingSuggestion.SafeFileName,
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

        namingSuggestion = RedumpNameService.Evaluate(
            oldPath,
            suggestedName);

        if (!namingSuggestion.IsApplicable)
        {
            ShowRedumpNamingUnavailable(namingSuggestion);
            return;
        }

        try
        {
            (bool Success, string NewPath, string Error) result = await Task.Run(
                    () =>
                    {
                        bool success = NamingCorrectionEngine.TryApplyRedumpSuggestedRename(
                            oldPath,
                            namingSuggestion.SafeFileName,
                            out string newPath,
                            out string error);

                        return (success, newPath, error);
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
            item.SetIntegrityView(integrityState, integrityStatus, integrityTooltip);
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


    private void ShowRedumpNamingUnavailable(RedumpNameSuggestion namingSuggestion)
    {
        string error = string.IsNullOrWhiteSpace(namingSuggestion.ErrorMessageKey)
            ? Ui(RedumpApplyNameNoApplicableFooterKey)
            : ArabicUi.ResolveDisplayString(namingSuggestion.ErrorMessageKey);

        SetFooterStatus(Ui(RedumpApplyNameNoApplicableFooterKey));
        ShowRedumpNotice(RedumpApplyNameTitleKey, error);
    }
}
