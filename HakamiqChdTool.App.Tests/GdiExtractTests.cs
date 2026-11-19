using HakamiqChdTool.App.Core.Workflow.Extraction;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestGdiSheetParsesStrictly()
    {
        string[] quoted =
        [
            "3",
            "1 0 4 2352 \"Crazy Game (USA)01.bin\" 0",
            "2 450 0 2352 track02.raw 0",
            "3 45000 4 2352 \"Crazy Game (USA)03.bin\" 0"
        ];
        AssertTrue(GdiSheet.TryParse(quoted, out List<GdiTrackLine> tracks), "A chdman-style GDI parses.");
        AssertEqual(3, tracks.Count, "Three tracks.");
        AssertEqual("Crazy Game (USA)01.bin", tracks[0].FileName, "Quoted names keep their spaces.");
        AssertEqual(45000L, tracks[2].StartLba, "The high-density track starts at LBA 45000.");
        AssertEqual("1 0 4 2352 \"Game X01.bin\" 0", tracks[0].Format("Game X01.bin"), "Names with spaces are written quoted.");
        AssertEqual("2 450 0 2352 game02.raw 0", tracks[1].Format("game02.raw"), "Plain names are written unquoted.");

        AssertFalse(GdiSheet.TryParse(["2", "1 0 4 2352 a.bin 0"], out _), "A track count that does not match is refused.");
        AssertFalse(GdiSheet.TryParse(["1", "1 0 2 2352 a.bin 0"], out _), "An unknown track type is refused.");
        AssertFalse(GdiSheet.TryParse(["1", "1 0 4 2336 a.bin 0"], out _), "An unsupported sector size is refused.");
        AssertFalse(GdiSheet.TryParse(["1", "1 0 4 2352 ..\\outside.bin 0"], out _), "A path outside the GDI folder is refused.");
        AssertFalse(GdiSheet.TryParse(["2", "1 0 4 2352 a.bin 0", "3 450 0 2352 b.raw 0"], out _), "Non-consecutive track numbers are refused.");
        AssertFalse(GdiSheet.TryParse(["1", "1 0 4 2352 \"a.bin 0"], out _), "An unterminated quote is refused.");
    }

    private static void TestGdRomExtractionPicksGdi()
    {
        var policy = new MetadataAwareChdExtractionPolicy();
        PlatformDetectionResult none = PlatformDetectionResult.Create(string.Empty, string.Empty, 0, string.Empty);

        MetadataAwareChdExtractionDecision gdRom = policy.Resolve(new MetadataAwareChdExtractionRequest("GD-ROM", "game.chd", "game.chd", none, null));
        AssertEqual(ChdmanExtractionKind.ExtractCd, gdRom.ExtractionKind, "A GD-ROM is extracted with extractcd.");
        AssertEqual(".gdi", gdRom.OutputExtension, "A GD-ROM is restored to GDI by default.");

        MetadataAwareChdExtractionDecision cueBin = policy.Resolve(new MetadataAwareChdExtractionRequest("GD-ROM", "game.chd", "game.chd", none, null, ExtractGdRomAsCueBin: true));
        AssertEqual(".cue", cueBin.OutputExtension, "The setting restores a GD-ROM to CUE/BIN.");

        MetadataAwareChdExtractionDecision cd = policy.Resolve(new MetadataAwareChdExtractionRequest("CD-ROM", "game.chd", "game.chd", none, null));
        AssertEqual(".cue", cd.OutputExtension, "A CD is still restored to CUE/BIN.");

        MetadataAwareChdExtractionDecision tagged = policy.Resolve(new MetadataAwareChdExtractionRequest("CD-ROM", "game.chd", "game.chd", none, null, GdRomMetadata: true));
        AssertEqual(".gdi", tagged.OutputExtension, "GD-ROM metadata (CHGD) picks GDI even when the media type reads CD-ROM.");

        AssertEqual(ExtractionOutputKind.GdiBundle, ExtractionOutputContract.Create(ChdmanExtractionKind.ExtractCd, @"C:\w\output.gdi", @"C:\o\Game.gdi").Kind, "A .gdi output is a GDI bundle.");
        AssertEqual(ExtractionOutputKind.CueBinBundle, ExtractionOutputContract.Create(ChdmanExtractionKind.ExtractCd, @"C:\w\output.cue", @"C:\o\Game.cue").Kind, "A .cue output stays a CUE/BIN bundle.");
        AssertFalse(new AppSettings().ExtractGdRomAsCueBin, "GDI is the default for GD-ROM.");
        AssertTrue(new AppSettings { ExtractGdRomAsCueBin = true }.Clone().ExtractGdRomAsCueBin, "The setting survives a settings copy.");
    }

    private static Dictionary<string, byte[]> WriteGdiTracks(string directory, string stem)
    {
        Directory.CreateDirectory(directory);
        var random = new Random(81);
        var tracks = new Dictionary<string, byte[]>
        {
            [stem + "01.bin"] = new byte[2352 * 300],
            [stem + "02.raw"] = new byte[2352 * 300],
            [stem + "03.bin"] = new byte[2352 * 600]
        };
        foreach ((string name, byte[] data) in tracks)
        {
            random.NextBytes(data);
            File.WriteAllBytes(Path.Combine(directory, name), data);
        }

        string Quote(string name) => name.Contains(' ', StringComparison.Ordinal) ? "\"" + name + "\"" : name;
        File.WriteAllText(
            Path.Combine(directory, stem + ".gdi"),
            "3\r\n" +
            "1 0 4 2352 " + Quote(stem + "01.bin") + " 0\r\n" +
            "2 450 0 2352 " + Quote(stem + "02.raw") + " 0\r\n" +
            "3 45000 4 2352 " + Quote(stem + "03.bin") + " 0\r\n",
            Encoding.ASCII);
        return tracks;
    }

    private static void TestGdiFinalizeRenamesTracksAndNeverOverwrites(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "gdi-finalize");
        string pending = Path.Combine(root, "pending");
        string final = Path.Combine(root, "final");
        Directory.CreateDirectory(final);
        Dictionary<string, byte[]> tracks = WriteGdiTracks(pending, "output");
        string finalGdi = Path.Combine(final, "Crazy Game (USA).gdi");
        var validator = new ExtractionOutputBundleValidator();
        ExtractionOutputContract contract = ExtractionOutputContract.Create(ChdmanExtractionKind.ExtractCd, Path.Combine(pending, "output.gdi"), finalGdi);

        AssertTrue(validator.TryFinalize(contract, allowOverwrite: false, out ExtractionOutputBundle bundle, out string failure), "The GDI set is finalized: " + failure);
        AssertEqual(4, bundle.FilePaths.Count, "The bundle holds the GDI and its three tracks.");
        string gdiText = File.ReadAllText(finalGdi);
        AssertTrue(gdiText.Contains("\"Crazy Game (USA)01.bin\"", StringComparison.Ordinal)
                   && gdiText.Contains("\"Crazy Game (USA)02.raw\"", StringComparison.Ordinal)
                   && gdiText.Contains("3 45000 4 2352 \"Crazy Game (USA)03.bin\" 0", StringComparison.Ordinal),
            "The GDI names the renamed tracks and keeps their layout: " + gdiText);
        AssertEqual(Convert.ToHexString(SHA256.HashData(tracks["output03.bin"])), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(final, "Crazy Game (USA)03.bin")))),
            "Track data is moved unchanged.");
        AssertFalse(Directory.EnumerateFiles(pending).Any(), "Nothing is left in the staging folder.");
        AssertTrue(validator.TryValidateExistingFinal(ExtractionOutputKind.GdiBundle, finalGdi, out _, out _), "The final GDI set validates.");

        // A second job targeting the same name must not replace the existing set.
        string pending2 = Path.Combine(root, "pending2");
        WriteGdiTracks(pending2, "output");
        byte[] before = File.ReadAllBytes(Path.Combine(final, "Crazy Game (USA)01.bin"));
        ExtractionOutputContract second = ExtractionOutputContract.Create(ChdmanExtractionKind.ExtractCd, Path.Combine(pending2, "output.gdi"), finalGdi);
        AssertFalse(validator.TryFinalize(second, allowOverwrite: false, out _, out _), "An existing GDI set is not overwritten without permission.");
        AssertEqual(Convert.ToHexString(SHA256.HashData(before)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(final, "Crazy Game (USA)01.bin")))),
            "The existing tracks are untouched.");
        AssertTrue(File.Exists(Path.Combine(pending2, "output01.bin")), "The refused job keeps its staged files for cleanup.");

        // A GDI that points outside its folder is refused and nothing moves.
        string pending3 = Path.Combine(root, "pending3");
        Directory.CreateDirectory(pending3);
        string outside = Path.Combine(root, "outside.bin");
        File.WriteAllBytes(outside, new byte[2352]);
        File.WriteAllText(Path.Combine(pending3, "output.gdi"), "1\r\n1 0 4 2352 ..\\outside.bin 0\r\n");
        ExtractionOutputContract traversal = ExtractionOutputContract.Create(ChdmanExtractionKind.ExtractCd, Path.Combine(pending3, "output.gdi"), Path.Combine(final, "Other.gdi"));
        AssertFalse(validator.TryFinalize(traversal, allowOverwrite: false, out _, out _), "A GDI reference outside the staging folder is refused.");
        AssertTrue(File.Exists(outside) && !File.Exists(Path.Combine(final, "Other.gdi")), "Nothing outside is moved and no GDI is published.");
    }

    private static void TestGdRomChdExtractsToGdiAndProves(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "gdi-roundtrip");
        Dictionary<string, byte[]> source = WriteGdiTracks(Path.Combine(root, "in"), "disc");
        string chd = Path.Combine(root, "disc.chd");
        RunChdman(chdmanPath, "createcd", "-i", Path.Combine(root, "in", "disc.gdi"), "-o", chd);

        ChdInfoResult info = new ChdInfoService().ReadInfoAsync(chdmanPath, chd).GetAwaiter().GetResult();
        AssertTrue(info.IsSuccess && info.MetadataStructure == HakamiqChdTool.App.Core.Verification.DiscStructure.GdRomTracks, "chdman metadata marks the CHD as a GD-ROM: " + info.MediaType);
        MetadataAwareChdExtractionDecision decision = new MetadataAwareChdExtractionPolicy().Resolve(new MetadataAwareChdExtractionRequest(
            info.MediaType, chd, chd, PlatformDetectionResult.Create(string.Empty, string.Empty, 0, string.Empty), info.LogicalBytes,
            GdRomMetadata: info.MetadataStructure == HakamiqChdTool.App.Core.Verification.DiscStructure.GdRomTracks));
        AssertEqual(".gdi", decision.OutputExtension, "The policy picks GDI for this CHD.");

        string staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(staging);
        string pendingGdi = Path.Combine(staging, "output.gdi");
        var service = new ChdConversionService();
        ChdConversionResult extracted = service.ConvertToChdAsync(
                chdmanPath, chd, pendingGdi,
                extractionKind: ChdmanExtractionKind.ExtractCd,
                extractionMetadataDecisionConfirmed: true)
            .GetAwaiter().GetResult();
        AssertTrue(extracted.IsSuccess, "extractcd writes a GDI set: " + extracted.Message + " " + extracted.Error);
        AssertFalse(extracted.CommandLine.Contains("-ob", StringComparison.Ordinal), "No -ob is passed for a GDI output.");

        string final = Path.Combine(root, "out");
        Directory.CreateDirectory(final);
        string finalGdi = Path.Combine(final, "Crazy Game (USA).gdi");
        var validator = new ExtractionOutputBundleValidator();
        AssertTrue(validator.TryFinalize(ExtractionOutputContract.Create(ChdmanExtractionKind.ExtractCd, pendingGdi, finalGdi), false, out ExtractionOutputBundle bundle, out string failure),
            "The extracted GDI set is finalized: " + failure);

        string[] suffixes = ["01.bin", "02.raw", "03.bin"];
        foreach (string suffix in suffixes)
        {
            AssertEqual(
                Convert.ToHexString(SHA256.HashData(source["disc" + suffix])),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(final, "Crazy Game (USA)" + suffix)))),
                "Track " + suffix + " matches the original byte for byte.");
        }

        var proof = new ExtractionOutputProofVerifier(new ChdInfoService(), service, Log.Logger);
        ExtractionOutputProofResult proven = proof.VerifyAsync(chdmanPath, chd, info, decision, bundle, new AppSettings(), default).GetAwaiter().GetResult();
        AssertTrue(proven.IsVerified, "Rebuilding a CHD from the GDI set reproduces the source SHA-1: " + proven.ReasonCode);
    }
}
