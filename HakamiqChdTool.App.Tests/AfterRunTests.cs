using HakamiqChdTool.App.Services.Power;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Sleep or shutdown runs only for a finished full run that asked for it; the shutdown command is the
    // System32 tool with /s /t 0 and no /f. Nothing here calls TryExecute.
    private static void TestAfterRunPolicy()
    {
        AssertTrue(AfterRunPower.ShouldRun(AfterRunAction.Shutdown, wasCancelled: false, processedSelectionOnly: false), "A finished run with Shut down asks for it.");
        AssertTrue(AfterRunPower.ShouldRun(AfterRunAction.Sleep, false, false), "A finished run with Sleep asks for it.");
        AssertFalse(AfterRunPower.ShouldRun(AfterRunAction.Shutdown, wasCancelled: true, processedSelectionOnly: false), "A stopped run never shuts down.");
        AssertFalse(AfterRunPower.ShouldRun(AfterRunAction.Shutdown, false, processedSelectionOnly: true), "A single-item run never shuts down.");
        AssertFalse(AfterRunPower.ShouldRun(AfterRunAction.None, false, false), "Nothing happens by default.");
        AssertTrue(AfterRunPower.Countdown >= TimeSpan.FromSeconds(30), "The user gets a real chance to cancel.");

        System.Diagnostics.ProcessStartInfo shutdown = AfterRunPower.BuildShutdownStartInfo();
        AssertEqual(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), shutdown.FileName, "The System32 shutdown tool is used.");
        AssertTrue(shutdown.ArgumentList.SequenceEqual(["/s", "/t", "0"]), "Shutdown is /s /t 0.");
        AssertFalse(shutdown.ArgumentList.Contains("/f"), "Other programs are not forced closed.");
        AssertFalse(shutdown.UseShellExecute, "No shell is involved.");

        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        foreach (string key in new[] { "LocAfterRun_Label", "LocAfterRun_Tip", "LocAfterRun_None", "LocAfterRun_Sleep", "LocAfterRun_Shutdown", "LocAfterRun_SleepTitle", "LocAfterRun_ShutdownTitle", "LocAfterRun_SleepMessage", "LocAfterRun_ShutdownMessage", "LocAfterRun_Now", "LocAfterRun_Cancel", "LocAfterRun_Failed" })
        {
            AssertTrue(english.ContainsKey(key) && arabic.ContainsKey(key), $"{key} exists in both languages.");
        }

        foreach (string key in new[] { "LocAfterRun_SleepMessage", "LocAfterRun_ShutdownMessage" })
        {
            AssertTrue(english[key].Contains("{0}", StringComparison.Ordinal) && arabic[key].Contains("{0}", StringComparison.Ordinal), $"{key} shows the seconds left.");
        }

        string dialog = File.ReadAllText(FindRepoFile(Path.Combine("Views", "AfterRunDlg.xaml")));
        int cancel = dialog.IndexOf("x:Name=\"CancelButton\"", StringComparison.Ordinal);
        AssertTrue(cancel > 0 && dialog.IndexOf("IsDefault=\"True\"", cancel, StringComparison.Ordinal) > cancel, "Cancel is the default button, so Enter cancels.");
        string queue = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Main", "QueueView.xaml")));
        AssertTrue(queue.Contains("x:Name=\"AfterRunComboBox\"", StringComparison.Ordinal) && queue.Contains("SelectedIndex=\"0\"", StringComparison.Ordinal), "The choice starts at nothing.");
    }
}
