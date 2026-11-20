using System.IO;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.ExternalTools.Igir;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // With several files at once, Auto splits the processors between them; an explicit count is kept.
    private static void TestAutoThreadsAreSharedAcrossConcurrentConversions()
    {
        int single = ProcessorTopologyService.ResolveChdmanProcessorCount(0, true, 0, ConversionPerformanceMode.Fast);
        AssertEqual(0, ProcessorTopologyService.ShareAutoProcessorCount(0, true, 0, ConversionPerformanceMode.Fast, 1),
            "One file at a time leaves Auto as Auto.");
        AssertEqual(6, ProcessorTopologyService.ShareAutoProcessorCount(6, true, 0, ConversionPerformanceMode.Fast, 4),
            "An explicit thread count is kept as chosen.");

        int shared = ProcessorTopologyService.ShareAutoProcessorCount(0, true, 0, ConversionPerformanceMode.Fast, 4);
        AssertEqual(Math.Max(1, single / 4), shared, "Four files share the Auto count.");
        AssertTrue(shared >= 1 && shared * 4 <= Math.Max(4, single), "Four chdman runs together stay within the Auto count.");
    }

    // A disc profile no longer refuses the other disc types in the queue; the hard-disk profile stays strict.
    private static void TestPlatformProfileFallsBackToAutoForOtherInputs()
    {
        AssertTrue(ChdPlatformProfiles.AppliesToInput(ChdPlatformProfiles.Ps1, ".cue"), "The PS1 profile applies to a CUE.");
        AssertFalse(ChdPlatformProfiles.AppliesToInput(ChdPlatformProfiles.Ps1, ".iso"), "The PS1 profile does not apply to an ISO.");
        AssertTrue(ChdPlatformProfiles.AppliesToInput(ChdPlatformProfiles.GenericHardDisk, ".iso"), "The hard-disk profile keeps judging every input.");

        ChdRequestedCreateProfileSelection iso = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            "createdvd", ".iso", "sony-ps1", ChdmanExtractionKind.None, @"C:\Samples\game.iso", @"C:\Samples\game.chd");
        AssertTrue(iso.FailureResult is null && iso.Profile is null && iso.Command == "createdvd",
            "An ISO with the PS1 profile selected converts with Auto instead of failing.");

        ChdRequestedCreateProfileSelection cue = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            "createcd", ".cue", "sony-ps1", ChdmanExtractionKind.None, @"C:\Samples\game.cue", @"C:\Samples\game.chd");
        AssertTrue(cue.FailureResult is null && cue.Profile == ChdPlatformProfiles.Ps1, "A CUE still uses the selected PS1 profile.");

        ChdRequestedCreateProfileSelection hdIso = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            "createdvd", ".iso", "generic-hd", ChdmanExtractionKind.None, @"C:\Samples\game.iso", @"C:\Samples\game.chd");
        AssertTrue(hdIso.FailureResult is not null, "The hard-disk profile still refuses an ISO.");
    }

    // A chosen igir that is not the pinned build is reported, and does not switch off the bundled igir.
    private static void TestChosenIgirDoesNotDisableBundledIgir()
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "igir-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string fake = Path.Combine(root, "igir.exe");
            File.WriteAllBytes(fake, [0x4D, 0x5A, 0x00, 0x01]);
            var locator = new IgirToolLocator();

            AssertFalse(locator.IsPinnedBuildAsync(fake).GetAwaiter().GetResult(), "A different igir.exe is not the pinned build.");
            AssertTrue(locator.FindVerifiedAsync(root, fake).GetAwaiter().GetResult() is null,
                "Without a bundled igir, a wrong chosen file finds nothing.");

            string repoRoot = Path.GetDirectoryName(FindRepoFile("HakamiqChdTool.App.csproj"))!;
            string bundled = Path.Combine(repoRoot, "Tools", "igir", "win-x64", "igir.exe");
            if (File.Exists(bundled))
            {
                string? found = locator.FindVerifiedAsync(repoRoot, fake).GetAwaiter().GetResult();
                AssertEqual(Path.GetFullPath(bundled), found, "A wrong chosen file falls back to the bundled igir.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
