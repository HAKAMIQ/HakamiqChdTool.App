using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Dialogs;
using HakamiqChdTool.App.Views;

namespace HakamiqChdTool.App.Ui.Shell;

public static class RedumpDetailsFactory
{
    public static RedumpDetailsDialog Create(TaskQueueItemViewModel item, bool canApplyName)
    {
        ArgumentNullException.ThrowIfNull(item);

        var viewModel = new RedumpDetailsDialogViewModel(
            item,
            canApplyName,
            new ClipboardService(),
            new RedumpFeedbackTimer());

        return new RedumpDetailsDialog(viewModel);
    }
}
