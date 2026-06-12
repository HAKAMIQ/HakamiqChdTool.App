using System;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Shell;

internal static class WindowBackdrop
{
    internal static void ApplyMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
    }

    internal static void ApplyDialog(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
    }
}
