using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static readonly string[] TosecStyleGdRomCue =
    [
        "REM SINGLE-DENSITY AREA",
        "FILE \"Game (Track 01).bin\" BINARY",
        "  TRACK 01 MODE1/2352",
        "    INDEX 01 00:00:00",
        "FILE \"Game (Track 02).bin\" BINARY",
        "  TRACK 02 AUDIO",
        "    PREGAP 00:02:00",
        "    INDEX 01 00:00:00",
        "REM HIGH-DENSITY AREA",
        "FILE \"Game (Track 03).bin\" BINARY",
        "  TRACK 03 MODE1/2352",
        "    INDEX 01 00:00:00"
    ];

    // A GD-ROM CUE is read into GDI track positions: track 2 starts after track 1 and its 150-sector pregap, and the
    // high-density area starts at LBA 45000. Layouts that are not the plain one are left to chdman as before.
    private static void TestGdRomCueBecomesGdiLines()
    {
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("D", "Game (Track 01).bin")] = 300 * 2352,
            [Path.Combine("D", "Game (Track 02).bin")] = 526 * 2352,
            [Path.Combine("D", "Game (Track 03).bin")] = 1000 * 2352
        };
        long Length(string path) => sizes.TryGetValue(path, out long size) ? size : -1;

        IReadOnlyList<GdRomCueTrack>? tracks = GdRomCueGdi.Parse(TosecStyleGdRomCue, "D", Length);
        AssertTrue(tracks is { Count: 3 }, "A GD-ROM CUE with three tracks is read.");
        AssertEqual(0L, tracks![0].Lba, "Track 1 starts at LBA 0.");
        AssertEqual(450L, tracks[1].Lba, "Track 2 starts after track 1 (300) and its pregap (150).");
        AssertEqual(45000L, tracks[2].Lba, "The high-density area starts at LBA 45000.");
        AssertTrue(tracks[1].IsAudio && !tracks[0].IsAudio && !tracks[2].IsAudio, "Track types are kept.");

        string gdi = GdRomCueGdi.BuildGdi(tracks, GdRomCueGdi.StagedFileName);
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        AssertTrue(arabic.ContainsKey(GdRomCueGdi.StagingFailedMessageKey) && english.ContainsKey(GdRomCueGdi.StagingFailedMessageKey), "The staging failure has text in both languages.");

        AssertEqual("3\r\n1 0 4 2352 track01.bin 0\r\n2 450 0 2352 track02.raw 0\r\n3 45000 4 2352 track03.bin 0\r\n", gdi, "The GDI lines match the layout chdman expects.");

        string[] Without(string removed) => [.. TosecStyleGdRomCue.Where(line => line != removed)];
        string[] With(int index, string added) => [.. TosecStyleGdRomCue.Take(index), added, .. TosecStyleGdRomCue.Skip(index)];
        AssertTrue(GdRomCueGdi.Parse(Without("REM HIGH-DENSITY AREA"), "D", Length) is null, "A CD CUE without the high-density area is not changed.");
        AssertTrue(GdRomCueGdi.Parse(With(7, "    INDEX 00 00:00:00"), "D", Length) is null, "A CUE with INDEX 00 is left to chdman.");
        AssertTrue(GdRomCueGdi.Parse([.. TosecStyleGdRomCue.Select(line => line.Replace("MODE1/2352", "MODE2/2352"))], "D", Length) is null, "Other track modes are left to chdman.");
        AssertTrue(GdRomCueGdi.Parse(TosecStyleGdRomCue, "D", path => path.EndsWith("02).bin", StringComparison.Ordinal) ? 1000 : Length(path)) is null, "A track file that is not whole sectors is left to chdman.");
        AssertTrue(GdRomCueGdi.Parse(TosecStyleGdRomCue, "D", path => path.EndsWith("03).bin", StringComparison.Ordinal) ? -1 : Length(path)) is null, "A missing track file is left to chdman.");
        AssertTrue(GdRomCueGdi.Parse([.. TosecStyleGdRomCue.Select(line => line.Replace("Game (Track 01).bin", @"..\Game (Track 01).bin"))], "D", Length) is null, "A track file outside the CUE folder is refused.");
    }

    // A GD-ROM CUE converts through a temporary GDI: the CHD extracts to the same track files as the dump, so track 2's
    // pregap is not added to the end of track 1. Nothing is written next to the source.
    private static void TestGdRomCueConvertsWithTracksInPlace(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "gdrom-cue-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string extracted = Path.Combine(root, "extracted");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(extracted);

        var random = new Random(7);
        byte[] Track(int sectors)
        {
            byte[] data = new byte[sectors * 2352];
            random.NextBytes(data);
            return data;
        }

        byte[][] tracks = [Track(300), Track(526), Track(1000)];
        for (int i = 0; i < tracks.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(source, $"Game (Track 0{i + 1}).bin"), tracks[i]);
        }

        string cue = Path.Combine(source, "Game.cue");
        File.WriteAllLines(cue, TosecStyleGdRomCue);
        string chd = Path.Combine(root, "Game.chd");
        string[] sourceFilesBefore = Directory.GetFiles(source);

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

        ChdConversionResult created = service.ConvertToChdAsync(chdmanPath, cue, chd).GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, "The GD-ROM CUE converts: " + created.Message + " " + created.Error);
        AssertTrue(Directory.GetFiles(source).Order().SequenceEqual(sourceFilesBefore.Order()), "Nothing is written next to the source.");

        var extract = Process.Start(new ProcessStartInfo(chdmanPath)
        {
            ArgumentList = { "extractcd", "-i", chd, "-o", Path.Combine(extracted, "disc.gdi") },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        extract.StandardOutput.ReadToEnd();
        extract.StandardError.ReadToEnd();
        AssertTrue(extract.WaitForExit(120_000) && extract.ExitCode == 0, "chdman extracts the CHD to GDI.");

        string[] gdi = File.ReadAllLines(Path.Combine(extracted, "disc.gdi"));
        AssertEqual("2 450 0 2352 disc02.raw 0", gdi[2].Trim(), "Track 2 is at LBA 450 in the CHD.");
        string[] outputs = [Path.Combine(extracted, "disc01.bin"), Path.Combine(extracted, "disc02.raw"), Path.Combine(extracted, "disc03.bin")];
        for (int i = 0; i < tracks.Length; i++)
        {
            AssertEqual(
                Convert.ToHexString(SHA256.HashData(tracks[i])),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputs[i]))),
                $"Track {i + 1} comes back exactly as dumped.");
        }
    }
}
