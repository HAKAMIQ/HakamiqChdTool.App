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
    // Builds a valid ZSO whose blocks are all stored uncompressed (index high bit set),
    // so no LZ4 encoder is needed in the test project.
    private static byte[] BuildStoredZso(byte[] iso, int blockSize = 2048)
    {
        int blocks = iso.Length / blockSize;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("ZISO"));
        writer.Write(24u);
        writer.Write((ulong)iso.Length);
        writer.Write((uint)blockSize);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((ushort)0);

        uint dataStart = (uint)(24 + (4 * (blocks + 1)));
        for (int i = 0; i <= blocks; i++)
        {
            uint offset = dataStart + (uint)(i * blockSize);
            writer.Write(i < blocks ? offset | 0x8000_0000u : offset);
        }

        writer.Write(iso);
        writer.Flush();
        return stream.ToArray();
    }

    // A valid LZ4 block made of one literal-only sequence (no matches).
    private static byte[] Lz4LiteralBlock(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream();
        int length = data.Length;
        stream.WriteByte((byte)(Math.Min(length, 15) << 4));
        if (length >= 15)
        {
            int rest = length - 15;
            while (rest >= 255)
            {
                stream.WriteByte(255);
                rest -= 255;
            }

            stream.WriteByte((byte)rest);
        }

        stream.Write(data);
        return stream.ToArray();
    }

    // maxcso layout for large images: 16 KB blocks and a compressed final block that
    // decodes to a full block (zero padded) although the image ends mid-block.
    private static byte[] BuildMaxcsoStyleZso(byte[] iso, int blockSize = 16384)
    {
        int blocks = (iso.Length + blockSize - 1) / blockSize;
        var payloads = new byte[blocks][];
        for (int i = 0; i < blocks; i++)
        {
            byte[] full = new byte[blockSize];
            int count = Math.Min(blockSize, iso.Length - (i * blockSize));
            Array.Copy(iso, i * blockSize, full, 0, count);
            payloads[i] = Lz4LiteralBlock(full);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("ZISO"));
        writer.Write(24u);
        writer.Write((ulong)iso.Length);
        writer.Write((uint)blockSize);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((ushort)0);

        uint offset = (uint)(24 + (4 * (blocks + 1)));
        for (int i = 0; i < blocks; i++)
        {
            writer.Write(offset);
            offset += (uint)payloads[i].Length;
        }

        writer.Write(offset);
        foreach (byte[] payload in payloads)
        {
            writer.Write(payload);
        }

        writer.Flush();
        return stream.ToArray();
    }

    // CSO1 in maxcso's large-image layout: raw-deflate 16 KB blocks, the final partial
    // block zero-filled and compressed as a full block. CsoKit 0.6.5 rejected this as
    // InvalidCompressedData; 0.6.6 accepts it.
    private static byte[] BuildMaxcsoStyleCso(byte[] iso, int blockSize = 16384)
    {
        int blocks = (iso.Length + blockSize - 1) / blockSize;
        var payloads = new byte[blocks][];
        var stored = new bool[blocks];
        for (int i = 0; i < blocks; i++)
        {
            byte[] full = new byte[blockSize];
            Array.Copy(iso, i * blockSize, full, 0, Math.Min(blockSize, iso.Length - (i * blockSize)));
            using var compressed = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            {
                deflate.Write(full);
            }

            stored[i] = compressed.Length >= blockSize;
            payloads[i] = stored[i] ? full : compressed.ToArray();
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("CISO"));
        writer.Write(24u);
        writer.Write((ulong)iso.Length);
        writer.Write((uint)blockSize);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((ushort)0);

        uint offset = (uint)(24 + (4 * (blocks + 1)));
        for (int i = 0; i < blocks; i++)
        {
            writer.Write(stored[i] ? offset | 0x8000_0000u : offset);
            offset += (uint)payloads[i].Length;
        }

        writer.Write(offset);
        foreach (byte[] payload in payloads)
        {
            writer.Write(payload);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void TestMaxcsoLargeBlockCsoConverts(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "cso-maxcso-16k");
        Directory.CreateDirectory(root);

        // Half compressible, half random, so both deflated and stored blocks appear.
        byte[] iso = CreateIsoPayload(29, 13);
        Array.Clear(iso, 0, iso.Length / 2);
        string cso = Path.Combine(root, "game.cso");
        File.WriteAllBytes(cso, BuildMaxcsoStyleCso(iso));
        string chd = Path.Combine(root, "game.chd");
        string restored = Path.Combine(root, "restored.iso");

        var service = new ChdConversionService(
            new ChdCommandPreparationService(),
            new ChdProcessExecutionService(),
            new ChdResultMappingService(),
            new ChdVerificationBridge(),
            new ChdmanCapabilityService(),
            null,
            new PlatformAwareChdProfilePolicy(),
            new CsoPreprocessor(
                new CsoToolProbe(new CsoToolLocator(preferredToolPath: null), new ExternalToolProcessRunner()),
                new ExternalToolProcessRunner()));

        ChdConversionResult created = service.ConvertToChdAsync(chdmanPath, cso, chd).GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, "maxcso 16 KB-block CSO converts (needs CsoKit 0.6.6+): " + created.Message + " " + created.Error);

        ChdConversionResult extracted = service.ConvertToChdAsync(
                chdmanPath, chd, restored,
                extractionKind: ChdmanExtractionKind.ExtractDvd,
                extractionMetadataDecisionConfirmed: true)
            .GetAwaiter().GetResult();
        AssertTrue(extracted.IsSuccess, "extractdvd succeeds: " + extracted.Message);
        AssertEqual(
            Convert.ToHexString(SHA256.HashData(iso)),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))),
            "The ISO inside the maxcso CSO survives the round trip exactly.");
    }

    private static byte[] CreateIsoPayload(int sectors, int seed)
    {
        byte[] iso = new byte[sectors * 2048];
        new Random(seed).NextBytes(iso);
        return iso;
    }

    private static void TestZsoIntakeRequiresZisoMagic(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "zso-intake");
        Directory.CreateDirectory(root);

        string valid = Path.Combine(root, "game.zso");
        File.WriteAllBytes(valid, BuildStoredZso(CreateIsoPayload(16, 3)));
        MediaInputDescriptor descriptor = MediaInputClassifier.Shared.Classify(valid);
        AssertEqual(MediaInputKind.ZSO, descriptor.Kind, "ZSO extension maps to the ZSO kind.");
        AssertEqual(MediaInputProbeStatus.MagicConfirmed, descriptor.ProbeStatus, "ZISO magic is confirmed.");
        AssertEqual(QueueInputRole.ConvertibleDiscImage, QueueInputClassifier.Classify(valid).Role, "A real ZSO may enter the queue.");

        string fake = Path.Combine(root, "fake.zso");
        File.WriteAllBytes(fake, Encoding.ASCII.GetBytes("CISO-not-a-zso-file"));
        AssertFalse(QueueInputClassifier.Classify(fake).IsSupported, "A .zso without ZISO magic is rejected at intake.");
    }

    private static void TestZsoPlannerExpandsThenCreatesDvd(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "zso-planner");
        Directory.CreateDirectory(root);
        string zso = Path.Combine(root, "game.zso");
        File.WriteAllBytes(zso, BuildStoredZso(CreateIsoPayload(16, 4)));

        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(zso);
        AssertTrue(plan.IsSupported, "ZSO is convertible.");
        AssertEqual("createdvd", plan.Command, "ZSO converts through createdvd.");
        AssertEqual(ChdMediaFormatKind.Zso, plan.MediaKind, "ZSO media kind.");
        AssertEqual(ChdPlatformProfiles.CompressedZso, plan.PlatformProfile, "ZSO profile.");
        AssertEqual(ChdInputPreparationKind.ExpandZsoToIso, plan.PlatformProfile!.PreparationKind, "ZSO is expanded before chdman.");
        AssertEqual(ChdPlatformProfiles.CompressedZso, ChdPlatformProfiles.ResolveForInput(zso, null), "Auto resolves the ZSO profile.");
    }

    private static void TestZsoToolVersionAndDetectParsing()
    {
        AssertFalse(CsoPreprocessor.SupportsZso("CsoKit 0.6.3+e1a4bca"), "0.6.3 cannot decompress ZSO.");
        AssertTrue(CsoPreprocessor.SupportsZso("CsoKit 0.6.4+30e2b1bd"), "0.6.4 decompresses ZSO.");
        AssertTrue(CsoPreprocessor.SupportsZso("CsoKit 1.0.0"), "Newer versions are accepted.");
        AssertFalse(CsoPreprocessor.SupportsZso(string.Empty), "Unknown version is rejected.");

        AssertEqual<long?>(65536, CsoPreprocessor.TryReadZsoUncompressedSize(
            "{\"success\":true,\"format\":\"Zso\",\"magic\":\"ZISO\",\"uncompressedSize\":65536}"),
            "Declared size is read from a ZSO detect result.");
        AssertEqual<long?>(null, CsoPreprocessor.TryReadZsoUncompressedSize(
            "{\"success\":true,\"format\":\"Cso\",\"magic\":\"CISO\",\"uncompressedSize\":65536}"),
            "A CSO detect result is never treated as ZSO.");
        AssertEqual<long?>(null, CsoPreprocessor.TryReadZsoUncompressedSize("not json"), "Invalid JSON is rejected.");
    }

    private static void TestZsoRoundTripWithBundledTools(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "zso-roundtrip");
        string inDir = Path.Combine(root, "in");
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(inDir);
        Directory.CreateDirectory(outDir);

        byte[] iso = CreateIsoPayload(64, 9);
        string zso = Path.Combine(inDir, "game.zso");
        File.WriteAllBytes(zso, BuildStoredZso(iso));
        string chd = Path.Combine(outDir, "game.chd");
        string restored = Path.Combine(outDir, "restored.iso");

        var service = new ChdConversionService(
            new ChdCommandPreparationService(),
            new ChdProcessExecutionService(),
            new ChdResultMappingService(),
            new ChdVerificationBridge(),
            new ChdmanCapabilityService(),
            null,
            new PlatformAwareChdProfilePolicy(),
            // Pin the bundled CsoKit so a user-configured external path cannot affect the test.
            new CsoPreprocessor(
                new CsoToolProbe(new CsoToolLocator(preferredToolPath: null), new ExternalToolProcessRunner()),
                new ExternalToolProcessRunner()));

        ChdConversionResult created = service.ConvertToChdAsync(chdmanPath, zso, chd).GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, "ZSO converts to CHD: " + created.Message + " " + created.Error);
        AssertTrue(created.CommandLine.Contains("createdvd", StringComparison.OrdinalIgnoreCase), "chdman ran createdvd.");

        ChdConversionResult extracted = service.ConvertToChdAsync(
                chdmanPath, chd, restored,
                extractionKind: ChdmanExtractionKind.ExtractDvd,
                extractionMetadataDecisionConfirmed: true)
            .GetAwaiter().GetResult();
        AssertTrue(extracted.IsSuccess, "extractdvd succeeds: " + extracted.Message + " " + extracted.Error);
        AssertEqual(
            Convert.ToHexString(SHA256.HashData(iso)),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))),
            "Extracted ISO matches the ISO inside the ZSO.");
        AssertTrue(File.Exists(zso), "The source ZSO is preserved.");

        // maxcso layout (16 KB blocks, full-size compressed final block).
        byte[] maxcsoIso = CreateIsoPayload((3 * 8) + 5, 12);
        string maxcsoZso = Path.Combine(inDir, "maxcso.zso");
        File.WriteAllBytes(maxcsoZso, BuildMaxcsoStyleZso(maxcsoIso));
        string maxcsoChd = Path.Combine(outDir, "maxcso.chd");
        string maxcsoRestored = Path.Combine(outDir, "maxcso.iso");
        ChdConversionResult maxcsoCreated = service.ConvertToChdAsync(chdmanPath, maxcsoZso, maxcsoChd).GetAwaiter().GetResult();
        AssertTrue(maxcsoCreated.IsSuccess, "maxcso-layout ZSO converts: " + maxcsoCreated.Message + " " + maxcsoCreated.Error);
        ChdConversionResult maxcsoExtracted = service.ConvertToChdAsync(
                chdmanPath, maxcsoChd, maxcsoRestored,
                extractionKind: ChdmanExtractionKind.ExtractDvd,
                extractionMetadataDecisionConfirmed: true)
            .GetAwaiter().GetResult();
        AssertTrue(maxcsoExtracted.IsSuccess, "maxcso-layout extractdvd succeeds: " + maxcsoExtracted.Message);
        AssertEqual(
            Convert.ToHexString(SHA256.HashData(maxcsoIso)),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(maxcsoRestored))),
            "maxcso-layout ZSO round trip keeps the exact ISO (padding is not added).");

        // A truncated ZSO must stop before chdman and leave no CHD behind.
        string damaged = Path.Combine(inDir, "broken.zso");
        byte[] bytes = File.ReadAllBytes(zso);
        File.WriteAllBytes(damaged, bytes.Take(bytes.Length - 1000).ToArray());
        string damagedChd = Path.Combine(outDir, "broken.chd");
        ChdConversionResult failed = service.ConvertToChdAsync(chdmanPath, damaged, damagedChd).GetAwaiter().GetResult();
        AssertFalse(failed.IsSuccess, "A truncated ZSO is not converted.");
        AssertEqual(CsoPreprocessor.ZsoPreparationFailedMessageKey, failed.Message, "Truncated ZSO reports the ZSO failure message.");
        AssertFalse(File.Exists(damagedChd), "No CHD is created from a truncated ZSO.");
    }
}
