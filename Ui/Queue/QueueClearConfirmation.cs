using HakamiqChdTool.App.Views;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Queue;

public static class QueueClearConfirmation
{
    public static bool Confirm(Window? owner)
    {
        var dialog = new ClearTaskLogConfirmationDialog
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }
}