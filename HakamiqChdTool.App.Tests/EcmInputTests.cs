using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Reference fixture: the sample image built by BuildEcmSampleImage() encoded by the original
    // "ecm" 1.0.3 encoder (Neill Corlett, built locally only to make this fixture; not part of the
    // app). The encoder reported 3 Mode 1, 2 Mode 2 Form 1 and 2 Form 2 sectors plus 4768 literal
    // bytes, which also confirms that the sectors built by EcmDecoder's EDC/ECC code are valid.
    private const string EcmReferenceSha256 = "A98A0CC83377DD651ACF7E5023B26F9A3A5BF9896EF0CDC541B23D216E388986";
    private const int EcmReferenceBytes = 21168;
    private const string EcmReferenceFixtureGzipBase64 =
        "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";

    private static byte[] LoadEcmReferenceFixture()
    {
        using var input = new MemoryStream(Convert.FromBase64String(EcmReferenceFixtureGzipBase64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    // 3 Mode 1, 2 Mode 2 Form 1, 2 Mode 2 Form 2, one audio-like sector and one Mode 1 sector with a broken EDC.
    private static byte[] BuildEcmSampleImage()
    {
        var sectors = new List<byte[]>();
        int lba = 0;
        for (int i = 0; i < 3; i++)
        {
            sectors.Add(EcmMode1Sector(lba++, i));
        }

        for (int i = 0; i < 2; i++)
        {
            sectors.Add(EcmMode2Sector(lba++, i, form2: false));
        }

        for (int i = 0; i < 2; i++)
        {
            sectors.Add(EcmMode2Sector(lba++, i, form2: true));
        }

        byte[] audio = new byte[2352];
        for (int i = 0; i < audio.Length; i++)
        {
            audio[i] = (byte)(i * 13 + 5);
        }

        sectors.Add(audio);
        lba++;
        byte[] broken = EcmMode1Sector(lba, 9);
        broken[0x810] ^= 0xFF;
        sectors.Add(broken);
        return sectors.SelectMany(sector => sector).ToArray();
    }

    private static void WriteEcmSectorHeader(byte[] sector, int lba, byte mode)
    {
        static byte Bcd(int value) => (byte)((value / 10 << 4) | (value % 10));
        int address = lba + 150;
        sector[0] = 0;
        for (int i = 1; i <= 10; i++)
        {
            sector[i] = 0xFF;
        }

        sector[11] = 0;
        sector[12] = Bcd(address / 4500);
        sector[13] = Bcd(address / 75 % 60);
        sector[14] = Bcd(address % 75);
        sector[15] = mode;
    }

    private static byte[] EcmMode1Sector(int lba, int seed)
    {
        byte[] sector = new byte[2352];
        WriteEcmSectorHeader(sector, lba, 1);
        for (int i = 0x10; i < 0x810; i++)
        {
            sector[i] = (byte)(i * 7 + seed * 31);
        }

        EcmDecoder.RebuildMode1(sector);
        return sector;
    }

    private static byte[] EcmMode2Sector(int lba, int seed, bool form2)
    {
        byte[] sector = new byte[2352];
        WriteEcmSectorHeader(sector, lba, 2);
        sector[0x14] = 1;
        sector[0x15] = (byte)seed;
        sector[0x16] = (byte)(form2 ? 0x20 : 0x08);
        int end = form2 ? 0x92C : 0x818;
        for (int i = 0x18; i < end; i++)
        {
            sector[i] = (byte)(i * 3 + seed * 17);
        }

        if (form2)
        {
            EcmDecoder.RebuildMode2Form2(sector);
        }
        else
        {
            EcmDecoder.RebuildMode2Form1(sector);
        }

        return sector;
    }

    private static string? DecodeEcmBytes(byte[] ecm, out byte[] decoded)
    {
        using var input = new MemoryStream(ecm);
        using var output = new MemoryStream();
        string? failure = EcmDecoder.DecodeAsync(input, output, default).GetAwaiter().GetResult();
        decoded = output.ToArray();
        return failure;
    }

    private static void TestEcmDecoderMatchesReferenceEncoder(string workDirectory)
    {
        byte[] sample = BuildEcmSampleImage();
        AssertEqual(EcmReferenceSha256, Convert.ToHexString(SHA256.HashData(sample)),
            "Sectors rebuilt with EcmDecoder's EDC/ECC match the image the reference encoder accepted.");

        byte[] fixture = LoadEcmReferenceFixture();
        AssertEqual(null, DecodeEcmBytes(fixture, out byte[] decoded), "The reference ECM decodes and its stored EDC matches.");
        AssertEqual(EcmReferenceSha256, Convert.ToHexString(SHA256.HashData(decoded)), "Decoded data matches the original image byte for byte.");

        string path = Path.Combine(workDirectory, "ecm-scan", "sample.bin.ecm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, fixture);
        EcmScanResult scan = EcmDecoder.Scan(path);
        AssertTrue(scan.IsValid && scan.OutputBytes == EcmReferenceBytes, "Scan reports the exact decoded size without decoding.");

        string decodedPath = Path.Combine(workDirectory, "ecm-scan", "sample.bin");
        AssertEqual(null, EcmDecoder.DecodeToFileAsync(path, decodedPath, default).GetAwaiter().GetResult(), "File decoding succeeds.");
        AssertEqual(EcmDecoder.InvalidMessageKey, EcmDecoder.DecodeToFileAsync(path, decodedPath, default).GetAwaiter().GetResult(), "An existing output is never overwritten.");
        AssertEqual(EcmReferenceSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(decodedPath))), "The existing output is left unchanged.");
    }

    private static void TestEcmDecoderRejectsDamage(string workDirectory)
    {
        byte[] fixture = LoadEcmReferenceFixture();

        byte[] flipped = (byte[])fixture.Clone();
        flipped[fixture.Length / 2] ^= 0x01;
        AssertEqual(EcmDecoder.ChecksumMismatchMessageKey, DecodeEcmBytes(flipped, out _), "A changed data byte fails the stored EDC check.");

        byte[] truncated = fixture[..(fixture.Length - 100)];
        AssertEqual(EcmDecoder.InvalidMessageKey, DecodeEcmBytes(truncated, out _), "A truncated file is refused.");

        byte[] magic = (byte[])fixture.Clone();
        magic[0] = (byte)'X';
        AssertEqual(EcmDecoder.InvalidMessageKey, DecodeEcmBytes(magic, out _), "A wrong signature is refused.");

        byte[] trailing = [.. fixture, 0x00];
        AssertEqual(EcmDecoder.InvalidMessageKey, DecodeEcmBytes(trailing, out _), "Data after the stored EDC is refused.");

        byte[] noSectors = [(byte)'E', (byte)'C', (byte)'M', 0, 0xFC, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 0, 0];
        AssertEqual(EcmDecoder.InvalidMessageKey, DecodeEcmBytes(noSectors, out _), "An empty image is refused.");

        string root = Path.Combine(workDirectory, "ecm-damage");
        Directory.CreateDirectory(root);
        string truncatedPath = Path.Combine(root, "short.bin.ecm");
        File.WriteAllBytes(truncatedPath, truncated);
        AssertFalse(EcmDecoder.Scan(truncatedPath).IsValid, "Scan refuses a truncated file.");

        string damagedPath = Path.Combine(root, "damaged.bin.ecm");
        File.WriteAllBytes(damagedPath, flipped);
        string output = Path.Combine(root, "damaged.bin");
        AssertEqual(EcmDecoder.ChecksumMismatchMessageKey, EcmDecoder.DecodeToFileAsync(damagedPath, output, default).GetAwaiter().GetResult(), "File decoding reports the checksum failure.");
        AssertFalse(File.Exists(output), "A failed decode leaves no output file.");
    }

    private static string WriteEcmCueSet(string directory, string cueText, params (string Name, byte[] Data)[] files)
    {
        Directory.CreateDirectory(directory);
        string cue = Path.Combine(directory, "game.cue");
        File.WriteAllText(cue, cueText, Encoding.ASCII);
        foreach ((string name, byte[] data) in files)
        {
            File.WriteAllBytes(Path.Combine(directory, name), data);
        }

        return cue;
    }

    private static void TestEcmIntakeAndPlanning(string workDirectory)
    {
        byte[] ecm = LoadEcmReferenceFixture();
        string root = Path.Combine(workDirectory, "ecm-plan");

        string single = WriteEcmCueSet(Path.Combine(root, "single"),
            "FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n",
            ("game.bin.ecm", ecm));
        string singleEcm = Path.Combine(root, "single", "game.bin.ecm");
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(singleEcm).Role, "The .bin.ecm enters the queue.");
        AssertEqual(QueueInputRole.DependentTrackFile, QueueInputClassifier.Classify(single).Role, "Its CUE is covered by the .ecm entry, so it is not queued twice.");
        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(singleEcm);
        AssertTrue(plan.IsSupported, "An ECM set plans a conversion: " + plan.FailureMessage);
        AssertEqual("createcd", plan.Command, "ECM uses createcd.");
        AssertEqual(ChdMediaFormatKind.Ecm, plan.MediaKind, "ECM media kind.");
        AssertEqual(ChdPlatformProfiles.EcmCd, plan.PlatformProfile, "ECM profile.");

        WriteEcmCueSet(Path.Combine(root, "multi"),
            "FILE \"game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
            "FILE \"game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n" +
            "FILE \"game (Track 3).bin\" BINARY\r\n  TRACK 03 AUDIO\r\n    INDEX 01 00:00:00\r\n",
            ("game (Track 1).bin.ecm", ecm), ("game (Track 2).bin", new byte[2352 * 4]), ("game (Track 3).bin.ecm", ecm));
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(Path.Combine(root, "multi", "game (Track 1).bin.ecm")).Role, "The first compressed track represents the set.");
        AssertEqual(QueueInputRole.DependentTrackFile, QueueInputClassifier.Classify(Path.Combine(root, "multi", "game (Track 3).bin.ecm")).Role, "Later compressed tracks are covered by the set.");

        string missing = WriteEcmCueSet(Path.Combine(root, "missing"),
            "FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
            "FILE \"other.bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n",
            ("game.bin.ecm", ecm));
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(missing).Role, "A CUE with a track missing entirely keeps its normal handling.");
        ChdWorkflowProfilePlan incomplete = ChdWorkflowProfilePlanner.PlanCreateFromSource(Path.Combine(root, "missing", "game.bin.ecm"));
        AssertEqual(EcmSetPreparer.CueIncompleteMessageKey, incomplete.FailureMessage, "An ECM whose CUE names a missing track is refused with the reason.");

        string plainSet = WriteEcmCueSet(Path.Combine(root, "plain"),
            "FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n",
            ("game.bin", new byte[2352 * 4]), ("game.bin.ecm", ecm));
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(plainSet).Role, "A CUE whose tracks all exist is unchanged.");
        AssertEqual(QueueInputRole.DependentTrackFile, QueueInputClassifier.Classify(Path.Combine(root, "plain", "game.bin.ecm")).Role, "A leftover .ecm next to the decoded track is not queued.");

        string iso = Path.Combine(root, "iso", "game.iso.ecm");
        Directory.CreateDirectory(Path.GetDirectoryName(iso)!);
        File.WriteAllBytes(iso, ecm);
        ChdWorkflowProfilePlan isoPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(iso);
        AssertEqual(EcmSetPreparer.UnsupportedNameMessageKey, isoPlan.FailureMessage, "Only .bin.ecm and .img.ecm are accepted.");

        string standalone = Path.Combine(root, "standalone", "disc.img.ecm");
        Directory.CreateDirectory(Path.GetDirectoryName(standalone)!);
        File.WriteAllBytes(standalone, ecm);
        AssertTrue(ChdWorkflowProfilePlanner.PlanCreateFromSource(standalone).IsSupported, "A standalone .img.ecm with whole sectors plans a conversion.");
    }

    private static void TestEcmRoundTripsWithBundledChdman(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        byte[] ecm = LoadEcmReferenceFixture();
        byte[] sample = BuildEcmSampleImage();
        byte[] audio = new byte[2352 * 8];
        new Random(71).NextBytes(audio);
        string root = Path.Combine(workDirectory, "ecm-roundtrip");

        // CUE set: compressed data track plus a plain audio track.
        string setDir = Path.Combine(root, "set", "in");
        WriteEcmCueSet(setDir,
            "FILE \"game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
            "FILE \"game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n",
            ("game (Track 1).bin.ecm", ecm), ("game (Track 2).bin", audio));
        string setEcm = Path.Combine(setDir, "game (Track 1).bin.ecm");
        AssertRoundTrip(chdmanPath, setEcm, Path.Combine(root, "set", "out"), [.. sample, .. audio], "ECM CUE set");
        AssertTrue(!File.Exists(Path.Combine(setDir, "game (Track 1).bin")), "The decoded track is not written to the source folder.");

        // No CUE: a single data track is assumed because the image starts with a Mode 1 sector.
        string loneDir = Path.Combine(root, "lone", "in");
        Directory.CreateDirectory(loneDir);
        string lone = Path.Combine(loneDir, "disc.bin.ecm");
        File.WriteAllBytes(lone, ecm);
        AssertRoundTrip(chdmanPath, lone, Path.Combine(root, "lone", "out"), sample, "Standalone ECM");

        // A damaged file stops before chdman runs.
        string badDir = Path.Combine(root, "bad", "in");
        Directory.CreateDirectory(badDir);
        byte[] damaged = (byte[])ecm.Clone();
        damaged[ecm.Length / 2] ^= 0x01;
        string bad = Path.Combine(badDir, "disc.bin.ecm");
        File.WriteAllBytes(bad, damaged);
        string badOut = Path.Combine(root, "bad", "out");
        Directory.CreateDirectory(badOut);
        ChdConversionResult refused = new ChdConversionService().ConvertToChdAsync(chdmanPath, bad, Path.Combine(badOut, "disc.chd")).GetAwaiter().GetResult();
        AssertFalse(refused.IsSuccess, "A damaged ECM is not converted.");
        AssertFalse(File.Exists(Path.Combine(badOut, "disc.chd")), "No CHD is written for a damaged ECM.");
    }

    private static void AssertRoundTrip(string chdmanPath, string input, string outDir, byte[] expected, string label)
    {
        Directory.CreateDirectory(outDir);
        string chd = Path.Combine(outDir, "game.chd");
        ChdConversionResult created = new ChdConversionService().ConvertToChdAsync(chdmanPath, input, chd).GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, label + " converts to CHD: " + created.Message + " " + created.Error);
        AssertTrue(created.CommandLine.Contains("createcd", StringComparison.OrdinalIgnoreCase), label + ": chdman ran createcd.");

        string cue = Path.Combine(outDir, "restored.cue");
        string bin = Path.Combine(outDir, "restored.bin");
        RunChdman(chdmanPath, "extractcd", "-i", chd, "-o", cue, "-ob", bin);
        AssertEqual(Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bin))),
            label + ": extracted tracks match the decoded source byte for byte.");
    }
}
