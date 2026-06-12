using System;
using System.Threading.Tasks;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Ui.Shell;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private Task RunProcessSelectedInternalAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        return _coordinator.ProcessSelectedAsync(item);
    }

    private Task RunVerifySelectedChdInternalAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        return _coordinator.VerifySelectedChdAsync(item);
    }

    public async Task ShowRedumpDetails(TaskQueueItemViewModel? item)
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

        var dialog = RedumpDetailsFactory.Create(
            item,
            CanApplyRedumpSuggestedName(item));

        dialog.Owner = this;

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
}