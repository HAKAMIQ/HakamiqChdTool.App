using HakamiqChdTool.App.Views;
using System.Windows;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed class WpfDialogService : IDialogService
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
            Owner = WpfOwnerResolver.GetActiveOwner()
        };

        return dialog.ShowDialog() == true;
    }

    private static void Show(string title, string message)
    {
        var dialog = new RedumpNoticeDialog(title, message)
        {
            Owner = WpfOwnerResolver.GetActiveOwner()
        };

        _ = dialog.ShowDialog();
    }
}
