using System.Diagnostics;
using System.IO;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Quiet priority lowers the helper tools (7-Zip, csokit, igir) as it does chdman; Normal leaves them alone.
    private static void TestHelperToolsFollowThePrioritySetting()
    {
        ChdmanProcessPriorityMode saved = HelperToolPriority.Mode;
        try
        {
            HelperToolPriority.Mode = ChdmanProcessPriorityMode.Quiet;
            using (Process quiet = StartIdleProcess())
            {
                AssertTrue(HelperToolPriority.TryApply(quiet), "Quiet mode changes a helper's priority.");
                quiet.Refresh();
                AssertEqual(ProcessPriorityClass.BelowNormal, quiet.PriorityClass, "Quiet mode runs a helper below normal.");
                quiet.Kill();
            }

            HelperToolPriority.Mode = ChdmanProcessPriorityMode.Normal;
            using (Process normal = StartIdleProcess())
            {
                AssertFalse(HelperToolPriority.TryApply(normal), "Normal mode leaves a helper's priority alone.");
                normal.Refresh();
                AssertEqual(ProcessPriorityClass.Normal, normal.PriorityClass, "Normal mode keeps normal priority.");
                normal.Kill();
            }
        }
        finally
        {
            HelperToolPriority.Mode = saved;
        }

        foreach (string runner in new[] { Path.Combine("Services", "SevenZipRun.cs"), Path.Combine("Services", "ExternalTools", "ToolProcRun.cs") })
        {
            AssertTrue(File.ReadAllText(FindRepoFile(runner)).Contains("HelperToolPriority.TryApply(process);", StringComparison.Ordinal),
                $"{runner} applies the priority setting to the tool it starts.");
        }
    }

    private static Process StartIdleProcess() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");
}
