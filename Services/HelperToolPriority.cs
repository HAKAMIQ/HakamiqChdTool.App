using System.ComponentModel;
using System.Diagnostics;
using HakamiqChdTool.App.Models.Chd;

namespace HakamiqChdTool.App.Services;

/// <summary>
/// The process priority setting covers the helper tools too (7-Zip for archives, csokit for CSO/ZSO,
/// igir reports), not only chdman. The app sets <see cref="Mode"/> from the settings at startup and
/// whenever the options are saved; the runners apply it right after each tool starts.
/// </summary>
public static class HelperToolPriority
{
    private static int _mode = (int)ChdmanProcessPriorityMode.Quiet;

    public static ChdmanProcessPriorityMode Mode
    {
        get => (ChdmanProcessPriorityMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    /// <summary>Lowers a started helper process in Quiet mode. Returns true when the priority was changed.</summary>
    public static bool TryApply(Process process)
    {
        if (Mode == ChdmanProcessPriorityMode.Normal)
        {
            return false;
        }

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The tool may already have exited; its priority no longer matters.
            Serilog.Log.Debug(ex, "Helper tool priority was not changed.");
            return false;
        }
    }
}
