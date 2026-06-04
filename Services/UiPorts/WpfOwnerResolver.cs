using System.Linq;
using System.Windows;

namespace HakamiqChdTool.App.Services.UiPorts;

internal static class WpfOwnerResolver
{
    public static Window? GetActiveOwner()
    {
        Application? application = Application.Current;
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
