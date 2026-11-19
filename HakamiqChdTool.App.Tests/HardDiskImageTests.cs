using HakamiqChdTool.App.Core.Chd.Commands;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private const string HardDiskProfileId = "generic-hd";

    private static string CreateHardDiskFixture(string directory, string name, int sizeBytes, int seed = 7)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        byte[] data = new byte[sizeBytes];
        new Random(seed).NextBytes(data);
        data[0] = 0x45; // never a CD sync byte
        File.WriteAllBytes(path, data);
        return path;
    }

    private static void TestHardDiskEvidenceRejectsDiscImages(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "hdd-evidence");

        string plain = CreateHardDiskFixture(Path.Combine(root, "plain"), "drive.img", 64 * 1024);
        AssertEqual(HardDiskImageEvidence.Accepted, HardDiskImageEvidenceProbe.Evaluate(plain), "A plain sector-aligned image is accepted.");

        string hdd = CreateHardDiskFixture(Path.Combine(root, "plain"), "drive2.hdd", 64 * 1024);
        AssertEqual(HardDiskImageEvidence.Accepted, HardDiskImageEvidenceProbe.Evaluate(hdd), ".hdd is accepted like .img.");

        string sibling = CreateHardDiskFixture(Path.Combine(root, "sibling"), "disc.img", 64 * 1024);
        File.WriteAllText(Path.Combine(root, "sibling", "disc.ccd"), "[CloneCD]\r\nVersion=3\r\n");
        AssertEqual(HardDiskImageEvidence.DescriptorSibling, HardDiskImageEvidenceProbe.Evaluate(sibling), "A same-name CCD marks the image as a disc.");

        string referenced = CreateHardDiskFixture(Path.Combine(root, "referenced"), "track01.img", 64 * 1024);
        File.WriteAllText(
            Path.Combine(root, "referenced", "game.cue"),
            "FILE \"track01.img\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n",
            Encoding.ASCII);
        AssertEqual(HardDiskImageEvidence.ReferencedByDescriptor, HardDiskImageEvidenceProbe.Evaluate(referenced), "A CUE that references the image marks it as a track.");

        // 32 raw sectors keep the file 512-byte aligned so the content probe is reached.
        string syncPath = Path.Combine(root, "sync", "raw.img");
        Directory.CreateDirectory(Path.GetDirectoryName(syncPath)!);
        byte[] syncData = new byte[32 * 2352];
        syncData[0] = 0x00;
        for (int i = 1; i <= 10; i++)
        {
            syncData[i] = 0xFF;
        }
        File.WriteAllBytes(syncPath, syncData);
        AssertEqual(HardDiskImageEvidence.CdSyncHeader, HardDiskImageEvidenceProbe.Evaluate(syncPath), "A CD sync header is rejected.");

        string isoPath = Path.Combine(root, "iso", "cooked.img");
        Directory.CreateDirectory(Path.GetDirectoryName(isoPath)!);
        byte[] isoData = new byte[18 * 2048];
        Encoding.ASCII.GetBytes("CD001").CopyTo(isoData, (16 * 2048) + 1);
        File.WriteAllBytes(isoPath, isoData);
        AssertEqual(HardDiskImageEvidence.Iso9660Volume, HardDiskImageEvidenceProbe.Evaluate(isoPath), "An ISO9660 volume descriptor is rejected.");

        string odd = CreateHardDiskFixture(Path.Combine(root, "odd"), "odd.img", 1000);
        AssertEqual(HardDiskImageEvidence.NotSectorAligned, HardDiskImageEvidenceProbe.Evaluate(odd), "A size that is not a multiple of 512 is rejected.");

        string emptyPath = Path.Combine(root, "odd", "empty.img");
        File.WriteAllBytes(emptyPath, []);
        AssertEqual(HardDiskImageEvidence.Empty, HardDiskImageEvidenceProbe.Evaluate(emptyPath), "An empty file is rejected.");

        AssertEqual(HardDiskImageEvidence.NotHardDiskExtension, HardDiskImageEvidenceProbe.Evaluate(Path.Combine(root, "x.iso")), "Other extensions are not hard-disk candidates.");
    }

    private static void TestHardDiskIntakeOnlyAcceptsImagesWithoutDiscEvidence(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "hdd-intake");

        string plain = CreateHardDiskFixture(Path.Combine(root, "plain"), "drive.img", 64 * 1024);
        AssertEqual(MediaInputKind.HardDiskImage, MediaInputClassifier.Shared.Classify(plain).Kind, "Accepted image is classified as a hard-disk image.");
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(plain).Role, "Accepted image may enter the queue.");

        string track = CreateHardDiskFixture(Path.Combine(root, "set"), "game.img", 64 * 1024);
        File.WriteAllText(Path.Combine(root, "set", "game.cue"), "FILE \"game.img\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n", Encoding.ASCII);
        AssertEqual(MediaInputKind.Other, MediaInputClassifier.Shared.Classify(track).Kind, "A CUE track keeps the pre-existing Other kind.");
        AssertFalse(QueueInputClassifier.Classify(track).IsSupported, "A CUE track never enters the queue on its own.");
    }

    private static void TestHardDiskPlannerRequiresExplicitProfile(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "hdd-planner");
        string plain = CreateHardDiskFixture(Path.Combine(root, "plain"), "drive.img", 64 * 1024);

        ChdWorkflowProfilePlan auto = ChdWorkflowProfilePlanner.PlanCreateFromSource(plain);
        AssertFalse(auto.IsSupported, "Auto mode never converts a hard-disk image.");
        AssertEqual(ChdWorkflowProfilePlanner.HardDiskProfileRequiredMessageKey, auto.FailureMessage, "Auto mode explains which profile to pick.");

        ChdWorkflowProfilePlan explicitPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            plain, IsoCreateCommandOverride.Auto, ChdMediaContainerKind.DirectFile, HardDiskProfileId);
        AssertTrue(explicitPlan.IsSupported, "The hard-disk profile plans createhd.");
        AssertEqual("createhd", explicitPlan.Command, "Hard-disk command.");
        AssertEqual(ChdWorkflowProfileKind.CreateHd, explicitPlan.ProfileKind, "Hard-disk profile kind.");
        AssertEqual(ChdMediaFormatKind.HardDisk, explicitPlan.MediaKind, "Hard-disk media kind.");

        string track = CreateHardDiskFixture(Path.Combine(root, "set"), "game.img", 64 * 1024);
        File.WriteAllText(Path.Combine(root, "set", "game.cue"), "FILE \"game.img\" BINARY\r\n", Encoding.ASCII);
        ChdWorkflowProfilePlan trackPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            track, IsoCreateCommandOverride.Auto, ChdMediaContainerKind.DirectFile, HardDiskProfileId);
        AssertFalse(trackPlan.IsSupported, "The hard-disk profile still refuses a disc track.");
        AssertEqual(ChdWorkflowProfilePlanner.HardDiskDiscEvidenceMessageKey, trackPlan.FailureMessage, "Disc evidence message.");

        string iso = Path.Combine(root, "plain", "disc.iso");
        File.WriteAllBytes(iso, new byte[18 * 2048]);
        ChdWorkflowProfilePlan isoPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            iso, IsoCreateCommandOverride.Auto, ChdMediaContainerKind.DirectFile, HardDiskProfileId);
        AssertFalse(isoPlan.IsSupported, "The hard-disk profile does not accept ISO input.");
    }

    private static void TestHardDiskCommandPolicyKeepsChdmanDefaults()
    {
        ChdPlatformProfile? profile = ChdPlatformProfiles.FindById(HardDiskProfileId);
        AssertTrue(profile is not null, "The hard-disk profile is registered.");
        AssertEqual("createhd", ChdPlatformProfiles.ToCommandName(ChdCommandKind.CreateHd), "CreateHd maps to createhd.");
        AssertEqual("createcd", ChdPlatformProfiles.ToCommandName(ChdCommandKind.CreateCd), "CreateCd mapping is unchanged.");
        AssertEqual("createdvd", ChdPlatformProfiles.ToCommandName(ChdCommandKind.CreateDvd), "CreateDvd mapping is unchanged.");
        AssertTrue(ChdPlatformProfiles.ResolveForInput("drive.img", "PC") is null, "Hard-disk images are never resolved automatically.");
        AssertEqual(profile, ChdPlatformProfiles.ResolveForCommand("createhd", "drive.img", null), "createhd resolves the hard-disk profile.");

        string[] args = ChdmanCommandBuilder.BuildCreateArgs(profile!, "in.img", "out.chd", 2).ToArray();
        AssertEqual("createhd", args[0], "Builder emits createhd.");
        AssertFalse(args.Contains("-hs"), "No hunk size is forced for hard disks.");

        var preparation = new ChdCommandPreparationService();
        AssertTrue(preparation.IsCreateCommand("createhd"), "createhd is a create command.");
        AssertEqual(0, preparation.ResolveHunkSizeSetting(-1, "createhd", "in.img", out _), "CD/DVD hunk presets are not applied to createhd.");
        AssertEqual(string.Empty, preparation.ResolveCompressionSettingWithTruth("-1", "createhd").ResolvedCompression, "createhd keeps chdman's default compression.");
    }

    private static void TestHardDiskRoundTripWithBundledChdman(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "hdd-roundtrip");
        string source = CreateHardDiskFixture(Path.Combine(root, "in"), "drive.img", 1024 * 1024, seed: 11);
        string chdPath = Path.Combine(root, "out", "drive.chd");
        string restoredPath = Path.Combine(root, "restored", "drive.img");
        Directory.CreateDirectory(Path.GetDirectoryName(chdPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(restoredPath)!);

        var service = new ChdConversionService();

        // Unsupported extensions throw from the service (the workflow planner rejects them first).
        string autoMessage = string.Empty;
        try
        {
            service.ConvertToChdAsync(chdmanPath, source, chdPath).GetAwaiter().GetResult();
        }
        catch (NotSupportedException ex)
        {
            autoMessage = ex.Message;
        }

        AssertEqual(ChdWorkflowProfilePlanner.HardDiskProfileRequiredMessageKey, autoMessage, "Auto mode must not run createhd.");
        AssertFalse(File.Exists(chdPath), "Auto mode leaves no output.");

        ChdConversionResult created = service.ConvertToChdAsync(
                chdmanPath, source, chdPath, platformProfileId: HardDiskProfileId)
            .GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, "createhd succeeds: " + created.Message + " " + created.Error);
        AssertTrue(created.CommandLine.Contains("createhd", StringComparison.OrdinalIgnoreCase), "chdman ran createhd.");
        AssertTrue(File.Exists(chdPath), "CHD output exists.");

        ChdConversionResult restored = service.ConvertToChdAsync(
                chdmanPath, chdPath, restoredPath,
                extractionKind: ChdmanExtractionKind.ExtractHd,
                extractionMetadataDecisionConfirmed: true)
            .GetAwaiter().GetResult();
        AssertTrue(restored.IsSuccess, "extracthd succeeds: " + restored.Message + " " + restored.Error);

        AssertEqual(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restoredPath))),
            "Extracted image matches the source SHA-256.");

        AssertFalse(
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(path =>
                path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)),
            "No partial files are left behind.");
    }
}
