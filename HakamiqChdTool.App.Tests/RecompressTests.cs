using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Recompress;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Codec families follow the CHD media type, and output planning never targets the source folder or an existing file.
    private static void TestRecompressPlanning()
    {
        AssertTrue(ChdRecompressService.TryResolveCodecs("CD-ROM", "preset:max", out string cd) && cd == "cdlz,cdzl,cdfl", "CD CHDs use CD codecs.");
        AssertTrue(ChdRecompressService.TryResolveCodecs("GD-ROM", "preset:fast", out string gd) && gd == "cdzs,cdfl", "GD-ROM CHDs use CD codecs.");
        AssertTrue(ChdRecompressService.TryResolveCodecs("DVD-ROM", "preset:default", out string dvd) && dvd == "lzma,zlib,huff,flac", "The default for DVD is chdman's createdvd default.");
        AssertTrue(ChdRecompressService.TryResolveCodecs("HD-ROM", "preset:fast", out string hd) && hd == "zstd,flac", "Hard-disk CHDs use the general codecs.");
        AssertFalse(ChdRecompressService.TryResolveCodecs("Unknown", "preset:max", out _), "An unknown media type is refused.");

        string root = Path.Combine(Path.GetTempPath(), "hct-recompress-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "out"));
            string source = Path.Combine(root, "src", "Game.chd");
            File.WriteAllBytes(source, [1]);

            AssertEqual(ChdRecompressService.SameFolderKey, ChdRecompressService.PlanOutput(source, new("preset:max", false, Path.Combine(root, "src")), out _, out _), "The source folder is refused when not replacing.");
            AssertEqual(ChdRecompressService.SameFolderKey, ChdRecompressService.PlanOutput(source, new("preset:max", false, null), out _, out _), "A missing output folder is refused.");
            AssertEqual(null, ChdRecompressService.PlanOutput(source, new("preset:max", false, Path.Combine(root, "out")), out string final, out string pending), "Another folder is accepted.");
            AssertEqual(Path.Combine(root, "out", "Game.chd"), final, "The new file keeps the name.");
            AssertTrue(Path.GetDirectoryName(pending) == Path.Combine(root, "out") && pending.EndsWith(".chd", StringComparison.Ordinal) && pending != final, "The temporary file sits beside the final path under another name.");

            File.WriteAllBytes(final, [2]);
            AssertEqual(ChdRecompressService.ExistsKey, ChdRecompressService.PlanOutput(source, new("preset:max", false, Path.Combine(root, "out")), out _, out _), "An existing file is never overwritten.");
            AssertEqual(null, ChdRecompressService.PlanOutput(source, new("preset:max", true, null), out string replaceFinal, out string replacePending), "Replacing plans in the source folder.");
            AssertEqual(source, replaceFinal, "Replacing targets the source.");
            AssertEqual(Path.Combine(root, "src"), Path.GetDirectoryName(replacePending), "The temporary file is on the source's volume.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Real chdman: an uncompressed CHD is recompressed smaller with the same SHA-1; the no-gain, replace, exists and
    // not-a-CHD cases keep the user's files and leave no temporary files.
    private static void TestRecompressWithRealChdman()
    {
        string chdman = Path.Combine(AppContext.BaseDirectory, "Tools", "chdman.exe");
        if (!File.Exists(chdman))
        {
            throw new TestSkippedException("Bundled chdman is not available next to the test runner.");
        }

        string root = Path.Combine(Path.GetTempPath(), "hct-recompress-" + Guid.NewGuid().ToString("N"));
        try
        {
            string raw = Path.Combine(root, "raw");
            string src = Path.Combine(root, "src");
            string output = Path.Combine(root, "out");
            Directory.CreateDirectory(raw);
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(output);

            string cue = Path.Combine(raw, "Disc.cue");
            File.WriteAllBytes(Path.Combine(raw, "Disc.bin"), Enumerable.Range(0, 2352 * 400).Select(i => (byte)(i % 7 == 0 ? i % 251 : 0x22)).ToArray());
            File.WriteAllText(cue, "FILE \"Disc.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
            string cdChd = Path.Combine(src, "Disc.chd");
            ChdmanCliRunner.Result create = ChdmanCliRunner.ExecuteAsync(chdman, ["createcd", "-i", cue, "-o", cdChd, "-c", "none"], false, null, null).GetAwaiter().GetResult();
            AssertEqual(0, create.ExitCode, "Test setup: uncompressed createcd succeeded. " + create.StandardError);

            string iso = Path.Combine(raw, "Disc.iso");
            File.WriteAllBytes(iso, Enumerable.Range(0, 2048 * 600).Select(i => (byte)(i % 13 == 0 ? i % 241 : 0x33)).ToArray());
            string dvdChd = Path.Combine(src, "Dvd.chd");
            create = ChdmanCliRunner.ExecuteAsync(chdman, ["createdvd", "-i", iso, "-o", dvdChd, "-c", "none"], false, null, null).GetAwaiter().GetResult();
            AssertEqual(0, create.ExitCode, "Test setup: uncompressed createdvd succeeded. " + create.StandardError);

            var info = new ChdInfoService();
            var service = new ChdRecompressService(chdman, info, new ChdVerificationService());
            AssertEqual(string.Empty, info.ReadInfoAsync(chdman, cdChd).GetAwaiter().GetResult().Sha1, "Test setup: an uncompressed CHD stores no SHA-1.");
            long cdBefore = new FileInfo(cdChd).Length;

            RecompressResult saved = service.RecompressAsync(cdChd, new("preset:max", false, output), null, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(RecompressOutcome.Saved, saved.Outcome, "The CD CHD is recompressed: " + saved.MessageKey);
            AssertTrue(saved.NewBytes > 0 && saved.NewBytes < cdBefore, "The new file is smaller.");
            AssertEqual(cdBefore, new FileInfo(cdChd).Length, "The source is untouched when not replacing.");
            ChdInfoResult savedInfo = info.ReadInfoAsync(chdman, saved.OutputPath!).GetAwaiter().GetResult();
            AssertTrue(savedInfo.Sha1.Length == 40, "The new file carries the SHA-1 chdman computed from the source data.");
            // A compressed source stores SHA-1 values; the copy must match them (a mismatch would fail before the size check).
            RecompressResult again = service.RecompressAsync(saved.OutputPath!, new("preset:fast", false, Path.Combine(root, "out2")), null, CancellationToken.None).GetAwaiter().GetResult();
            AssertTrue(again.Outcome is RecompressOutcome.Saved or RecompressOutcome.NoGain, "A compressed CHD recompresses with matching SHA-1 values: " + again.MessageKey);
            if (again.Outcome == RecompressOutcome.Saved)
            {
                AssertEqual(savedInfo.Sha1, info.ReadInfoAsync(chdman, again.OutputPath!).GetAwaiter().GetResult().Sha1, "The second copy has the same SHA-1.");
            }
            AssertEqual("CD-ROM", savedInfo.MediaType, "The new file is still a CD CHD.");

            RecompressResult exists = service.RecompressAsync(cdChd, new("preset:max", false, output), null, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(RecompressOutcome.SkippedExists, exists.Outcome, "An existing output is skipped.");

            RecompressResult noGain = service.RecompressAsync(saved.OutputPath!, new("preset:max", true, null), null, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(RecompressOutcome.NoGain, noGain.Outcome, "Recompressing an already compressed file with the same codecs gains nothing.");
            AssertEqual(saved.NewBytes, new FileInfo(saved.OutputPath!).Length, "A file without gain is left as it was.");

            long dvdBefore = new FileInfo(dvdChd).Length;
            string isoSha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(iso)));
            RecompressResult replaced = service.RecompressAsync(dvdChd, new("preset:fast", true, null), null, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(RecompressOutcome.Replaced, replaced.Outcome, "The DVD CHD is replaced: " + replaced.MessageKey);
            AssertTrue(new FileInfo(dvdChd).Length < dvdBefore, "The replaced file is smaller.");
            AssertEqual(isoSha1, info.ReadInfoAsync(chdman, dvdChd).GetAwaiter().GetResult().DataSha1.ToUpperInvariant(), "The replaced file's data is the original ISO, byte for byte.");

            string fake = Path.Combine(src, "Fake.chd");
            File.WriteAllBytes(fake, new byte[4096]);
            RecompressResult notChd = service.RecompressAsync(fake, new("preset:max", false, output), null, CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(RecompressOutcome.Failed, notChd.Outcome, "A file that is not a CHD fails.");
            AssertEqual(4096L, new FileInfo(fake).Length, "The failed source is untouched.");

            string[] leftovers = [.. Directory.EnumerateFiles(root, "*.recompress-*", SearchOption.AllDirectories), .. Directory.EnumerateFiles(root, "*.bak", SearchOption.AllDirectories)];
            AssertEqual(0, leftovers.Length, "No temporary or backup files are left: " + string.Join(", ", leftovers));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
