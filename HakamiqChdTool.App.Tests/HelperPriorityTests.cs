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

            // A child starts with its parent's priority, and CI runners may run below normal,
            // so "left alone" means the helper keeps the priority it inherited from this process.
            HelperToolPriority.Mode = ChdmanProcessPriorityMode.Normal;
            using Process self = Process.GetCurrentProcess();
            ProcessPriorityClass inherited = self.PriorityClass;
            using (Process normal = StartIdleProcess())
            {
                AssertFalse(HelperToolPriority.TryApply(normal), "Normal mode leaves a helper's priority alone.");
                normal.Refresh();
                AssertEqual(inherited, normal.PriorityClass, "Normal mode keeps the priority the helper inherited.");
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
