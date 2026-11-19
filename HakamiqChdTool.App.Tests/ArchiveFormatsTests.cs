using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static string ZipFolder(string sourceDirectory, string zipPath, string entryPrefix = "Game (USA)/")
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            zip.CreateEntryFromFile(file, entryPrefix + Path.GetFileName(file));
        }

        return zipPath;
    }

    private static ArchiveExtractionResult ExtractArchiveLeader(string zip, string root, string name)
    {
        string destination = Path.Combine(root, name + "-extracted");
        Directory.CreateDirectory(destination);
        return new SevenZipArchiveExtractionService().ExtractFirstConvertibleDiscImageAsync(zip, destination).GetAwaiter().GetResult();
    }

    // CloneCD, Alcohol 120% and ECM sets inside a ZIP: the right files are extracted, the extracted
    // entry is the one the queue converts, and each converts to a CHD whose tracks match the source.
    private static void TestCcdMdsEcmInsideArchivesConvert(AppReflection app, string workDirectory)
    {
        AssertTrue(new SevenZipArchiveExtractionService().IsAvailable, "The bundled 7-Zip is available.");
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "archive-formats");

        // CloneCD: the .ccd and its .img are extracted; the .sub is not needed.
        string ccdSource = Path.Combine(root, "ccd-src");
        WriteCcdSet(ccdSource, "game", TwoTrackCcd, 64, seed: 91);
        ArchiveExtractionResult ccd = ExtractArchiveLeader(ZipFolder(ccdSource, Path.Combine(root, "ccd.zip")), root, "ccd");
        AssertTrue(ccd.IsSuccess && ccd.ExtractedPath.EndsWith(".ccd", StringComparison.OrdinalIgnoreCase), "The CloneCD control file is the archive's disc image: " + ccd.Message);
        AssertTrue(File.Exists(Path.ChangeExtension(ccd.ExtractedPath, ".img")), "Its .img is extracted next to it.");
        AssertFalse(File.Exists(Path.ChangeExtension(ccd.ExtractedPath, ".sub")), "The .sub is not extracted.");
        AssertRoundTrip(chdmanPath, ccd.ExtractedPath, Path.Combine(root, "ccd-out"), File.ReadAllBytes(Path.Combine(ccdSource, "game.img")), "CloneCD in ZIP");

        // Alcohol 120%: the .mds and its .mdf are extracted.
        string mdsSource = Path.Combine(root, "mds-src");
        WriteMdsSet(mdsSource, "game", TwoTrackMds(), seed: 92);
        ArchiveExtractionResult mds = ExtractArchiveLeader(ZipFolder(mdsSource, Path.Combine(root, "mds.zip")), root, "mds");
        AssertTrue(mds.IsSuccess && mds.ExtractedPath.EndsWith(".mds", StringComparison.OrdinalIgnoreCase), "The Alcohol descriptor is the archive's disc image: " + mds.Message);
        AssertTrue(File.Exists(Path.ChangeExtension(mds.ExtractedPath, ".mdf")), "Its .mdf is extracted next to it.");
        AssertRoundTrip(chdmanPath, mds.ExtractedPath, Path.Combine(root, "mds-out"), File.ReadAllBytes(Path.Combine(mdsSource, "game.mdf")), "Alcohol MDS in ZIP");

        // ECM CUE set: the CUE names "game (Track 1).bin", present only as .bin.ecm, plus a plain audio track.
        byte[] audio = new byte[2352 * 6];
        new Random(93).NextBytes(audio);
        string ecmSource = Path.Combine(root, "ecm-src");
        WriteEcmCueSet(ecmSource,
            "FILE \"game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
            "FILE \"game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n",
            ("game (Track 1).bin.ecm", LoadEcmReferenceFixture()), ("game (Track 2).bin", audio));
        ArchiveExtractionResult ecm = ExtractArchiveLeader(ZipFolder(ecmSource, Path.Combine(root, "ecm.zip")), root, "ecm");
        AssertTrue(ecm.IsSuccess, "An ECM CUE set is extracted: " + ecm.Message);
        AssertTrue(ecm.ExtractedPath.EndsWith("game (Track 1).bin.ecm", StringComparison.OrdinalIgnoreCase), "The set is converted through its first .ecm: " + ecm.ExtractedPath);
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(ecm.ExtractedPath).Role, "The extracted .ecm is convertible.");
        AssertTrue(ecm.ExtractedFiles.Any(path => path.EndsWith("game.cue", StringComparison.OrdinalIgnoreCase)) && ecm.ExtractedFiles.Any(path => path.EndsWith("game (Track 2).bin", StringComparison.OrdinalIgnoreCase)),
            "The CUE and the plain track are extracted too.");
        ChdWorkflowProfilePlan ecmPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(ecm.ExtractedPath, IsoCreateCommandOverride.Auto, ChdMediaContainerKind.Archive);
        AssertTrue(ecmPlan.IsSupported && ecmPlan.MediaKind == ChdMediaFormatKind.Ecm, "The extracted ECM set plans createcd: " + ecmPlan.FailureMessage);
        AssertRoundTrip(chdmanPath, ecm.ExtractedPath, Path.Combine(root, "ecm-out"), [.. BuildEcmSampleImage(), .. audio], "ECM CUE set in ZIP");

        // Standalone ECM without a CUE.
        string loneSource = Path.Combine(root, "lone-src");
        Directory.CreateDirectory(loneSource);
        File.WriteAllBytes(Path.Combine(loneSource, "disc.bin.ecm"), LoadEcmReferenceFixture());
        ArchiveExtractionResult lone = ExtractArchiveLeader(ZipFolder(loneSource, Path.Combine(root, "lone.zip")), root, "lone");
        AssertTrue(lone.IsSuccess && lone.ExtractedPath.EndsWith("disc.bin.ecm", StringComparison.OrdinalIgnoreCase), "A standalone .bin.ecm is the archive's disc image: " + lone.Message);
        AssertRoundTrip(chdmanPath, lone.ExtractedPath, Path.Combine(root, "lone-out"), BuildEcmSampleImage(), "Standalone ECM in ZIP");

        // A CUE track missing entirely is still refused, and the .ecm substitute is archive-only.
        string missingSource = Path.Combine(root, "missing-src");
        WriteEcmCueSet(missingSource, "FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n", ("readme.txt", [1, 2, 3]));
        AssertFalse(ExtractArchiveLeader(ZipFolder(missingSource, Path.Combine(root, "missing.zip")), root, "missing").IsSuccess, "A CUE whose track is missing is refused.");
        AssertFalse(ArchiveCandidateDiscovery.TryValidateDescriptorDependencies("game.cue", "FILE \"game.bin\" BINARY\r\n", ["game.bin.ecm"], out _),
            "Outside archive extraction a .bin.ecm does not stand in for a missing .bin.");
    }
}
