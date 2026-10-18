using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static string WriteText(string directory, string name, string text)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static void TestDatParserReadsClrMameProText(string workDirectory)
    {
        string dir = Path.Combine(workDirectory, "dat-cmpro");
        string full = WriteText(dir, "full.dat",
            "clrmamepro (\n\tname \"Sony - PlayStation\"\n\tdescription \"Sony - PlayStation\"\n\tversion 20260101\n\tauthor \"someone\"\n)\n\n" +
            "game (\n\tname \"Game (USA) (Rev 1)\"\n\tdescription \"Game (USA) (Rev 1)\"\n\tyear 1999\n\tsample ( name x )\n" +
            "\trelease ( name \"Game\" region \"USA\" )\n" +
            "\trom ( name \"Game (USA) (Rev 1).bin\" size 100 crc ABCDEF12 md5 " + new string('B', 32) + " sha1 " + new string('C', 40) + " )\n" +
            "\trom ( name \"Game (USA) (Rev 1).cue\" size 50 )\n)\n" +
            "machine (\n\tname \"Machine One\"\n\trom ( name m.bin size 1 crc 00000001 )\n)\n");
        byte[] before = File.ReadAllBytes(full);
        DatParseResult result = DatParser.Parse(full, CancellationToken.None);

        AssertEqual(DatParseStatus.Parsed, result.Status, "ClrMamePro text must parse: " + string.Join(";", result.Issues.Select(i => i.Code)));
        AssertEqual(0, result.RejectedRecords, "Clean DAT rejects nothing.");
        DatFile dat = result.Dat!;
        AssertEqual("Sony - PlayStation", dat.Platform, "Header name is the platform.");
        AssertEqual("20260101", dat.Header.Version, "Header version preserved.");
        AssertEqual(2, dat.Games.Count, "game and machine blocks are both read.");
        DatGame game = dat.Games[0];
        AssertEqual("Game (USA) (Rev 1)", game.Name, "Parentheses inside quotes are data.");
        AssertEqual("USA", game.Region, "Region comes from the release block.");
        AssertEqual(2, game.Roms.Count, "ROM blocks preserved.");
        AssertEqual("abcdef12", game.Roms[0].Crc32, "CRC32 lowercased.");
        AssertEqual(new string('c', 40), game.Roms[0].Sha1, "SHA1 lowercased.");
        AssertEqual(100L, game.Roms[0].Size, "Size preserved.");
        AssertEqual("m.bin", dat.Games[1].Roms[0].Name, "Bare-word values are read.");
        AssertTrue(before.SequenceEqual(File.ReadAllBytes(full)), "Parsing must not modify the DAT.");

        // libretro hash-only style: no game name, only a comment; the ROM has a CRC32 and nothing else.
        string libretro = WriteText(dir, "libretro.dat",
            "clrmamepro (\n\tname \"Nintendo - Super Nintendo Entertainment System\"\n)\n\n" +
            "game (\n\tcomment \"'96 Zenkoku Koukou Soccer Senshuken (Japan)\"\n\tgenre \"Sports\"\n\trom ( crc 05FBB855 )\n)\n");
        DatFile hashOnly = ParseDatOrFail(libretro);
        AssertEqual("'96 Zenkoku Koukou Soccer Senshuken (Japan)", hashOnly.Games[0].Name, "comment is the game name when name is absent.");
        AssertEqual("", hashOnly.Games[0].Roms[0].Name, "A hash-only ROM stays nameless; no name is invented.");
        AssertEqual("05fbb855", hashOnly.Games[0].Roms[0].Crc32, "CRC32 kept.");
        AssertEqual(null, hashOnly.Games[0].Roms[0].Size, "No size is invented.");

        string bad = WriteText(dir, "bad.dat",
            "game (\n\tname \"Ok\"\n\trom ( name ok.bin size 1 crc 00000000 )\n\trom ( name badcrc.bin crc XYZ )\n\trom ( name badsize.bin size -1 )\n\trom ( size 5 )\n)\n" +
            "game (\n\tgenre \"no name or comment\"\n\trom ( crc 11111111 )\n)\n");
        DatParseResult badResult = DatParser.Parse(bad, CancellationToken.None);
        AssertEqual(DatParseStatus.Parsed, badResult.Status, "Bad records are rejected individually.");
        AssertEqual(4, badResult.RejectedRecords, "Three bad ROMs and one unnamed game.");
        AssertEqual(1, badResult.Dat!.Games.Count, "Only the named game remains.");
        AssertEqual(1, badResult.Dat.Games[0].Roms.Count, "Only the valid ROM remains.");

        string[] malformed =
        [
            "game (\n\tname \"unterminated\n)",
            "game (\n\tname x\n\trom ( name y",
            "game ( a ( b ( c ( d ( e ( f ( ) ) ) ) ) ) )",
            "game (\n\tname \"" + new string('x', 5000) + "\"\n)",
            "game ( ) )"
        ];

        for (int i = 0; i < malformed.Length; i++)
        {
            string path = WriteText(dir, $"malformed{i}.dat", malformed[i]);
            AssertEqual(DatParseStatus.Malformed, DatParser.Parse(path, CancellationToken.None).Status, "Malformed text DAT #" + i + " must be rejected.");
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertEqual(DatParseStatus.Cancelled, DatParser.Parse(full, cancelled.Token).Status, "Pre-cancelled parse is Cancelled.");
    }

    private static void TestDatParserOptionalAttributesAndStatus(string workDirectory)
    {
        // Real No-Intro/Redump XML carries extra attributes (status, serial, header) and <release> children.
        string path = WriteText(Path.Combine(workDirectory, "dat-xml-extra"), "extra.dat",
            "<?xml version=\"1.0\"?><!DOCTYPE datafile PUBLIC \"-//Logiqx//DTD ROM Management Datafile//EN\" \"http://www.logiqx.com/Dats/datafile.dtd\">" +
            "<datafile><header><name>Nintendo - SNES</name><clrmamepro forcenodump=\"ignore\"/><romcenter plugin=\"x\"/></header>" +
            "<game name=\"A (USA)\" id=\"0001\" cloneof=\"B\"><category>Games</category><description>A (USA)</description>" +
            "<release name=\"A\" region=\"USA\"/><release name=\"A\" region=\"EUR\"/>" +
            "<rom name=\"A (USA).sfc\" size=\"524288\" crc=\"B22C9747\" md5=\"14758F1AFD44C09B7992073CCF00B43D\" sha1=\"988881ADC9FC3655077DC2D4D757D480B5EA0E11\" status=\"verified\" serial=\"SNS-XX\"/>" +
            "</game><machine name=\"M\"><rom name=\"m.bin\" size=\"1\" crc=\"00000001\"/></machine></datafile>");
        DatParseResult result = DatParser.Parse(path, CancellationToken.None);
        AssertEqual(DatParseStatus.Parsed, result.Status, "Extra header children and ROM attributes must not break parsing.");
        AssertEqual("Nintendo - SNES", result.Dat!.Platform, "Header name read despite nested clrmamepro/romcenter elements.");
        DatGame game = result.Dat.Games[0];
        AssertEqual("USA", game.Region, "The first <release> region is kept.");
        AssertEqual("B", game.CloneOf, "cloneof preserved.");
        AssertEqual("b22c9747", game.Roms[0].Crc32, "Uppercase hashes are normalized.");
        AssertEqual(2, result.Dat.Games.Count, "machine elements are games.");

        // A game that lists the same ROM twice keeps both records (a catalog may do so); matching treats them as one game.
        string dup = WriteText(Path.Combine(workDirectory, "dat-xml-extra"), "dup.dat",
            "<datafile><header><name>X</name></header><game name=\"G\"><rom name=\"g.bin\" size=\"1\" crc=\"00000001\"/><rom name=\"g.bin\" size=\"1\" crc=\"00000001\"/></game></datafile>");
        AssertEqual(2, ParseDatOrFail(dup).Games[0].Roms.Count, "Duplicate records are preserved, not silently dropped.");
    }

    private static uint BitwiseCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            crc ^= b;

            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void TestCrc32StreamingAndExistingHashes(string workDirectory)
    {
        string dir = Path.Combine(workDirectory, "crc32");
        Directory.CreateDirectory(dir);

        AssertEqual("cbf43926", Crc32Streaming.ToHex(Crc32Streaming.Update(0, Encoding.ASCII.GetBytes("123456789"))), "Standard CRC-32 check value.");
        AssertEqual("00000000", Crc32Streaming.ToHex(Crc32Streaming.Update(0, [])), "CRC-32 of nothing.");
        AssertEqual("e8b7be43", Crc32Streaming.ToHex(Crc32Streaming.Update(0, "a"u8)), "CRC-32 of 'a'.");
        AssertEqual("414fa339", Crc32Streaming.ToHex(Crc32Streaming.Update(0, "The quick brown fox jumps over the lazy dog"u8)), "CRC-32 of the pangram.");

        byte[] data = new byte[3 * 1024 * 1024 + 123];
        new Random(1234).NextBytes(data);
        uint expected = BitwiseCrc32(data);
        uint chunked = 0;

        for (int offset = 0; offset < data.Length; offset += 77_777)
        {
            chunked = Crc32Streaming.Update(chunked, data.AsSpan(offset, Math.Min(77_777, data.Length - offset)));
        }

        AssertEqual(expected, chunked, "Chunked updates must equal a one-shot CRC.");

        string path = Path.Combine(dir, "big.bin");
        File.WriteAllBytes(path, data);
        string streamed = Crc32Streaming.ComputeAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(Crc32Streaming.ToHex(expected), streamed, "Streaming a file larger than its buffer must match the reference CRC.");
        AssertEqual(streamed, DatHashing.Hasher(path, DatHashKind.Crc32, CancellationToken.None).GetAwaiter().GetResult(), "The matcher's hasher routes CRC32 to the streaming implementation.");

        // SHA1 and MD5 keep going through the existing service and stay correct.
        AssertEqual(Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant(), DatHashing.Hasher(path, DatHashKind.Sha1, CancellationToken.None).GetAwaiter().GetResult(), "SHA1 unchanged.");
        AssertEqual(Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant(), DatHashing.Hasher(path, DatHashKind.Md5, CancellationToken.None).GetAwaiter().GetResult(), "MD5 unchanged.");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertThrows<OperationCanceledException>(
            () => Crc32Streaming.ComputeAsync(path, cancelled.Token).GetAwaiter().GetResult(),
            "A cancelled CRC32 must stop.");
        AssertThrows<FileNotFoundException>(
            () => Crc32Streaming.ComputeAsync(Path.Combine(dir, "missing.bin"), CancellationToken.None).GetAwaiter().GetResult(),
            "A missing file must surface as an error.");
        AssertTrue(data.SequenceEqual(File.ReadAllBytes(path)), "Hashing must not modify the file.");
    }

    private static void TestDatMatcherCrc32Rules(string workDirectory)
    {
        byte[] data = Enumerable.Range(0, 100).Select(i => (byte)(i * 3)).ToArray();
        byte[] other = data.ToArray();
        other[0] ^= 0xFF;
        string crc = Crc32Streaming.ToHex(BitwiseCrc32(data));
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "match-crc",
            ("renamed.bin", data),
            ("Other.bin", other),
            ("Box.chd", BuildChdHeader(version: 5)));
        string dats = Path.Combine(workDirectory, "match-crc-dats");

        List<(string File, DatHashKind Kind)> hashed = [];
        DatContentHasher recording = async (path, kind, token) =>
        {
            hashed.Add((Path.GetFileName(path), kind));
            return await DatHashing.Hasher(path, kind, token);
        };

        // CRC32 plus an agreeing size identifies the file.
        DatFile withSize = ParseDatOrFail(WriteDat(dats, "size.dat", "X", new DatGameSpec("Game", null, new DatRomSpec("x.bin", 100, Crc: crc)), new DatGameSpec("Box", null, new DatRomSpec("Box.bin", 5, Crc: crc))));
        DatMatchReport report = RunMatch(new DatMatcher([withSize], recording), lib.Root, lib.Entries, verify: true);
        DatMatchResult identified = ResultFor(report, "renamed.bin");
        AssertEqual(DatMatchStatus.VerifiedMatch, identified.Status, "CRC32 + size is accepted identification.");
        AssertTrue(identified.Evidence.Contains(DatEvidence.Crc32Equal) && identified.Evidence.Contains(DatEvidence.SizeEqual), "CRC32 and size evidence.");
        AssertTrue(identified.Reasons.Contains("crc32-identification"), "The reason says it is CRC32-grade evidence.");
        AssertEqual(DatMatchStatus.NotFound, ResultFor(report, "Other.bin").Status, "Different content is not found.");
        AssertTrue(hashed.All(h => h.Kind == DatHashKind.Crc32 && h.File != "Box.chd"), "CRC32 is the only hash used and the CHD is never hashed.");
        AssertEqual(DatMatchStatus.CandidateMatch, ResultFor(report, "Box.chd").Status, "A CHD named like a game stays a name-only candidate; its hash is not compared with raw CRCs.");

        // A CRC-only record (no size) is only a candidate.
        string libretroPath = WriteText(dats, "libretro.dat", "clrmamepro (\n\tname \"Y\"\n)\ngame (\n\tcomment \"Hash Only\"\n\trom ( crc " + crc.ToUpperInvariant() + " )\n)\n");
        DatFile noSize = ParseDatOrFail(libretroPath);
        DatMatchResult weak = ResultFor(RunMatch(new DatMatcher([noSize], DatHashing.Hasher), lib.Root, lib.Entries, verify: true), "renamed.bin");
        AssertEqual(DatMatchStatus.CandidateMatch, weak.Status, "CRC32 without a catalog size is only a candidate.");
        AssertEqual("Hash Only", weak.GameName, "Game identity comes from the hash-only record.");
        AssertTrue(weak.Reasons.Contains("crc32-without-catalog-size"), "Reason explains the weakness.");

        // Size conflict: same CRC, different catalog size is not a verified match.
        DatFile conflict = ParseDatOrFail(WriteDat(dats, "conflict.dat", "X", new DatGameSpec("Game", null, new DatRomSpec("x.bin", 5, Crc: crc))));
        AssertTrue(ResultFor(RunMatch(new DatMatcher([conflict], DatHashing.Hasher), lib.Root, lib.Entries, verify: true), "renamed.bin").Status != DatMatchStatus.VerifiedMatch, "CRC equal with conflicting size must not verify.");

        // The same CRC+size under two games is ambiguous.
        DatFile two = ParseDatOrFail(WriteDat(dats, "two.dat", "X", new DatGameSpec("One", null, new DatRomSpec("a.bin", 100, Crc: crc)), new DatGameSpec("Two", null, new DatRomSpec("b.bin", 100, Crc: crc))));
        DatMatchResult ambiguous = ResultFor(RunMatch(new DatMatcher([two], DatHashing.Hasher), lib.Root, lib.Entries, verify: true), "renamed.bin");
        AssertEqual(DatMatchStatus.Ambiguous, ambiguous.Status, "Identical CRC+size in two games is ambiguous.");

        // A catalog with SHA1 prefers SHA1 over CRC32 and still works.
        hashed.Clear();
        DatFile all = ParseDatOrFail(WriteDat(dats, "all.dat", "X", new DatGameSpec("Game", null, new DatRomSpec("x.bin", 100, Sha1: Sha1Of(data), Crc: crc))));
        DatMatchResult strong = ResultFor(RunMatch(new DatMatcher([all], recording), lib.Root, lib.Entries, verify: true), "renamed.bin");
        AssertEqual(DatMatchStatus.VerifiedMatch, strong.Status, "SHA1 verification is unchanged.");
        AssertTrue(strong.Evidence.Contains(DatEvidence.Sha1Equal) && hashed.All(h => h.Kind == DatHashKind.Sha1), "SHA1 is preferred when the catalog has it.");

        // Indexing and default matching never hash.
        hashed.Clear();
        _ = RunMatch(new DatMatcher([all], recording), lib.Root, lib.Entries, verify: false);
        AssertEqual(0, hashed.Count, "No hashing without VerifyContent.");
    }

    private static void TestPlannerDiscSetHandling(string workDirectory)
    {
        byte[] eBin = [1, 2, 3, 4];
        string eCue = "FILE \"E (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n";
        byte[] fBin = [5, 5, 5];
        string fCue = "FILE \"F.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n";
        string gdi = "3\r\n1 0 4 2352 \"track01.bin\" 0\r\n2 756 0 2352 \"track02.raw\" 0\r\n3 45000 4 2352 \"track03.bin\" 0\r\n";
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-sets-2",
            ("a\\Rewrite.cue", Encoding.ASCII.GetBytes("FILE \"sub\\t1.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n")),
            ("a\\sub\\t1.bin", [9, 9]),
            ("b\\Missing.cue", Encoding.ASCII.GetBytes("FILE \"m1.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\nFILE \"m2.bin\" BINARY\r\n  TRACK 02 AUDIO\r\n")),
            ("b\\m1.bin", [8]),
            ("c\\disc.gdi", Encoding.ASCII.GetBytes(gdi)),
            ("c\\track01.bin", [1]),
            ("c\\track02.raw", [2]),
            ("c\\track03.bin", [3]),
            ("d\\One.cue", Encoding.ASCII.GetBytes("FILE \"shared.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n")),
            ("d\\Two.cue", Encoding.ASCII.GetBytes("FILE \"shared.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n")),
            ("d\\shared.bin", [7]),
            ("e\\E.cue", Encoding.ASCII.GetBytes(eCue)),
            ("e\\E (Track 1).bin", eBin),
            ("f\\F.cue", Encoding.ASCII.GetBytes(fCue)),
            ("f\\F.bin", fBin));

        DatFile dat = ParseDatOrFail(WriteDat(Path.Combine(workDirectory, "plan-sets-2-dats"), "p.dat", "Sony - PlayStation",
            new DatGameSpec("E", "USA", new DatRomSpec("E.cue", (long)eCue.Length, Sha1: Sha1Of(Encoding.ASCII.GetBytes(eCue))), new DatRomSpec("E (Track 1).bin", 4, Sha1: Sha1Of(eBin))),
            new DatGameSpec("F", null, new DatRomSpec("F.cue", (long)fCue.Length, Sha1: Sha1Of(Encoding.ASCII.GetBytes(fCue))), new DatRomSpec("F.bin", 3, Sha1: new string('9', 40))),
            new DatGameSpec("Disc", null, new DatRomSpec("track01.bin", 1, Sha1: Sha1Of([1])))));
        DatMatchReport report = RunMatch(new DatMatcher([dat], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);
        Dictionary<string, string> before = SnapshotTree(lib.Root);
        OrganizationPlan plan = PlanFor(lib.Root, Path.Combine(workDirectory, "plan-sets-2-out"), lib.Entries, report);

        // A reference through a sub-folder cannot be moved safely without rewriting the sheet.
        PlanItem rewrite = ItemFor(plan, "a\\Rewrite.cue");
        PlanItem nested = ItemFor(plan, "a\\sub\\t1.bin");
        AssertTrue(rewrite.Flags.HasFlag(PlanFlags.SheetNeedsReferenceRewrite) && nested.Flags.HasFlag(PlanFlags.SheetNeedsReferenceRewrite), "Sheet and nested track both need review.");
        AssertEqual(Path.Combine("_Review", "NeedsReferenceRewrite", "a", "Rewrite.cue"), rewrite.DestinationRelativePath, "Rewrite-needing sheet is not proposed as executable.");
        AssertEqual(Path.Combine("_Review", "NeedsReferenceRewrite", "a", "sub", "t1.bin"), nested.DestinationRelativePath, "The nested track stays with its sheet and keeps its relative layout.");

        // A missing referenced file leaves the set incomplete; every member goes to review together.
        PlanItem missing = ItemFor(plan, "b\\Missing.cue");
        PlanItem m1 = ItemFor(plan, "b\\m1.bin");
        AssertTrue(missing.Flags.HasFlag(PlanFlags.IncompleteDiscSet) && m1.Flags.HasFlag(PlanFlags.IncompleteDiscSet), "Incomplete set flagged on all members.");
        AssertEqual(Path.Combine("_Review", "IncompleteDiscSet", "b", "m1.bin"), m1.DestinationRelativePath, "Members share the review reason.");

        // GDI: sheet and RAW/BIN tracks are one set; the verified track lends its identity, the rest are unverified.
        PlanItem[] gdiItems = new[] {"c\\disc.gdi", "c\\track01.bin", "c\\track02.raw", "c\\track03.bin"}.Select(s => ItemFor(plan, s)).ToArray();
        AssertEqual(1, gdiItems.Select(i => i.DiscSetId).Distinct().Count(), "All GDI files share one set id.");
        AssertTrue(gdiItems.All(i => i.Bucket == PlanBucket.Platform && i.DestinationRelativePath.StartsWith(Path.Combine("Sony - PlayStation", "Disc") + Path.DirectorySeparatorChar)), "The GDI set lands in one game folder.");
        AssertTrue(gdiItems.All(i => i.Flags.HasFlag(PlanFlags.PartiallyVerifiedSet) && !i.IsSafeToApply), "A partially verified set is not applied as a unit.");
        AssertEqual(DatMatchStatus.VerifiedMatch, ItemFor(plan, "c\\track01.bin").MatchStatus, "The verified track stays verified.");
        AssertEqual(DatMatchStatus.CandidateMatch, ItemFor(plan, "c\\track02.raw").MatchStatus, "Unverified tracks inherit as candidates only.");

        // A track referenced by two sheets makes both sets inconsistent.
        AssertTrue(new[] { "d\\One.cue", "d\\Two.cue", "d\\shared.bin" }.All(s => ItemFor(plan, s).Flags.HasFlag(PlanFlags.InconsistentDiscSet)), "A shared track flags both sets.");

        // A fully verified set is safe to apply as a unit.
        PlanItem[] verified = new[] { "e\\E.cue", "e\\E (Track 1).bin" }.Select(s => ItemFor(plan, s)).ToArray();
        AssertTrue(verified.All(i => i.IsSafeToApply), "Fully verified, flag-free set is safe.");
        AssertTrue(verified.All(i => i.DestinationRelativePath.StartsWith(Path.Combine("Sony - PlayStation", "E") + Path.DirectorySeparatorChar)), "Verified set shares a game folder.");

        // One conflicting member sends the whole set to the same review folder.
        PlanItem fCuePlan = ItemFor(plan, "f\\F.cue");
        PlanItem fBinPlan = ItemFor(plan, "f\\F.bin");
        AssertEqual(DatMatchStatus.VerifiedMatch, fCuePlan.MatchStatus, "Fixture: the sheet verifies.");
        AssertEqual(DatMatchStatus.Mismatch, fBinPlan.MatchStatus, "Fixture: the track conflicts.");
        AssertEqual(Path.Combine("_Review", "Mismatch", "f", "F.cue"), fCuePlan.DestinationRelativePath, "A verified sheet follows its mismatching track into review.");
        AssertEqual(Path.Combine("_Review", "Mismatch", "f", "F.bin"), fBinPlan.DestinationRelativePath, "The set is never split.");

        // Sheets are never rewritten and nothing moves.
        AssertTreeUnchanged(before, lib.Root, "Planning must not modify sheets or move files.");
    }

    private static void TestPlannerReportExplainsDecisions(string workDirectory)
    {
        byte[] good = [1, 2, 3];
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-report",
            ("Alpha.bin", good), ("Guess.bin", [4, 4]), ("zzz.rom", [0]));
        DatFile dat = ParseDatOrFail(WriteDat(Path.Combine(workDirectory, "plan-report-dats"), "p.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", "USA", new DatRomSpec("Alpha.bin", 3, Sha1: Sha1Of(good))),
            new DatGameSpec("Guess", null, new DatRomSpec("Guess.bin", 2))));
        DatMatchReport report = RunMatch(new DatMatcher([dat], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);
        string output = Path.Combine(workDirectory, "plan-report-out");
        string text = PlanFor(lib.Root, output, lib.Entries, report).ToReport();

        AssertTrue(text.Contains("[VerifiedMatch] Alpha.bin"), "Header per item with its status.");
        AssertTrue(text.Contains("proposed: " + Path.Combine("Sony - PlayStation", "Alpha.bin") + " (safe to apply)"), "Destination and safety.");
        AssertTrue(text.Contains("catalog: DAT 'Sony - PlayStation' | game 'Alpha' | platform 'Sony - PlayStation' | region USA"), "Catalog details.");
        AssertTrue(text.Contains("evidence: Sha1Equal"), "Evidence listed.");
        AssertTrue(text.Contains("reasons: content-hash-verified"), "Reason codes listed.");
        AssertTrue(text.Contains("[CandidateMatch] Guess.bin") && text.Contains("flag UnverifiedIdentity:"), "Flags explained in words.");
        AssertTrue(text.Contains("[NotFound] zzz.rom") && text.Contains("review reason: Unknown"), "Review reason shown.");
        AssertTrue(text.Contains("  VerifiedMatch: 1") && text.Contains("  NotFound: 1"), "Per-status summary.");
        AssertEqual(text, PlanFor(lib.Root, output, lib.Entries, report).ToReport(), "Report is deterministic.");
    }
}
