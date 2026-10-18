using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static OrganizationPlan PlanFor(string root, string output, IReadOnlyList<LibraryIndexEntry> entries, DatMatchReport report) =>
        OrganizationPlanner.Plan(root, output, entries, report.Results);

    private static PlanItem ItemFor(OrganizationPlan plan, string source) =>
        plan.Items.Single(i => string.Equals(i.SourceRelativePath, source, StringComparison.OrdinalIgnoreCase));

    private static void AssertTreeUnchanged(Dictionary<string, string> before, string root, string message)
    {
        var after = SnapshotTree(root);
        AssertTrue(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out string? v) && v == p.Value), message);
    }

    private static void TestPlannerGroupsConsolesAndKeepsUnknownSeparate(string workDirectory)
    {
        byte[] ps = [1, 2, 3, 4];
        byte[] snes = [5, 6, 7, 8, 9];
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-consoles",
            ("ps\\Alpha.bin", ps),
            ("snes\\Beta.sfc", snes),
            ("misc\\mystery.rom", [1]),
            ("misc\\guess.bin", [7, 7]),
            ("misc\\Box.chd", BuildChdHeader(version: 5)));

        string dats = Path.Combine(workDirectory, "plan-consoles-dats");
        DatFile psDat = ParseDatOrFail(WriteDat(dats, "ps.dat", "Sony - PlayStation", new DatGameSpec("Alpha", "USA", new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(ps))),
            new DatGameSpec("Guess", null, new DatRomSpec("guess.bin", 2))));
        DatFile snesDat = ParseDatOrFail(WriteDat(dats, "snes.dat", "Nintendo - Super Nintendo", new DatGameSpec("Beta", null, new DatRomSpec("Beta.sfc", 5, Sha1: Sha1Of(snes)))));
        DatMatchReport report = RunMatch(new DatMatcher([psDat, snesDat], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);

        string output = Path.Combine(workDirectory, "plan-consoles-out");
        Dictionary<string, string> before = SnapshotTree(lib.Root);
        OrganizationPlan plan = PlanFor(lib.Root, output, lib.Entries, report);

        AssertEqual(PlanStatus.Ready, plan.Status, "Plan must be ready.");
        PlanItem alpha = ItemFor(plan, Path.Combine("ps", "Alpha.bin"));
        AssertEqual(Path.Combine("Sony - PlayStation", "Alpha.bin"), alpha.DestinationRelativePath, "Verified PlayStation file goes to its platform folder.");
        AssertTrue(alpha.IsSafeToApply, "Verified, unflagged item is safe to apply.");
        AssertEqual(Path.Combine("Nintendo - Super Nintendo", "Beta.sfc"), ItemFor(plan, Path.Combine("snes", "Beta.sfc")).DestinationRelativePath, "Second console gets its own folder.");

        PlanItem unknown = ItemFor(plan, Path.Combine("misc", "mystery.rom"));
        AssertEqual(PlanBucket.Review, unknown.Bucket, "Unknown media stays out of platform folders.");
        AssertEqual(Path.Combine("_Review", "Unknown", "misc", "mystery.rom"), unknown.DestinationRelativePath, "Review keeps the original relative path.");
        AssertFalse(unknown.IsSafeToApply, "Unknown is never safe.");

        PlanItem candidate = ItemFor(plan, Path.Combine("misc", "guess.bin"));
        AssertEqual(DatMatchStatus.CandidateMatch, candidate.MatchStatus, "Name+size only.");
        AssertTrue(candidate.Flags.HasFlag(PlanFlags.UnverifiedIdentity), "Unverified identity is flagged.");
        AssertFalse(candidate.IsSafeToApply, "Candidates are not safe to apply.");

        PlanItem container = ItemFor(plan, Path.Combine("misc", "Box.chd"));
        AssertEqual(PlanBucket.Review, container.Bucket, "Unevaluable container goes to review.");
        AssertTrue(container.Flags.HasFlag(PlanFlags.ContentUnverifiable), "Unsupported verification is flagged.");

        AssertTrue(plan.ToReport().Contains("no files were changed"), "Report states it is a preview.");
        AssertEqual(plan.ToReport(), PlanFor(lib.Root, output, lib.Entries, report).ToReport(), "Plans must be deterministic.");
        AssertFalse(Directory.Exists(output), "Planning must not create the output folder.");
        AssertTreeUnchanged(before, lib.Root, "Planning must not touch the library.");
    }

    private static void TestPlannerKeepsDiscSetsTogether(string workDirectory)
    {
        byte[] t1 = [1, 1, 1];
        byte[] t2 = [2, 2, 2];
        string cue = "FILE \"Disc (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\nFILE \"Disc (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n";
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-sets",
            ("d\\Disc.cue", Encoding.ASCII.GetBytes(cue)),
            ("d\\Disc (Track 1).bin", t1),
            ("d\\Disc (Track 2).bin", t2),
            ("d\\Lonely.bin", [4, 4]));

        // Only track 1 and the cue have catalog records; track 2 has none.
        DatFile dat = ParseDatOrFail(WriteDat(Path.Combine(workDirectory, "plan-sets-dats"), "p.dat", "Sony - PlayStation",
            new DatGameSpec("Disc", "Europe",
                new DatRomSpec("Disc.cue", null),
                new DatRomSpec("Disc (Track 1).bin", 3, Sha1: Sha1Of(t1)))));
        DatMatchReport report = RunMatch(new DatMatcher([dat], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);
        AssertEqual(DatMatchStatus.NotFound, ResultFor(report, "d\\Disc (Track 2).bin").Status, "Fixture: track 2 has no record.");

        OrganizationPlan plan = PlanFor(lib.Root, Path.Combine(workDirectory, "plan-sets-out"), lib.Entries, report);
        string folder = Path.Combine("Sony - PlayStation", "Disc");
        PlanItem sheet = ItemFor(plan, "d\\Disc.cue");
        PlanItem track1 = ItemFor(plan, "d\\Disc (Track 1).bin");
        PlanItem track2 = ItemFor(plan, "d\\Disc (Track 2).bin");

        AssertEqual(Path.Combine(folder, "Disc.cue"), sheet.DestinationRelativePath, "Sheet goes into the game folder.");
        AssertEqual(Path.Combine(folder, "Disc (Track 1).bin"), track1.DestinationRelativePath, "Verified track stays with its sheet.");
        AssertEqual(Path.Combine(folder, "Disc (Track 2).bin"), track2.DestinationRelativePath, "A track without its own record follows the sheet, never splitting the disc.");
        AssertEqual(sheet.DiscSetId, track2.DiscSetId, "All members share one set id.");
        AssertTrue(track2.Flags.HasFlag(PlanFlags.UnverifiedIdentity) && !track2.IsSafeToApply, "The inherited track is flagged unverified.");
        AssertEqual(null, ItemFor(plan, "d\\Lonely.bin").DiscSetId, "Unreferenced files are not part of the set.");
        AssertEqual(PlanBucket.Review, ItemFor(plan, "d\\Lonely.bin").Bucket, "Unrelated unknown file stays in review.");
    }

    private static void TestPlannerFlagsDuplicatesCollisionsAndInvalidNames(string workDirectory)
    {
        byte[] same = [3, 3, 3];
        byte[] other = [4, 4, 4];
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-dups",
            ("a\\Game.bin", same),
            ("b\\Game.bin", same),
            ("c\\Disc.BIN", other),
            ("d\\disc.bin", [9, 9, 9, 9]),
            ("e\\Odd.bin", [8]),
            ("f\\Dev.bin", [7]));

        DatFile dat = ParseDatOrFail(WriteDat(Path.Combine(workDirectory, "plan-dups-dats"), "p.dat", "Sega - Saturn",
            new DatGameSpec("Game", null, new DatRomSpec("Game.bin", 3, Sha1: Sha1Of(same))),
            new DatGameSpec("Disc X", null, new DatRomSpec("Disc.BIN", 3, Sha1: Sha1Of(other))),
            new DatGameSpec("Disc Y", null, new DatRomSpec("disc.bin", 4, Sha1: Sha1Of([9, 9, 9, 9]))),
            new DatGameSpec("Odd", null, new DatRomSpec("Odd.bin", 1, Sha1: Sha1Of([8]))),
            new DatGameSpec("Dev", null, new DatRomSpec("Dev.bin", 1, Sha1: Sha1Of([7])))));
        DatFile badPlatform = ParseDatOrFail(WriteDat(Path.Combine(workDirectory, "plan-dups-dats"), "bad.dat", "Bad: Platform",
            new DatGameSpec("Never", null, new DatRomSpec("never.bin", 1, Sha1: new string('0', 40)))));
        DatMatchReport report = RunMatch(new DatMatcher([dat, badPlatform], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);
        Dictionary<string, string> before = SnapshotTree(lib.Root);

        OrganizationPlan plan = PlanFor(lib.Root, Path.Combine(workDirectory, "plan-dups-out"), lib.Entries, report);

        PlanItem keep = ItemFor(plan, "a\\Game.bin");
        PlanItem loser = ItemFor(plan, "b\\Game.bin");
        AssertTrue(keep.Flags.HasFlag(PlanFlags.DuplicateCandidate) && loser.Flags.HasFlag(PlanFlags.DuplicateCandidate), "Both copies are flagged.");
        AssertEqual(PlanBucket.Platform, keep.Bucket, "The first copy keeps its slot.");
        AssertEqual(Path.Combine("_Review", "Duplicates", "b", "Game.bin"), loser.DestinationRelativePath, "Later copies go to review, never deleted.");

        PlanItem x = ItemFor(plan, "c\\Disc.BIN");
        PlanItem y = ItemFor(plan, "d\\disc.bin");
        AssertTrue(x.Flags.HasFlag(PlanFlags.DestinationCollision) && y.Flags.HasFlag(PlanFlags.DestinationCollision),
            "Case-insensitive destination collisions must be flagged on both items.");
        AssertFalse(x.IsSafeToApply || y.IsSafeToApply, "Colliding items are not safe.");

        PlanItem reserved = ItemFor(plan, "f\\Dev.bin");
        AssertEqual(PlanBucket.Platform, reserved.Bucket, "Valid names stay in platform folders.");

        // A catalog platform that is not a valid Windows folder name is routed to review.
        DatMatchResult forged = new("e\\Odd.bin", DatMatchStatus.VerifiedMatch, "bad", "Bad: Platform", "Odd", "Odd.bin", null, null, [DatEvidence.Sha1Equal], [], []);
        DatMatchResult reservedGame = new("f\\Dev.bin", DatMatchStatus.VerifiedMatch, "ok", "Sega - Saturn", "CON", "Dev.bin", null, null, [DatEvidence.Sha1Equal], [], []);
        OrganizationPlan invalid = OrganizationPlanner.Plan(lib.Root, Path.Combine(workDirectory, "plan-dups-out"), lib.Entries, [forged, reservedGame]);
        PlanItem badItem = ItemFor(invalid, "e\\Odd.bin");
        AssertEqual(Path.Combine("_Review", "InvalidName", "e", "Odd.bin"), badItem.DestinationRelativePath, "Invalid platform name goes to review.");
        AssertTrue(badItem.Flags.HasFlag(PlanFlags.InvalidDestinationName), "Invalid name is flagged.");

        // Path-length limit.
        string longOutput = Path.Combine(workDirectory, "plan-dups-out", new string('x', 200));
        OrganizationPlan tooLong = PlanFor(lib.Root, longOutput, lib.Entries, report);
        AssertTrue(tooLong.Items.Any(i => i.Flags.HasFlag(PlanFlags.PathTooLong)), "Over-long destinations must be flagged.");

        AssertTreeUnchanged(before, lib.Root, "Planning must not move, rename or delete source files.");
        AssertTrue(lib.Entries.All(e => File.Exists(Path.Combine(lib.Root, e.RelativePath))), "Every source file must still exist.");
    }

    private static void TestPlannerRejectsUnsafeOutputRoots(string workDirectory)
    {
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-roots", ("g.bin", [1]));
        DatMatchReport report = RunMatch(new DatMatcher([]), lib.Root, lib.Entries, verify: false);

        string[] bad =
        [
            "",
            "   ",
            "relative\\out",
            Path.Combine(workDirectory, "..", "escape"),
            Path.GetPathRoot(workDirectory)!,
            Path.Combine(workDirectory, "Hakamiq Work", "out"),
            Path.Combine(workDirectory, ".hakamiq-pending")
        ];

        foreach (string output in bad)
        {
            OrganizationPlan plan = PlanFor(lib.Root, output, lib.Entries, report);
            AssertEqual(PlanStatus.Rejected, plan.Status, "Unsafe output root must be rejected: '" + output + "'.");
            AssertEqual(0, plan.Items.Count, "A rejected plan has no items.");
            AssertTrue(plan.Issues.Count > 0, "Rejection must say why.");
        }

        // A junction anywhere on the existing part of the path is refused.
        string real = Path.Combine(workDirectory, "plan-roots-real");
        string junction = Path.Combine(workDirectory, "plan-roots-junction");
        Directory.CreateDirectory(real);
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, Arguments = $"/c mklink /J \"{junction}\" \"{real}\"" };
        using (Process process = Process.Start(start) ?? throw new InvalidOperationException("Junction fixture could not start."))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            AssertEqual(0, process.ExitCode, "Junction fixture failed: " + output);
        }

        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, Path.Combine(junction, "out"), lib.Entries, report).Status, "Output under a junction must be rejected.");
        AssertEqual(PlanStatus.Ready, PlanFor(lib.Root, Path.Combine(workDirectory, "plan-roots-ok", "deeper"), lib.Entries, report).Status, "A not-yet-existing root under a safe ancestor is allowed.");
    }

    private static void TestPlannerDetectsDestinationConflictsWithSources(string workDirectory)
    {
        byte[] data = [2, 4, 6];
        // The library is itself the "Sega - Saturn" folder of the output tree (the output is its parent),
        // so proposed destinations can land on existing sources without the output being inside the library.
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, Path.Combine("plan-conflict", "Sega - Saturn"),
            ("Already.bin", data),
            ("Fresh\\Other.bin", [1, 1]),
            ("sub\\Taken.bin", [5]),
            ("Taken.bin", [6]));

        DatFile dat = ParseDatOrFail(WriteDat(Path.Combine(workDirectory, "plan-conflict-dats"), "p.dat", "Sega - Saturn",
            new DatGameSpec("Already", null, new DatRomSpec("Already.bin", 3, Sha1: Sha1Of(data))),
            new DatGameSpec("Other", null, new DatRomSpec("Other.bin", 2, Sha1: Sha1Of([1, 1]))),
            new DatGameSpec("Taken", null, new DatRomSpec("Taken.bin", 1, Sha1: Sha1Of([5])))));
        DatMatchReport report = RunMatch(new DatMatcher([dat], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);

        // The output is the library's parent: it must not be refused, and destinations can land on existing sources.
        string outputRoot = Path.GetDirectoryName(lib.Root)!;
        OrganizationPlan plan = PlanFor(lib.Root, outputRoot, lib.Entries, report);
        AssertEqual(PlanStatus.Ready, plan.Status, "An output that contains the library is still a separate tree and is planned.");
        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, lib.Root, lib.Entries, report).Status, "The library itself is never an output.");
        PlanItem already = ItemFor(plan, "Already.bin");
        AssertTrue(already.Flags.HasFlag(PlanFlags.DestinationConflictsWithSource), "A move onto its own source is flagged.");
        AssertTrue(already.Flags.HasFlag(PlanFlags.DestinationExists), "Existing destination is flagged.");
        AssertFalse(already.IsSafeToApply, "Conflicting items are not safe.");

        PlanItem taken = ItemFor(plan, "sub\\Taken.bin");
        AssertTrue(taken.Flags.HasFlag(PlanFlags.DestinationConflictsWithSource), "A destination occupied by another source is flagged.");
        AssertEqual("Sega - Saturn\\Taken.bin".Replace('\\', Path.DirectorySeparatorChar), taken.DestinationRelativePath, "Destination is reported, not silently changed.");
        AssertTrue(File.Exists(Path.Combine(lib.Root, "Taken.bin")) && File.Exists(Path.Combine(lib.Root, "sub", "Taken.bin")), "Nothing was moved or overwritten.");
    }

    private static void TestPlannerSurvivesOversizedSheetsAndCaseDuplicateEntries(string workDirectory)
    {
        byte[] oversized = new byte[1024 * 1024 + 16];
        Array.Fill(oversized, (byte)' ');
        Encoding.ASCII.GetBytes("FILE \"a.bin\" BINARY\r\n").CopyTo(oversized, 0);
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-bounds",
            ("big\\Huge.cue", oversized),
            ("big\\a.bin", [1]),
            ("dup\\Game.bin", [2, 2]));

        // A case-sensitive folder can yield two entries that differ only by case.
        List<LibraryIndexEntry> entries = [.. lib.Entries, lib.Entries.Single(e => e.FileName == "Game.bin") with { RelativePath = "dup\\GAME.bin", FileName = "GAME.bin" }];
        DatMatchReport report = RunMatch(new DatMatcher([], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);

        OrganizationPlan plan = PlanFor(lib.Root, Path.Combine(workDirectory, "plan-bounds-out"), entries, report);
        AssertEqual(PlanStatus.Ready, plan.Status, "Case-duplicate entries must not abort planning.");
        PlanItem huge = ItemFor(plan, "big\\Huge.cue");
        AssertTrue(huge.Flags.HasFlag(PlanFlags.IncompleteDiscSet), "A sheet over the size bound is treated as unreadable, never read whole.");
        AssertEqual(PlanBucket.Review, huge.Bucket, "An unreadable sheet goes to review.");
    }

    private static void TestPlannerFailsClosedOnCaseAmbiguousSheetReference(string workDirectory)
    {
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-case-ref",
            ("g\\Game.cue", Encoding.ASCII.GetBytes("FILE \"Track01.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n")),
            ("g\\Track01.bin", [1]));

        // A case-sensitive folder can hold a second file differing only by case.
        List<LibraryIndexEntry> entries = [.. lib.Entries, lib.Entries.Single(e => e.FileName == "Track01.bin") with { RelativePath = "g\\TRACK01.bin", FileName = "TRACK01.bin" }];
        DatMatchReport report = RunMatch(new DatMatcher([], DatHashing.Hasher), lib.Root, lib.Entries, verify: true);

        OrganizationPlan plan = PlanFor(lib.Root, Path.Combine(workDirectory, "plan-case-ref-out"), entries, report);
        PlanItem cue = ItemFor(plan, "g\\Game.cue");
        AssertTrue(cue.Flags.HasFlag(PlanFlags.IncompleteDiscSet), "An ambiguous case-only reference must mark the set incomplete.");
        AssertEqual(PlanBucket.Review, cue.Bucket, "An ambiguous reference must send the sheet to review.");
        AssertTrue(cue.Reasons.Contains("case-ambiguous-reference"), "The review must carry an explicit reason.");
        AssertFalse(cue.IsSafeToApply, "An ambiguous set is never safe to apply.");
    }
}
