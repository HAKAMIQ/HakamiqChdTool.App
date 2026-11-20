using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The Tools tab lists every bundled tool with its version and checks each file against the pin the app runs with.
    private static void TestBundledToolsAreListedAndChecked()
    {
        string root = Path.GetDirectoryName(FindRepoFile("HakamiqChdTool.App.csproj"))!;
        var statuses = HakamiqChdTool.App.Services.ExternalTools.BundledToolsCheck.Check(root);
        AssertEqual(5, statuses.Count, "chdman, 7-Zip and the three Xbox tools are listed.");
        AssertTrue(statuses.All(s => s.State == HakamiqChdTool.App.Services.ExternalTools.BundledToolState.Verified),
            "The repository copies match their pins: " + string.Join(", ", statuses.Select(s => s.Name + "=" + s.State)));

        string versions = File.ReadAllText(Path.Combine(root, "Tools", "VERSIONS.md"));
        foreach (var status in statuses)
        {
            AssertTrue(versions.Contains(status.Version, StringComparison.Ordinal), $"{status.Name} {status.Version} is the version in Tools/VERSIONS.md.");
        }

        string work = Path.Combine(Path.GetTempPath(), "hq-bundled-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(work, "Tools", "7zip"));
            File.Copy(Path.Combine(root, "Tools", "chdman.exe"), Path.Combine(work, "Tools", "chdman.exe"));
            File.WriteAllText(Path.Combine(work, "Tools", "7zip", "7z.exe"), "not 7-Zip");
            var copy = HakamiqChdTool.App.Services.ExternalTools.BundledToolsCheck.Check(work);
            AssertEqual(HakamiqChdTool.App.Services.ExternalTools.BundledToolState.Verified, copy.Single(s => s.Name == "chdman").State, "An unchanged copy is verified.");
            AssertEqual(HakamiqChdTool.App.Services.ExternalTools.BundledToolState.Missing, copy.Single(s => s.Name == "7-Zip").State, "A missing file wins over a changed one.");
            AssertEqual(HakamiqChdTool.App.Services.ExternalTools.BundledToolState.Missing, copy.Single(s => s.Name == "xdvdfs").State, "A missing tool is reported.");

            File.WriteAllText(Path.Combine(work, "Tools", "7zip", "7z.dll"), "not 7-Zip");
            AssertEqual(HakamiqChdTool.App.Services.ExternalTools.BundledToolState.Changed,
                HakamiqChdTool.App.Services.ExternalTools.BundledToolsCheck.Check(work).Single(s => s.Name == "7-Zip").State, "A replaced file is reported as changed.");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }
}
