using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private const string TwoTrackCcd =
        "[CloneCD]\r\nVersion=3\r\n[Disc]\r\nTocEntries=5\r\nSessions=1\r\nDataTracksScrambled=0\r\nCDTextLength=0\r\n" +
        "[Session 1]\r\nPreGapMode=1\r\nPreGapSubC=0\r\n" +
        "[TRACK 1]\r\nMODE=1\r\nINDEX 1=0\r\n" +
        "[TRACK 2]\r\nMODE=0\r\nINDEX 1=32\r\n";

    private static string WriteCcdSet(string directory, string name, string ccdText, int sectors, int seed = 21)
    {
        Directory.CreateDirectory(directory);
        string ccd = Path.Combine(directory, name + ".ccd");
        File.WriteAllText(ccd, ccdText, Encoding.ASCII);
        byte[] image = new byte[sectors * CloneCdCueBuilder.RawSectorBytes];
        new Random(seed).NextBytes(image);
        File.WriteAllBytes(Path.Combine(directory, name + ".img"), image);
        File.WriteAllBytes(Path.Combine(directory, name + ".sub"), new byte[sectors * 96]);
        return ccd;
    }

    private static void TestCcdParserBuildsExactCue(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "ccd-parse");
        string ccd = WriteCcdSet(
            root,
            "disc",
            TwoTrackCcd.Replace("[TRACK 2]\r\nMODE=0\r\nINDEX 1=32\r\n", "[TRACK 2]\r\nMODE=0\r\nINDEX 0=30\r\nINDEX 1=105\r\n", StringComparison.Ordinal),
            200);

        CloneCdParseResult result = CloneCdCueBuilder.Parse(ccd);
        AssertTrue(result.IsValid, "Valid CCD parses: " + result.MessageKey);

        AssertEqual(Path.Combine(Path.GetFullPath(root), "disc.img"), result.Layout!.ImagePath, "Image path is the same-name .img.");
        string expected =
            "FILE \"tmp.img\" BINARY\r\n" +
            "  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n" +
            "  TRACK 02 AUDIO\r\n    INDEX 00 00:00:30\r\n    INDEX 01 00:01:30\r\n";
        AssertEqual(expected, CloneCdCueBuilder.BuildCue(result.Layout!, "tmp.img"), "Generated CUE text.");
        AssertThrows<ArgumentException>(() => CloneCdCueBuilder.BuildCue(result.Layout!, @"..\escape.img"), "The CUE reference must be a plain file name.");
        AssertEqual("01:00:00", CloneCdCueBuilder.ToMsf(4500), "LBA 4500 is one minute.");
    }

    private static void TestCcdParserFailsClosed(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "ccd-reject");

        string multi = WriteCcdSet(Path.Combine(root, "multi"), "d", TwoTrackCcd.Replace("Sessions=1", "Sessions=2", StringComparison.Ordinal), 64);
        AssertEqual(CloneCdCueBuilder.MultiSessionMessageKey, CloneCdCueBuilder.Parse(multi).MessageKey, "Multi-session is refused.");

        string scrambled = WriteCcdSet(Path.Combine(root, "scrambled"), "d", TwoTrackCcd.Replace("DataTracksScrambled=0", "DataTracksScrambled=1", StringComparison.Ordinal), 64);
        AssertEqual(CloneCdCueBuilder.ScrambledMessageKey, CloneCdCueBuilder.Parse(scrambled).MessageKey, "Scrambled data is refused.");

        string missing = WriteCcdSet(Path.Combine(root, "missing"), "d", TwoTrackCcd, 64);
        File.Delete(Path.ChangeExtension(missing, ".img"));
        AssertEqual(CloneCdCueBuilder.ImageMissingMessageKey, CloneCdCueBuilder.Parse(missing).MessageKey, "Missing .img is refused.");

        string misaligned = WriteCcdSet(Path.Combine(root, "misaligned"), "d", TwoTrackCcd, 64);
        File.AppendAllText(Path.ChangeExtension(misaligned, ".img"), "x");
        AssertEqual(CloneCdCueBuilder.ImageNotAlignedMessageKey, CloneCdCueBuilder.Parse(misaligned).MessageKey, "Misaligned .img is refused.");

        string beyondEnd = WriteCcdSet(Path.Combine(root, "beyond"), "d", TwoTrackCcd.Replace("INDEX 1=32", "INDEX 1=64", StringComparison.Ordinal), 64);
        AssertFalse(CloneCdCueBuilder.Parse(beyondEnd).IsValid, "An index past the end of the image is refused.");

        string gap = WriteCcdSet(Path.Combine(root, "gap"), "d", TwoTrackCcd.Replace("[TRACK 2]", "[TRACK 3]", StringComparison.Ordinal), 64);
        AssertFalse(CloneCdCueBuilder.Parse(gap).IsValid, "Non-consecutive track numbers are refused.");

        string backwards = WriteCcdSet(Path.Combine(root, "backwards"), "d", TwoTrackCcd.Replace("INDEX 1=32", "INDEX 1=0", StringComparison.Ordinal), 64);
        AssertFalse(CloneCdCueBuilder.Parse(backwards).IsValid, "Tracks that do not move forward are refused.");
    }

    private static void TestCcdStagingNeverTouchesTheSource(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "ccd-stage");
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "readonly.img");
        File.WriteAllBytes(source, new byte[2352 * 4]);
        File.SetAttributes(source, FileAttributes.ReadOnly);
        string staged = Path.Combine(root, "staged.img");

        try
        {
            AssertTrue(CloneCdImageStager.TryStageAsync(source, staged, default).GetAwaiter().GetResult(), "A read-only image is staged.");
            File.WriteAllBytes(staged, [1, 2, 3]);
            AssertEqual(2352L * 4, new FileInfo(source).Length, "A read-only source is copied, never hard-linked.");
            AssertTrue(new FileInfo(source).IsReadOnly, "The source keeps its read-only attribute.");
            AssertFalse(CloneCdImageStager.TryStageAsync(source, staged, default).GetAwaiter().GetResult(), "An existing destination is never overwritten.");
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
        }
    }

    private static void TestCcdIntakeAndPlanning(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "ccd-plan");
        string ccd = WriteCcdSet(root, "game", TwoTrackCcd, 64);

        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(ccd).Role, "A .ccd enters the queue.");
        AssertFalse(QueueInputClassifier.Classify(Path.ChangeExtension(ccd, ".img")).IsSupported, "Its .img track stays out of the queue.");
        AssertFalse(QueueInputClassifier.Classify(Path.ChangeExtension(ccd, ".sub")).IsSupported, "Its .sub stays out of the queue.");

        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(ccd);
        AssertTrue(plan.IsSupported, "CCD plans createcd.");
        AssertEqual("createcd", plan.Command, "CCD command.");
        AssertEqual(ChdMediaFormatKind.Ccd, plan.MediaKind, "CCD media kind.");
        AssertEqual(ChdPlatformProfiles.CloneCd, plan.PlatformProfile, "CCD profile.");
    }

    private static void TestCcdRoundTripWithBundledChdman(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "ccd-roundtrip");
        string ccd = WriteCcdSet(Path.Combine(root, "in"), "game", TwoTrackCcd, 64, seed: 33);
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        string chd = Path.Combine(outDir, "game.chd");

        ChdConversionResult created = new ChdConversionService().ConvertToChdAsync(chdmanPath, ccd, chd).GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, "CCD converts to CHD: " + created.Message + " " + created.Error);
        AssertTrue(created.CommandLine.Contains("createcd", StringComparison.OrdinalIgnoreCase), "chdman ran createcd.");

        string extractedCue = Path.Combine(outDir, "restored.cue");
        string extractedBin = Path.Combine(outDir, "restored.bin");
        var start = new ProcessStartInfo(chdmanPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { "extractcd", "-i", chd, "-o", extractedCue, "-ob", extractedBin })
        {
            start.ArgumentList.Add(argument);
        }

        using (Process process = Process.Start(start)!)
        {
            process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            AssertEqual(0, process.ExitCode, "chdman extractcd failed: " + stderr);
        }

        AssertEqual(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(ccd, ".img")))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extractedBin))),
            "Extracted tracks match the CloneCD .img byte for byte.");

        string cueText = File.ReadAllText(extractedCue);
        AssertTrue(cueText.Contains("MODE1/2352", StringComparison.Ordinal) && cueText.Contains("AUDIO", StringComparison.Ordinal),
            "Extracted CUE keeps the data and audio track types.");
        AssertTrue(File.Exists(ccd) && File.Exists(Path.ChangeExtension(ccd, ".img")), "The CloneCD source is preserved.");
    }
}
