using System.IO;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.M3u;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // "Organize by region" puts the output in a region folder, under the platform folder when both are on.
    private static void TestOrganizeByRegionBuildsTheRegionFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "region-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string tagged = Path.Combine(root, "Sample Game (USA).cue");
            string untagged = Path.Combine(root, "Sample Game.cue");
            File.WriteAllText(tagged, "sample");
            File.WriteAllText(untagged, "sample");

            string regionOnly = WorkflowOutputPathContract.ResolveOutputRootDirectory(
                tagged, tagged, "Sony PlayStation", new AppSettings { OrganizeByRegion = true });
            AssertEqual(Path.Combine(root, "USA"), regionOnly, "Region only adds the region folder next to the source.");

            string both = WorkflowOutputPathContract.ResolveOutputRootDirectory(
                tagged, tagged, "Sony PlayStation", new AppSettings { OrganizeByPlatform = true, OrganizeByRegion = true });
            AssertEqual(Path.Combine(root, "Sony PlayStation", "USA"), both, "Platform and region nest as platform then region.");

            string none = WorkflowOutputPathContract.ResolveOutputRootDirectory(
                untagged, untagged, "Sony PlayStation", new AppSettings { OrganizeByRegion = true });
            AssertEqual(root, none, "A file with no known region stays next to its source.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A converted multi-disc game gets one M3U that lists the discs in order; an existing M3U is kept.
    private static void TestM3uListsDiscsInOrderAndKeepsExistingFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "m3u-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string disc2 = Path.Combine(root, "Sample Game (USA) (Disc 2).chd");
            string disc1 = Path.Combine(root, "Sample Game (USA) (Disc 1).chd");
            File.WriteAllBytes(disc2, [2]);
            File.WriteAllBytes(disc1, [1]);

            IReadOnlyList<MultiDiscSet> sets = new MultiDiscSetDetector().Detect([disc2, disc1]);
            AssertEqual(1, sets.Count, "The two discs form one set.");

            var generator = new M3uPlaylistGenerator();
            M3uGenerationResult first = generator.Generate(sets, overwriteExisting: false);
            AssertEqual(1, first.GeneratedCount, "One playlist is written.");
            string playlist = first.GeneratedPaths.Single();
            string[] lines = File.ReadAllLines(playlist).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).ToArray();
            AssertTrue(lines.Length == 2 && lines[0].EndsWith("(Disc 1).chd", StringComparison.Ordinal) && lines[1].EndsWith("(Disc 2).chd", StringComparison.Ordinal),
                "The playlist lists disc 1 then disc 2.");

            M3uGenerationResult second = generator.Generate(sets, overwriteExisting: false);
            AssertTrue(second.GeneratedCount == 0 && second.SkippedExistingCount == 1, "An existing playlist is kept, not overwritten.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
