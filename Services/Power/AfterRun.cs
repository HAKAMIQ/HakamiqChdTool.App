using Serilog;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Services.Power;

public enum AfterRunAction
{
    None = 0,
    Sleep = 1,
    Shutdown = 2
}

/// <summary>
/// Puts the computer to sleep or shuts it down after a queue run, when the user picked that for the run.
/// It never fires for a cancelled run or a single-item run, and the window shows a cancellable countdown first.
/// Shutdown uses <c>%SystemRoot%\System32\shutdown.exe /s /t 0</c> without <c>/f</c>, so other programs can
/// still ask to save their work.
/// </summary>
internal static class AfterRunPower
{
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(60);

    public static bool ShouldRun(AfterRunAction action, bool wasCancelled, bool processedSelectionOnly) =>
        action != AfterRunAction.None && !wasCancelled && !processedSelectionOnly;

    public static ProcessStartInfo BuildShutdownStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/t");
        startInfo.ArgumentList.Add("0");
        return startInfo;
    }

    public static bool TryExecute(AfterRunAction action)
    {
        try
        {
            switch (action)
            {
                case AfterRunAction.Sleep:
                    Log.Information("Queue finished; putting the computer to sleep as requested.");
                    return SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                case AfterRunAction.Shutdown:
                    Log.Information("Queue finished; shutting the computer down as requested.");
                    using (Process? process = Process.Start(BuildShutdownStartInfo()))
                    {
                        return process is not null;
                    }

                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning(ex, "After-run power action {Action} failed.", action);
            return false;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
}
