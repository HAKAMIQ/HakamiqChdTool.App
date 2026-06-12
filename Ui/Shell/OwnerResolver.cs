using System.Linq;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Shell;

internal static class OwnerResolver
{
    public static Window? GetActiveOwner()
    {
        System.Windows.Application? application = System.Windows.Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Windows
            .OfType<Window>()
            .FirstOrDefault(static window => window.IsActive && window.IsVisible)
            ?? (application.MainWindow?.IsVisible == true ? application.MainWindow : null);
    }
}
