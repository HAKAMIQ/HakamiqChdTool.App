using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HakamiqChdTool.App.Ui.Shell;

internal static class WindowBackdrop
{
    private const int WindowCornerPreferenceAttribute = 33;
    private const int RoundCornerPreference = 2;

    internal static void ApplyMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
    }

    internal static void ApplyDialog(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
    }

    internal static void ApplyRoundedDialog(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        ApplyDialog(window);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        nint handle = new WindowInteropHelper(window).Handle;

        if (handle != 0)
        {
            ApplyRoundedCorners(handle);
            return;
        }

        window.SourceInitialized -= RoundedDialog_SourceInitialized;
        window.SourceInitialized += RoundedDialog_SourceInitialized;
    }

    private static void RoundedDialog_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.SourceInitialized -= RoundedDialog_SourceInitialized;

        nint handle = new WindowInteropHelper(window).Handle;

        if (handle == 0)
        {
            return;
        }

        ApplyRoundedCorners(handle);
    }

    private static void ApplyRoundedCorners(nint handle)
    {
        int preference = RoundCornerPreference;

        _ = DwmSetWindowAttribute(
            handle,
            WindowCornerPreferenceAttribute,
            ref preference,
            sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
