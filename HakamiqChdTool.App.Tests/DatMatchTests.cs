using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed record DatRomSpec(string Name, long? Size, string? Sha1 = null, string? Md5 = null, string? Crc = null);

    private sealed record DatGameSpec(string Name, string? Region, params DatRomSpec[] Roms);

    private static string Sha1Of(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

    private static string WriteDat(string directory, string fileName, string? headerName, params DatGameSpec[] games)
    {
        Directory.CreateDirectory(directory);
        StringBuilder xml = new();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        xml.AppendLine("<!DOCTYPE datafile PUBLIC \"-//Logiqx//DTD ROMVault//EN\" \"http://www.logiqx.com/Dats/datafile.dtd\">");
        xml.AppendLine("<datafile><header>");

        if (headerName is not null)
        {
            xml.AppendLine("<name>" + SecurityElement.Escape(headerName) + "</name>");
        }

        xml.AppendLine("<version>2026-01-01</version><author>fixture</author></header>");

        foreach (DatGameSpec game in games)
        {
            string region = game.Region is null ? string.Empty : $" region=\"{game.Region}\"";
            xml.AppendLine($"<game name=\"{SecurityElement.Escape(game.Name)}\"{region}><description>{SecurityElement.Escape(game.Name)}</description>");

            foreach (DatRomSpec rom in game.Roms)
            {
                string size = rom.Size is null ? string.Empty : $" size=\"{rom.Size}\"";
                string sha1 = rom.Sha1 is null ? string.Empty : $" sha1=\"{rom.Sha1}\"";
                string md5 = rom.Md5 is null ? string.Empty : $" md5=\"{rom.Md5}\"";
                string crc = rom.Crc is null ? string.Empty : $" crc=\"{rom.Crc}\"";
                xml.AppendLine($"<rom name=\"{SecurityElement.Escape(rom.Name)}\"{size}{crc}{md5}{sha1}/>");
            }

            xml.AppendLine("</game>");
        }

        xml.AppendLine("</datafile>");
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, xml.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static DatFile ParseDatOrFail(string path)
    {
        DatParseResult result = DatParser.Parse(path, CancellationToken.None);
        AssertEqual(DatParseStatus.Parsed, result.Status, "DAT fixture must parse: " + string.Join("; ", result.Issues.Select(i => i.Code)));
        return result.Dat!;
    }

    private static void TestDatParserSupportedAndBounded(string workDirectory)
    {
        string dir = Path.Combine(workDirectory, "dat-parser");
        string path = WriteDat(dir, "ok.dat", "Sony - PlayStation",
            new DatGameSpec("Game One (USA)", "USA",
                new DatRomSpec("Game One (USA) (Track 1).bin", 100, Sha1: new string('A', 40), Md5: new string('B', 32), Crc: "DEADBEEF"),
                new DatRomSpec("Game One (USA).cue", 50)),
            new DatGameSpec("Game Two", null, new DatRomSpec("two.bin", null)));
        byte[] before = File.ReadAllBytes(path);

        DatParseResult result = DatParser.Parse(path, CancellationToken.None);
        AssertEqual(DatParseStatus.Parsed, result.Status, "Logiqx DAT with a DOCTYPE must parse.");
        AssertEqual(0, result.RejectedRecords, "Clean DAT must reject nothing.");
        DatFile dat = result.Dat!;
        AssertEqual("Sony - PlayStation", dat.Platform, "Platform comes from the header name only.");
        AssertEqual("2026-01-01", dat.Header.Version, "Header version preserved.");
        AssertEqual(2, dat.Games.Count, "Both games parsed.");
        DatGame one = dat.Games[0];
        AssertEqual("USA", one.Region, "Region preserved when provided.");
        AssertEqual(2, one.Roms.Count, "ROM records preserved.");
        AssertEqual(new string('a', 40), one.Roms[0].Sha1, "Hashes are normalized to lowercase.");
        AssertEqual("deadbeef", one.Roms[0].Crc32, "CRC32 preserved.");
        AssertEqual(null, one.Roms[1].Sha1, "Absent hashes stay absent.");
        AssertEqual(null, dat.Games[1].Region, "Region must not be inferred from the name.");
        AssertEqual(null, dat.Games[1].Roms[0].Size, "Absent size stays absent.");
        AssertTrue(before.SequenceEqual(File.ReadAllBytes(path)), "Parsing must not modify the DAT.");

        string noHeader = WriteDat(dir, "noname.dat", null, new DatGameSpec("G", null, new DatRomSpec("g.bin", 1)));
        AssertEqual(null, ParseDatOrFail(noHeader).Platform, "A DAT without a header name has no platform.");

        string unsafeNames = WriteDat(dir, "unsafe.dat", "X", new DatGameSpec("G", null,
            new DatRomSpec("..\\evil.bin", 1), new DatRomSpec("sub/dir.bin", 1), new DatRomSpec("NUL.bin", 1), new DatRomSpec("fine.bin", 1)));
        DatRom[] roms = ParseDatOrFail(unsafeNames).Games[0].Roms.ToArray();
        AssertTrue(roms[0].NameIsUnsafe && roms[1].NameIsUnsafe && roms[2].NameIsUnsafe, "Traversal, separator and device names must be flagged unsafe.");
        AssertFalse(roms[3].NameIsUnsafe, "A plain file name is safe.");
    }

    private static void TestDatParserRejectsMalformedAndUnsupported(string workDirectory)
    {
        string dir = Path.Combine(workDirectory, "dat-parser-bad");
        Directory.CreateDirectory(dir);

        string badRecords = Path.Combine(dir, "bad-records.dat");
        File.WriteAllText(badRecords,
            "<datafile><header><name>X</name></header>" +
            "<game name=\"Good\"><rom name=\"good.bin\" size=\"5\" sha1=\"" + new string('a', 40) + "\"/></game>" +
            "<game name=\"BadSha\"><rom name=\"a.bin\" size=\"5\" sha1=\"abc\"/></game>" +
            "<game name=\"BadSize\"><rom name=\"b.bin\" size=\"-5\"/></game>" +
            "<game name=\"NoRomName\"><rom size=\"5\"/></game>" +
            "<game><rom name=\"c.bin\" size=\"5\"/></game>" +
            "</datafile>");
        DatParseResult bad = DatParser.Parse(badRecords, CancellationToken.None);
        AssertEqual(DatParseStatus.Parsed, bad.Status, "Bad records are rejected individually.");
        AssertEqual(4, bad.RejectedRecords, "Four malformed records must be counted.");
        AssertEqual(4, bad.Dat!.Games.Count, "Named games are kept (Good, BadSha, BadSize, NoRomName); the unnamed game is rejected.");
        AssertEqual(1, bad.Dat.Games.Count(g => g.Roms.Count > 0), "Only the valid ROM survives.");
        AssertTrue(bad.Issues.Any(i => i.Code == "rom-rejected"), "Rejections must be reported.");

        string romCenter = Path.Combine(dir, "romcenter.dat");
        File.WriteAllText(romCenter, "[CREDITS]\nauthor=x\n[GAMES]\n¬G¬G¬g.bin¬12345678¬1¬\n");
        DatParseResult rcResult = DatParser.Parse(romCenter, CancellationToken.None);
        AssertEqual(DatParseStatus.Unsupported, rcResult.Status, "RomCenter must be explicitly unsupported.");
        AssertEqual("format-unrecognized", rcResult.Issues[0].Code, "Unsupported reason code.");
        AssertEqual(null, rcResult.Dat, "No DAT is returned for unsupported input.");

        string empty = Path.Combine(dir, "empty.dat");
        File.WriteAllBytes(empty, []);
        AssertEqual(DatParseStatus.Unsupported, DatParser.Parse(empty, CancellationToken.None).Status, "An empty file is unsupported, not an empty catalog.");

        string mame = Path.Combine(dir, "mame.xml");
        File.WriteAllText(mame, "<mame build=\"x\"><machine name=\"a\"/></mame>");
        AssertEqual(DatParseStatus.Unsupported, DatParser.Parse(mame, CancellationToken.None).Status, "MAME XML is unsupported.");

        string junk = Path.Combine(dir, "junk.dat");
        File.WriteAllText(junk, "hello world");
        AssertEqual(DatParseStatus.Unsupported, DatParser.Parse(junk, CancellationToken.None).Status, "Unrecognized text is unsupported.");

        string broken = Path.Combine(dir, "broken.dat");
        File.WriteAllText(broken, "<datafile><game name=\"x\"><rom name=\"y\"></datafile>");
        AssertEqual(DatParseStatus.Malformed, DatParser.Parse(broken, CancellationToken.None).Status, "Broken XML is malformed.");

        AssertEqual(DatParseStatus.Unreadable, DatParser.Parse(Path.Combine(dir, "missing.dat"), CancellationToken.None).Status, "Missing DAT is unreadable.");

        string big = Path.Combine(dir, "big.dat");
        using (var stream = new FileStream(big, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(DatParser.MaxFileBytes + 1);
        }

        try
        {
            AssertEqual(DatParseStatus.TooLarge, DatParser.Parse(big, CancellationToken.None).Status, "Oversized DAT must be refused before parsing.");
        }
        finally
        {
            File.Delete(big);
        }

        string good = WriteDat(dir, "good.dat", "X", new DatGameSpec("G", null, new DatRomSpec("g.bin", 1)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertEqual(DatParseStatus.Cancelled, DatParser.Parse(good, cancelled.Token).Status, "Pre-cancelled parse must report Cancelled.");
    }

    private static void TestDatParserDoesNotResolveEntities(string workDirectory)
    {
        string dir = Path.Combine(workDirectory, "dat-parser-xxe");
        Directory.CreateDirectory(dir);
        string secret = Path.Combine(dir, "secret.txt");
        File.WriteAllText(secret, "CANARY-SECRET-VALUE");
        string dat = Path.Combine(dir, "xxe.dat");
        File.WriteAllText(dat,
            "<?xml version=\"1.0\"?><!DOCTYPE datafile [<!ENTITY xxe SYSTEM \"" + new Uri(secret).AbsoluteUri + "\">]>" +
            "<datafile><header><name>&xxe;</name></header><game name=\"G&xxe;\"><rom name=\"g.bin\" size=\"1\"/></game></datafile>");

        DatParseResult result = DatParser.Parse(dat, CancellationToken.None);
        string everything = string.Join('|', result.Issues.Select(i => i.Message))
            + (result.Dat is null ? string.Empty : result.Dat.Header.Name + "|" + string.Join('|', result.Dat.Games.Select(g => g.Name)));
        AssertFalse(everything.Contains("CANARY-SECRET-VALUE"), "External entities must never be resolved into parsed data.");
    }

    private static (string Root, IReadOnlyList<LibraryIndexEntry> Entries) BuildMatchLibrary(string workDirectory, string name, params (string Path, byte[] Bytes)[] files)
    {
        string root = Path.Combine(workDirectory, name);
        Directory.CreateDirectory(root);

        foreach ((string path, byte[] bytes) in files)
        {
            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }

        return (root, IndexLibrary(root).Entries);
    }

    private static DatMatchReport RunMatch(DatMatcher matcher, string root, IReadOnlyList<LibraryIndexEntry> entries, bool verify, CancellationToken token = default) =>
        matcher.MatchAsync(root, entries, new DatMatchOptions(verify), token).GetAwaiter().GetResult();

    private static DatMatchResult ResultFor(DatMatchReport report, string relative) =>
        report.Results.Single(r => string.Equals(r.RelativePath, relative, StringComparison.OrdinalIgnoreCase));

    private static void TestDatMatcherNameAndSizeEvidenceOnly(string workDirectory)
    {
        byte[] a = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        byte[] b = Enumerable.Range(0, 64).Select(i => (byte)(i + 1)).ToArray();
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "match-name",
            ("Game A (USA).bin", a),
            ("Game B.iso", b),
            ("Game C.chd", BuildChdHeader(version: 5)),
            ("Other.chd", BuildChdHeader(version: 5)),
            ("Unknown.nes", [1, 2, 3]),
            ("WrongSize.bin", [1, 2, 3]),
            ("Dup.bin", [9]),
            ("Disc.pkg", [0x7F, 0x50, 0x4B, 0x47]));

        string dat = WriteDat(Path.Combine(workDirectory, "match-name-dats"), "p.dat", "Sony - PlayStation",
            new DatGameSpec("Game A (USA)", "USA", new DatRomSpec("Game A (USA).bin", 100, Sha1: Sha1Of(a))),
            new DatGameSpec("Game B", null, new DatRomSpec("Game B.iso", 64)),
            new DatGameSpec("Game C", null, new DatRomSpec("Game C.bin", 10)),
            new DatGameSpec("Wrong", null, new DatRomSpec("WrongSize.bin", 999)),
            new DatGameSpec("Dup Game 1", null, new DatRomSpec("Dup.bin", 1)),
            new DatGameSpec("Dup Game 2", null, new DatRomSpec("Dup.bin", 1)),
            new DatGameSpec("Game A (USA)", "USA", new DatRomSpec("Game A (USA).bin", 100, Sha1: Sha1Of(a))));

        int hashCalls = 0;
        var matcher = new DatMatcher([ParseDatOrFail(dat)], (_, _, _) =>
        {
            hashCalls++;
            throw new InvalidOperationException("Hashing must not run without opt-in.");
        });
        Dictionary<string, string> before = SnapshotTree(lib.Root);
        DatMatchReport report = RunMatch(matcher, lib.Root, lib.Entries, verify: false);

        AssertEqual(0, hashCalls, "No hashing without explicit opt-in.");
        AssertEqual(DatMatchOutcome.Completed, report.Outcome, "Matching completes.");

        DatMatchResult candidate = ResultFor(report, "Game A (USA).bin");
        AssertEqual(DatMatchStatus.CandidateMatch, candidate.Status, "Name+size alone is only a candidate, and the duplicate catalog record must not cause ambiguity.");
        AssertEqual("Sony - PlayStation", candidate.Platform, "Platform comes from the DAT header.");
        AssertEqual("USA", candidate.Region, "Region from the catalog.");
        AssertTrue(candidate.Evidence.Contains(DatEvidence.NameExact) && candidate.Evidence.Contains(DatEvidence.SizeEqual), "Evidence lists name and size.");
        AssertTrue(candidate.Reasons.Contains("content-not-verified"), "Reason must say content is unverified.");

        AssertEqual(DatMatchStatus.CandidateMatch, ResultFor(report, "Game B.iso").Status, "ISO candidate.");
        DatMatchResult chd = ResultFor(report, "Game C.chd");
        AssertEqual(DatMatchStatus.CandidateMatch, chd.Status, "CHD named like a catalog game is a candidate only.");
        AssertTrue(chd.Reasons.Contains("content-verification-unsupported"), "CHD must be marked unverifiable.");
        AssertEqual(DatMatchStatus.Unsupported, ResultFor(report, "Other.chd").Status, "Unnamed CHD cannot be evaluated by content.");
        AssertEqual(DatMatchStatus.Unsupported, ResultFor(report, "Disc.pkg").Status, "Unnamed PKG cannot be evaluated by content.");
        AssertEqual(DatMatchStatus.NotFound, ResultFor(report, "Unknown.nes").Status, "No catalog record means NotFound.");
        AssertEqual(DatMatchStatus.Mismatch, ResultFor(report, "WrongSize.bin").Status, "Name match with conflicting size is a Mismatch.");
        DatMatchResult ambiguous = ResultFor(report, "Dup.bin");
        AssertEqual(DatMatchStatus.Ambiguous, ambiguous.Status, "Same ROM name in two games is ambiguous.");
        AssertEqual(2, ambiguous.AlternativeGames.Count, "Both plausible games are listed.");

        var after = SnapshotTree(lib.Root);
        AssertTrue(before.Count == after.Count && before.All(p => after[p.Key] == p.Value), "Matching must not modify the library.");
    }

    private static void TestDatMatcherVerifiesOnlyWhenRequested(string workDirectory)
    {
        byte[] a = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        byte[] tampered = a.ToArray();
        tampered[10] ^= 0xFF;
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "match-verify",
            ("renamed.bin", a),
            ("Tampered.bin", tampered),
            ("Game C.chd", BuildChdHeader(version: 5)),
            ("Nothing.bin", [5, 5, 5]));

        string dat = WriteDat(Path.Combine(workDirectory, "match-verify-dats"), "p.dat", "Sony - PlayStation",
            new DatGameSpec("Game A", "Europe", new DatRomSpec("Tampered.bin", 100, Sha1: Sha1Of(a))),
            new DatGameSpec("Game C", null, new DatRomSpec("Game C.bin", 10, Sha1: new string('c', 40))));

        List<string> hashed = [];
        DatContentHasher counting = async (path, kind, token) =>
        {
            hashed.Add(Path.GetFileName(path));
            return await DatHashing.Hasher(path, kind, token);
        };

        var matcher = new DatMatcher([ParseDatOrFail(dat)], counting);
        DatMatchReport report = RunMatch(matcher, lib.Root, lib.Entries, verify: true);

        DatMatchResult verified = ResultFor(report, "renamed.bin");
        AssertEqual(DatMatchStatus.VerifiedMatch, verified.Status, "Streamed SHA1 equal to a catalog record verifies despite the file name.");
        AssertEqual("Game A", verified.GameName, "Identity comes from the hash, not the name.");
        AssertTrue(verified.Evidence.Contains(DatEvidence.Sha1Equal) && verified.Evidence.Contains(DatEvidence.SizeEqual), "Hash and size evidence.");
        AssertFalse(verified.Evidence.Contains(DatEvidence.NameExact), "Renamed file has no name evidence.");
        AssertTrue(verified.Reasons.Contains("filename-differs-from-catalog"), "Name difference is reported.");
        AssertEqual("Europe", verified.Region, "Catalog region carried through.");

        AssertEqual(DatMatchStatus.Mismatch, ResultFor(report, "Tampered.bin").Status, "Right name, wrong content is a Mismatch.");
        AssertEqual(DatMatchStatus.NotFound, ResultFor(report, "Nothing.bin").Status, "Unknown content is NotFound.");
        AssertTrue(report.Results.Count(r => r.Status == DatMatchStatus.VerifiedMatch) == 1, "Exactly one verified result.");
        AssertFalse(hashed.Contains("Game C.chd"), "Compressed containers must never be hashed or compared with raw hashes.");
        AssertEqual(DatMatchStatus.CandidateMatch, ResultFor(report, "Game C.chd").Status, "CHD stays a name-only candidate even with verification on.");

        // A catalog without any hash cannot be verified: stay a candidate and say why.
        string noHashDat = WriteDat(Path.Combine(workDirectory, "match-verify-dats"), "nohash.dat", "X",
            new DatGameSpec("No Hash", null, new DatRomSpec("renamed.bin", 100)));
        DatMatchResult noHash = ResultFor(RunMatch(new DatMatcher([ParseDatOrFail(noHashDat)], counting), lib.Root, lib.Entries, verify: true), "renamed.bin");
        AssertEqual(DatMatchStatus.CandidateMatch, noHash.Status, "A hash-less catalog must not claim verification.");
        AssertTrue(noHash.Reasons.Contains("verification-unavailable-no-hashes-in-catalog"), "Reason explains the gap.");

        // Same content recorded under two different games stays ambiguous even though the hash matches.
        string twoGames = WriteDat(Path.Combine(workDirectory, "match-verify-dats"), "two.dat", "X",
            new DatGameSpec("First", null, new DatRomSpec("x.bin", 100, Sha1: Sha1Of(a))),
            new DatGameSpec("Second", null, new DatRomSpec("y.bin", 100, Sha1: Sha1Of(a))));
        AssertEqual(DatMatchStatus.Ambiguous, ResultFor(RunMatch(new DatMatcher([ParseDatOrFail(twoGames)], DatHashing.Hasher), lib.Root, lib.Entries, verify: true), "renamed.bin").Status,
            "One hash, two games: Ambiguous.");

        // The same hash in two catalogs with different platforms is also ambiguous.
        string catA = WriteDat(Path.Combine(workDirectory, "match-verify-dats"), "ca.dat", "Platform A", new DatGameSpec("G", null, new DatRomSpec("g.bin", 100, Sha1: Sha1Of(a))));
        string catB = WriteDat(Path.Combine(workDirectory, "match-verify-dats"), "cb.dat", "Platform B", new DatGameSpec("G", null, new DatRomSpec("g.bin", 100, Sha1: Sha1Of(a))));
        AssertEqual(DatMatchStatus.Ambiguous, ResultFor(RunMatch(new DatMatcher([ParseDatOrFail(catA), ParseDatOrFail(catB)], DatHashing.Hasher), lib.Root, lib.Entries, verify: true), "renamed.bin").Status,
            "Cross-catalog disagreement must stay ambiguous.");

        // An MD5-only catalog verifies through MD5.
        string md5 = Convert.ToHexString(MD5.HashData(a)).ToLowerInvariant();
        string md5Dat = WriteDat(Path.Combine(workDirectory, "match-verify-dats"), "md5.dat", "X", new DatGameSpec("M", null, new DatRomSpec("m.bin", 100, Md5: md5)));
        DatMatchResult viaMd5 = ResultFor(RunMatch(new DatMatcher([ParseDatOrFail(md5Dat)], DatHashing.Hasher), lib.Root, lib.Entries, verify: true), "renamed.bin");
        AssertEqual(DatMatchStatus.VerifiedMatch, viaMd5.Status, "MD5-only catalog verifies through MD5.");
        AssertTrue(viaMd5.Evidence.Contains(DatEvidence.Md5Equal), "MD5 evidence recorded.");
    }

    private static void TestDatMatcherCancellationAndErrors(string workDirectory)
    {
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "match-cancel",
            ("a.bin", [1]), ("b.bin", [2]), ("c.bin", [3]));
        string dat = WriteDat(Path.Combine(workDirectory, "match-cancel-dats"), "p.dat", "X",
            new DatGameSpec("G", null, new DatRomSpec("a.bin", 1, Sha1: new string('1', 40))));

        using var cts = new CancellationTokenSource();
        int calls = 0;
        var matcher = new DatMatcher([ParseDatOrFail(dat)], (path, kind, token) =>
        {
            if (++calls == 2)
            {
                cts.Cancel();
            }

            token.ThrowIfCancellationRequested();
            return Task.FromResult(new string('f', 40));
        });
        DatMatchReport cancelled = RunMatch(matcher, lib.Root, lib.Entries, verify: true, cts.Token);
        AssertEqual(DatMatchOutcome.Cancelled, cancelled.Outcome, "Cancellation must be reported.");
        AssertEqual(1, cancelled.Results.Count, "Results completed before cancellation are kept.");

        File.Delete(Path.Combine(lib.Root, "b.bin"));
        DatMatchReport errors = RunMatch(new DatMatcher([ParseDatOrFail(dat)], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);
        AssertEqual(DatMatchStatus.Error, ResultFor(errors, "b.bin").Status, "A vanished file is an Error, not a silent skip.");
        AssertTrue(ResultFor(errors, "b.bin").Reasons.Contains("hash-read-failed"), "Error carries a reason code.");

        LibraryIndexEntry escape = new(Path.Combine("..", "outside.bin"), "outside.bin", ".bin", MediaInputKind.BIN, 1, LibraryIndexEntryStatus.Indexed, MediaInputProbeStatus.NotRequired);
        DatMatchReport traversal = RunMatch(new DatMatcher([ParseDatOrFail(dat)], DatHashing.Hasher), lib.Root, [escape], verify: true);
        AssertEqual(DatMatchStatus.Error, traversal.Results.Single().Status, "Entries escaping the library root are rejected.");
    }
}
