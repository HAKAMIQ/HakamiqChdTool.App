using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed record MdsTestTrack(byte Mode, int Lba, int Pregap, int Length, long? OffsetOverride = null);

    private sealed class MdsTestImage
    {
        public ushort Medium { get; init; }
        public byte Version { get; init; } = 1;
        public ushort Sessions { get; init; } = 1;
        public byte Subchannels { get; init; }
        public int SectorSize { get; init; } = 2352;
        public string Filename { get; init; } = "*.mdf";
        public bool OffsetsAtIndex0 { get; init; }
        public IReadOnlyList<MdsTestTrack> Tracks { get; init; } = [];
    }

    // Writes an Alcohol 120% descriptor following the public MDS v1 layout: 58h header,
    // 18h session block, 50h data blocks (A0..A2 lead-in first), 8-byte index blocks,
    // a 10h filename block and the "*.mdf" string.
    private static byte[] BuildMdsBytes(MdsTestImage spec)
    {
        const int Header = 0x58;
        const int Session = 0x18;
        const int Block = 0x50;
        int leadIn = 3;
        int blocks = leadIn + spec.Tracks.Count;
        int dataOffset = Header + Session;
        int indexOffset = dataOffset + blocks * Block;
        int filenameBlockOffset = indexOffset + blocks * 8;
        int filenameOffset = filenameBlockOffset + 0x10;
        byte[] name = Encoding.ASCII.GetBytes(spec.Filename + "\0");
        byte[] mds = new byte[filenameOffset + name.Length];

        Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR").CopyTo(mds, 0);
        mds[0x10] = spec.Version;
        mds[0x11] = 5;
        BinaryPrimitives.WriteUInt16LittleEndian(mds.AsSpan(0x12), spec.Medium);
        BinaryPrimitives.WriteUInt16LittleEndian(mds.AsSpan(0x14), spec.Sessions);
        mds[0x16] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(mds.AsSpan(0x50), Header);

        Span<byte> session = mds.AsSpan(Header, Session);
        BinaryPrimitives.WriteInt32LittleEndian(session, -150);
        int last = spec.Tracks.Count == 0 ? 0 : spec.Tracks[^1].Lba + spec.Tracks[^1].Length;
        BinaryPrimitives.WriteInt32LittleEndian(session[4..], last);
        BinaryPrimitives.WriteUInt16LittleEndian(session[8..], 1);
        session[0x0A] = (byte)blocks;
        session[0x0B] = (byte)leadIn;
        BinaryPrimitives.WriteUInt16LittleEndian(session[0x0C..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(session[0x0E..], (ushort)spec.Tracks.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(session[0x14..], (uint)dataOffset);

        for (int b = 0; b < leadIn; b++)
        {
            mds[dataOffset + b * Block + 4] = (byte)(0xA0 + b);
        }

        for (int t = 0; t < spec.Tracks.Count; t++)
        {
            MdsTestTrack track = spec.Tracks[t];
            int blockIndex = leadIn + t;
            Span<byte> data = mds.AsSpan(dataOffset + blockIndex * Block, Block);
            data[0] = track.Mode;
            data[1] = spec.Subchannels;
            data[2] = (byte)(track.Mode == 0xA9 ? 0x10 : 0x14);
            data[4] = (byte)(t + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(data[0x0C..], (uint)(indexOffset + blockIndex * 8));
            BinaryPrimitives.WriteUInt16LittleEndian(data[0x10..], (ushort)spec.SectorSize);
            data[0x12] = 2;
            BinaryPrimitives.WriteInt32LittleEndian(data[0x24..], track.Lba);
            long startSector = spec.OffsetsAtIndex0 && t > 0 ? track.Lba - track.Pregap : track.Lba;
            BinaryPrimitives.WriteUInt64LittleEndian(data[0x28..], (ulong)(track.OffsetOverride ?? startSector * spec.SectorSize));
            BinaryPrimitives.WriteUInt32LittleEndian(data[0x30..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(data[0x34..], (uint)filenameBlockOffset);

            BinaryPrimitives.WriteUInt32LittleEndian(mds.AsSpan(indexOffset + blockIndex * 8), (uint)track.Pregap);
            BinaryPrimitives.WriteUInt32LittleEndian(mds.AsSpan(indexOffset + blockIndex * 8 + 4), (uint)track.Length);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(mds.AsSpan(filenameBlockOffset), (uint)filenameOffset);
        name.CopyTo(mds, filenameOffset);
        return mds;
    }

    private static string WriteMdsSet(string directory, string name, MdsTestImage spec, long? imageSectors = null, int seed = 41)
    {
        Directory.CreateDirectory(directory);
        string mds = Path.Combine(directory, name + ".mds");
        File.WriteAllBytes(mds, BuildMdsBytes(spec));
        long sectors = imageSectors ?? (spec.Tracks.Count == 0 ? 0 : spec.Tracks[^1].Lba + spec.Tracks[^1].Length);
        byte[] image = new byte[sectors * spec.SectorSize];
        new Random(seed).NextBytes(image);
        File.WriteAllBytes(Path.Combine(directory, name + ".mdf"), image);
        return mds;
    }

    // Track 1 Mode 1 (sectors 0..31), track 2 audio with a 30-sector pregap (index 0 at 32, index 1 at 62).
    private static MdsTestImage TwoTrackMds(int sectorSize = 2352, byte subchannels = 0, bool offsetsAtIndex0 = false) => new()
    {
        SectorSize = sectorSize,
        Subchannels = subchannels,
        OffsetsAtIndex0 = offsetsAtIndex0,
        Tracks = [new MdsTestTrack(0xAA, 0, 150, 32), new MdsTestTrack(0xA9, 62, 30, 40)]
    };

    private static void TestMdsParserBuildsExactCue(string workDirectory)
    {
        const string Expected =
            "FILE \"tmp.img\" BINARY\r\n" +
            "  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n" +
            "  TRACK 02 AUDIO\r\n    INDEX 00 00:00:32\r\n    INDEX 01 00:00:62\r\n";
        string root = Path.Combine(workDirectory, "mds-parse");

        foreach (bool atIndex0 in new[] { false, true })
        {
            string mds = WriteMdsSet(Path.Combine(root, atIndex0 ? "at-index0" : "at-index1"), "disc", TwoTrackMds(offsetsAtIndex0: atIndex0));
            AlcoholMdsParseResult result = AlcoholMdsCueBuilder.Parse(mds);
            AssertTrue(result.IsValid, "Valid MDS parses (track offsets at " + (atIndex0 ? "index 0" : "index 1") + "): " + result.MessageKey);
            AssertEqual(Path.ChangeExtension(Path.GetFullPath(mds), ".mdf"), result.Layout!.ImagePath, "\"*.mdf\" is the same-name .mdf.");
            AssertEqual(AlcoholMdsMedium.Cd, result.Layout.Medium, "Medium type 0 is a CD.");
            AssertEqual(102L, result.Layout.SectorCount, "Sector count covers every track.");
            AssertEqual(Expected, AlcoholMdsCueBuilder.BuildCue(result.Layout, "tmp.img"), "Generated CUE text.");
        }

        string named = WriteMdsSet(Path.Combine(root, "named"), "disc", new MdsTestImage
        {
            Filename = "disc.mdf",
            Tracks = [new MdsTestTrack(0xAA, 0, 150, 16)]
        });
        AssertTrue(AlcoholMdsCueBuilder.Parse(named).IsValid, "A plain same-folder .mdf name is accepted.");

        string cooked = WriteMdsSet(Path.Combine(root, "cooked"), "disc", new MdsTestImage
        {
            SectorSize = 2048,
            Tracks = [new MdsTestTrack(0xAA, 0, 150, 16)]
        });
        AlcoholMdsParseResult cookedResult = AlcoholMdsCueBuilder.Parse(cooked);
        AssertTrue(cookedResult.IsValid && cookedResult.Layout!.SectorFormat == AlcoholMdsSectorFormat.Cooked2048, "A 2048-byte Mode 1 image parses.");
        AssertTrue(AlcoholMdsCueBuilder.BuildCue(cookedResult.Layout!, "tmp.img").Contains("TRACK 01 MODE1/2048", StringComparison.Ordinal), "2048-byte data uses MODE1/2048.");

        AssertThrows<ArgumentException>(() => AlcoholMdsCueBuilder.BuildCue(cookedResult.Layout!, @"..\escape.img"), "The CUE reference must be a plain file name.");
    }

    private static void TestMdsParserFailsClosed(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "mds-reject");

        string Write(string name, MdsTestImage spec, long? sectors = null) =>
            WriteMdsSet(Path.Combine(root, name), "d", spec, sectors);

        string signature = Write("signature", TwoTrackMds());
        byte[] bytes = File.ReadAllBytes(signature);
        bytes[0] = (byte)'X';
        File.WriteAllBytes(signature, bytes);
        AssertEqual(AlcoholMdsCueBuilder.InvalidMessageKey, AlcoholMdsCueBuilder.Parse(signature).MessageKey, "A wrong signature is refused.");

        string version2 = Write("version2", new MdsTestImage { Version = 2, Tracks = TwoTrackMds().Tracks });
        AssertEqual(AlcoholMdsCueBuilder.UnsupportedVersionMessageKey, AlcoholMdsCueBuilder.Parse(version2).MessageKey, "MDS version 2 is refused.");

        string multi = Write("multi", new MdsTestImage { Sessions = 2, Tracks = TwoTrackMds().Tracks });
        AssertEqual(AlcoholMdsCueBuilder.MultiSessionMessageKey, AlcoholMdsCueBuilder.Parse(multi).MessageKey, "Multi-session is refused.");

        string missing = Write("missing", TwoTrackMds());
        File.Delete(Path.ChangeExtension(missing, ".mdf"));
        AssertEqual(AlcoholMdsCueBuilder.ImageMissingMessageKey, AlcoholMdsCueBuilder.Parse(missing).MessageKey, "A missing .mdf is refused.");

        string shortImage = Write("short", TwoTrackMds(), sectors: 101);
        AssertEqual(AlcoholMdsCueBuilder.ImageSizeMismatchMessageKey, AlcoholMdsCueBuilder.Parse(shortImage).MessageKey, "A truncated or split .mdf is refused.");

        string longImage = Write("long", TwoTrackMds(), sectors: 103);
        AssertEqual(AlcoholMdsCueBuilder.ImageSizeMismatchMessageKey, AlcoholMdsCueBuilder.Parse(longImage).MessageKey, "Extra data after the last track is refused.");

        string gap = Write("gap", new MdsTestImage { Tracks = [new MdsTestTrack(0xAA, 0, 150, 32), new MdsTestTrack(0xA9, 70, 30, 40)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(gap).IsValid, "A gap between tracks is refused.");

        string offset = Write("offset", new MdsTestImage { Tracks = [new MdsTestTrack(0xAA, 0, 150, 32), new MdsTestTrack(0xA9, 62, 30, 40, OffsetOverride: 12345)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(offset).IsValid, "A track offset that does not match its LBA is refused.");

        string lba = Write("lba", new MdsTestImage { Tracks = [new MdsTestTrack(0xAA, 150, 150, 32)] }, sectors: 182);
        AssertFalse(AlcoholMdsCueBuilder.Parse(lba).IsValid, "Track 1 must start at LBA 0.");

        string mode = Write("mode", new MdsTestImage { Tracks = [new MdsTestTrack(0xA2, 0, 150, 32)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(mode).IsValid, "An unknown track mode is refused.");

        string cookedAudio = Write("cooked-audio", new MdsTestImage { SectorSize = 2048, Tracks = [new MdsTestTrack(0xA9, 0, 150, 16)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(cookedAudio).IsValid, "Audio in 2048-byte sectors is refused.");

        string oddSize = Write("odd-size", new MdsTestImage { SectorSize = 2336, Tracks = [new MdsTestTrack(0xAB, 0, 150, 16)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(oddSize).IsValid, "Unsupported sector sizes are refused.");

        string traversal = Write("traversal", new MdsTestImage { Filename = @"..\other.mdf", Tracks = [new MdsTestTrack(0xAA, 0, 150, 16)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(traversal).IsValid, "An .mdf outside the .mds folder is refused.");

        string otherExtension = Write("other-ext", new MdsTestImage { Filename = "d.bin", Tracks = [new MdsTestTrack(0xAA, 0, 150, 16)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(otherExtension).IsValid, "Only an .mdf data file is accepted.");

        string truncatedDescriptor = Path.Combine(root, "truncated", "d.mds");
        Directory.CreateDirectory(Path.GetDirectoryName(truncatedDescriptor)!);
        File.WriteAllBytes(truncatedDescriptor, BuildMdsBytes(TwoTrackMds()).AsSpan(0, 0x90).ToArray());
        AssertFalse(AlcoholMdsCueBuilder.Parse(truncatedDescriptor).IsValid, "A truncated descriptor is refused without reading past its end.");

        string dvdTwoTracks = Write("dvd-two", new MdsTestImage { Medium = 0x10, SectorSize = 2048, Tracks = [new MdsTestTrack(0x02, 0, 0, 16), new MdsTestTrack(0x02, 16, 0, 16)] });
        AssertFalse(AlcoholMdsCueBuilder.Parse(dvdTwoTracks).IsValid, "A DVD descriptor with more than one track is refused.");
    }

    private static void TestMdsSubchannelStagingKeepsOnlyMainData(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "mds-strip");
        string mds = WriteMdsSet(root, "sub", TwoTrackMds(sectorSize: 2448, subchannels: 8));
        AlcoholMdsParseResult parsed = AlcoholMdsCueBuilder.Parse(mds);
        AssertTrue(parsed.IsValid && parsed.Layout!.SectorFormat == AlcoholMdsSectorFormat.Raw2352WithSubchannel, "A 2448-byte image with subchannels parses: " + parsed.MessageKey);

        string staged = Path.Combine(root, "staged.img");
        AssertTrue(AlcoholMdfStager.TryStageAsync(parsed.Layout!, staged, default).GetAwaiter().GetResult(), "The image is staged.");
        byte[] expected = StripSubchannels(File.ReadAllBytes(parsed.Layout!.ImagePath));
        AssertEqual(Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(staged))),
            "The staged image holds exactly the 2352-byte main data of every sector.");
        AssertFalse(AlcoholMdfStager.TryStageAsync(parsed.Layout!, staged, default).GetAwaiter().GetResult(), "An existing destination is never overwritten.");
        AssertEqual(102L * 2448, new FileInfo(parsed.Layout!.ImagePath).Length, "The source .mdf is unchanged.");
    }

    private static byte[] StripSubchannels(byte[] image)
    {
        int sectors = image.Length / 2448;
        byte[] result = new byte[sectors * 2352];
        for (int i = 0; i < sectors; i++)
        {
            Buffer.BlockCopy(image, i * 2448, result, i * 2352, 2352);
        }

        return result;
    }

    private static void TestMdsIntakeAndPlanning(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "mds-plan");
        string cd = WriteMdsSet(Path.Combine(root, "cd"), "game", TwoTrackMds());

        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(cd).Role, "An .mds enters the queue.");
        AssertFalse(QueueInputClassifier.Classify(Path.ChangeExtension(cd, ".mdf")).IsSupported, "Its .mdf stays out of the queue.");

        ChdWorkflowProfilePlan cdPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(cd);
        AssertTrue(cdPlan.IsSupported, "A CD .mds plans a conversion.");
        AssertEqual("createcd", cdPlan.Command, "A CD .mds uses createcd.");
        AssertEqual(ChdMediaFormatKind.Mds, cdPlan.MediaKind, "MDS media kind.");
        AssertEqual(ChdPlatformProfiles.AlcoholCd, cdPlan.PlatformProfile, "Alcohol CD profile.");
        AssertEqual(ChdPlatformProfiles.AlcoholCd, ChdPlatformProfiles.ResolveForCommand("createcd", cd, null), "createcd resolves the Alcohol CD profile.");

        string dvd = WriteMdsSet(Path.Combine(root, "dvd"), "movie", new MdsTestImage
        {
            Medium = 0x10,
            SectorSize = 2048,
            Tracks = [new MdsTestTrack(0x02, 0, 0, 64)]
        });
        ChdWorkflowProfilePlan dvdPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(dvd);
        AssertTrue(dvdPlan.IsSupported, "A DVD .mds plans a conversion: " + dvdPlan.FailureMessage);
        AssertEqual("createdvd", dvdPlan.Command, "A DVD .mds uses createdvd.");
        AssertEqual(ChdPlatformProfiles.AlcoholDvd, dvdPlan.PlatformProfile, "Alcohol DVD profile.");
        AssertEqual(ChdPlatformProfiles.AlcoholDvd, ChdPlatformProfiles.ResolveForCommand("createdvd", dvd, null), "createdvd resolves the Alcohol DVD profile.");

        ChdWorkflowProfilePlan requested = ChdWorkflowProfilePlanner.PlanCreateFromSource(dvd, IsoCreateCommandOverride.Auto, ChdMediaContainerKind.DirectFile, ChdPlatformProfiles.Ps2Dvd.PlatformId);
        AssertEqual("createdvd", requested.Command, "A selected platform profile does not override what the descriptor says.");

        string broken = WriteMdsSet(Path.Combine(root, "broken"), "bad", new MdsTestImage { Sessions = 2, Tracks = TwoTrackMds().Tracks });
        ChdWorkflowProfilePlan refused = ChdWorkflowProfilePlanner.PlanCreateFromSource(broken);
        AssertFalse(refused.IsSupported, "A multi-session .mds is refused at planning.");
        AssertEqual(AlcoholMdsCueBuilder.MultiSessionMessageKey, refused.FailureMessage, "The refusal names the reason.");
    }

    private static void RunChdman(string chdmanPath, params string[] arguments)
    {
        var start = new ProcessStartInfo(chdmanPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertEqual(0, process.ExitCode, "chdman " + arguments[0] + " failed: " + stderr);
    }

    private static void TestMdsRoundTripsWithBundledChdman(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "mds-roundtrip");

        var cases = new (string Name, MdsTestImage Spec, Func<byte[], byte[]> Expected, string[] CueTokens)[]
        {
            ("raw", TwoTrackMds(), image => image, ["MODE1/2352", "AUDIO"]),
            ("subchannel", TwoTrackMds(sectorSize: 2448, subchannels: 8), StripSubchannels, ["MODE1/2352", "AUDIO"]),
            ("cooked", new MdsTestImage { SectorSize = 2048, Tracks = [new MdsTestTrack(0xAA, 0, 150, 48)] }, image => image, ["MODE1"])
        };

        foreach (var (name, spec, expected, cueTokens) in cases)
        {
            string mds = WriteMdsSet(Path.Combine(root, name, "in"), "game", spec, seed: 51);
            string outDir = Path.Combine(root, name, "out");
            Directory.CreateDirectory(outDir);
            string chd = Path.Combine(outDir, "game.chd");

            ChdConversionResult created = new ChdConversionService().ConvertToChdAsync(chdmanPath, mds, chd).GetAwaiter().GetResult();
            AssertTrue(created.IsSuccess, name + " MDS converts to CHD: " + created.Message + " " + created.Error);
            AssertTrue(created.CommandLine.Contains("createcd", StringComparison.OrdinalIgnoreCase), name + ": chdman ran createcd.");

            string cue = Path.Combine(outDir, "restored.cue");
            string bin = Path.Combine(outDir, "restored.bin");
            RunChdman(chdmanPath, "extractcd", "-i", chd, "-o", cue, "-ob", bin);

            byte[] source = File.ReadAllBytes(Path.ChangeExtension(mds, ".mdf"));
            AssertEqual(Convert.ToHexString(SHA256.HashData(expected(source))), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bin))),
                name + ": extracted tracks match the .mdf main data byte for byte.");
            string cueText = File.ReadAllText(cue);
            AssertTrue(cueTokens.All(token => cueText.Contains(token, StringComparison.Ordinal)), name + ": extracted CUE keeps the track types: " + cueText);
            AssertEqual(source.Length, new FileInfo(Path.ChangeExtension(mds, ".mdf")).Length, name + ": the source .mdf is unchanged.");
        }

        string dvdRoot = Path.Combine(root, "dvd");
        string dvd = WriteMdsSet(Path.Combine(dvdRoot, "in"), "movie", new MdsTestImage
        {
            Medium = 0x10,
            SectorSize = 2048,
            Tracks = [new MdsTestTrack(0x02, 0, 0, 512)]
        }, seed: 61);
        string dvdOut = Path.Combine(dvdRoot, "out");
        Directory.CreateDirectory(dvdOut);
        string dvdChd = Path.Combine(dvdOut, "movie.chd");
        ChdConversionResult dvdCreated = new ChdConversionService().ConvertToChdAsync(chdmanPath, dvd, dvdChd).GetAwaiter().GetResult();
        AssertTrue(dvdCreated.IsSuccess, "DVD MDS converts to CHD: " + dvdCreated.Message + " " + dvdCreated.Error);
        AssertTrue(dvdCreated.CommandLine.Contains("createdvd", StringComparison.OrdinalIgnoreCase), "chdman ran createdvd for the DVD .mds.");

        string iso = Path.Combine(dvdOut, "restored.iso");
        RunChdman(chdmanPath, "extractdvd", "-i", dvdChd, "-o", iso);
        AssertEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(dvd, ".mdf")))), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(iso))),
            "Extracted DVD data matches the .mdf byte for byte.");
    }
}
