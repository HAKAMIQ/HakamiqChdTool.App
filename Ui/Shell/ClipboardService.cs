using HakamiqChdTool.App.ViewModels.Dialogs;
using Serilog;
using System;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class ClipboardService : IClipboardService, IRedumpDetailsTextCopyService
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
