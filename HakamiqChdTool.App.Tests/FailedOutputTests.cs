using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Incomplete output from a failed job is always removed; the old setting no longer turns it off.
    private static void TestFailedOutputCleanupIsAlwaysOn()
    {
        string cleanup = File.ReadAllText(FindRepoFile(Path.Combine("Core", "Workflow", "FlowCleanup.cs")));
        AssertFalse(cleanup.Contains("settings.DeleteFailedOutput", StringComparison.Ordinal), "Cleanup does not read the old setting.");
        AssertTrue(cleanup.Contains("TryDeleteFailedCueBinBundle(failedOutputCandidate)", StringComparison.Ordinal), "A failed CUE/BIN still goes through the validated bundle delete.");
    }
}