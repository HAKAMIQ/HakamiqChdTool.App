using Serilog;
using System;
using System.Windows;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed class WpfClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Debug(ex, "Clipboard write failed.");
            return false;
        }
    }
}
