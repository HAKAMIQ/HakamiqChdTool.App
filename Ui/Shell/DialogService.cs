using HakamiqChdTool.App.Views;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class DialogService : IDialogService
{
    public void ShowNotice(string title, string message)
    {
        Show(title, message);
    }

    public void ShowError(string title, string message)
    {
        Show(title, message);
    }

    public bool Confirm(string title, string message)
    {
        var dialog = new RedumpNoticeDialog(title, message)
        {
            Owner = OwnerResolver.GetActiveOwner()
        };

        return dialog.ShowDialog() == true;
    }

    private static void Show(string title, string message)
    {
        var dialog = new RedumpNoticeDialog(title, message)
        {
            Owner = OwnerResolver.GetActiveOwner()
        };

        _ = dialog.ShowDialog();
    }
}
