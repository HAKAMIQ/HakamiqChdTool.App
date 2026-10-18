using HakamiqChdTool.App.Services;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The cleanup service is the app's only generic deleter, so its root boundary is a safety property:
    // it may remove files the app staged under its own process temp root, and nothing else.
    private static void TestCleanupServiceHonorsProcessTempBoundary(string workDirectory)
    {
        string outside = Path.Combine(workDirectory, "cleanup-boundary-outside");
        Directory.CreateDirectory(outside);
        string outsideFile = Path.Combine(outside, "user-output.chd");
        File.WriteAllBytes(outsideFile, [1, 2, 3]);

        CleanupService cleanup = new();
        AssertEqual(0, cleanup.DeleteFiles(outsideFile).DeletedFiles, "A file outside the process temp root is never deleted.");
        AssertTrue(File.Exists(outsideFile), "The file outside the temp root is untouched.");
        AssertEqual(0, cleanup.DeleteDirectoryTree(outside).DeletedFiles, "A directory outside the process temp root is never deleted.");
        AssertTrue(Directory.Exists(outside), "The directory outside the temp root is untouched.");

        string inside = AppPaths.CombineProcessTemp("CleanupBoundaryTest", System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inside);
        string insideFile = Path.Combine(inside, "staged.tmp");
        File.WriteAllBytes(insideFile, [1, 2, 3]);

        AssertEqual(1, cleanup.DeleteFiles(insideFile).DeletedFiles, "A staged file under the process temp root is deleted.");
        AssertFalse(File.Exists(insideFile), "The staged file is gone.");
        cleanup.DeleteDirectoryTree(inside);
        AssertFalse(Directory.Exists(inside), "The staged directory is removed.");
    }
}
