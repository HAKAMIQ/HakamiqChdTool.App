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
    private sealed class SyncProgress : IProgress<LibraryAnalysisProgress>
    {
        public List<LibraryAnalysisProgress> Events { get; } = [];

        public Action<LibraryAnalysisProgress>? Hook { get; init; }

        public void Report(LibraryAnalysisProgress value)
        {
            Events.Add(value);
            Hook?.Invoke(value);
        }
    }

    private static LibraryAnalysisResult Analyze(
        string root,
        IReadOnlyList<string> dats,
        string output,
        bool verify = false,
        IProgress<LibraryAnalysisProgress>? progress = null,
        CancellationToken token = default,
        DatContentHasher? hasher = null) =>
        new LibraryAnalysisService(new LibraryIndexer(), hasher ?? DatHashing.Hasher)
            .AnalyzeAsync(new LibraryAnalysisRequest(root, dats, output, verify), progress, token)
            .GetAwaiter()
            .GetResult();

    private static string AnalysisLibrary(string workDirectory, string name, params (string Path, byte[] Bytes)[] files)
    {
        string root = Path.Combine(workDirectory, name);
        Directory.CreateDirectory(root);

        foreach ((string path, byte[] bytes) in files)
        {
            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
            File.SetLastWriteTimeUtc(full, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        }

        return root;
    }

    private static void TestLibraryAnalysisEndToEnd(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        byte[] t1 = [1, 1, 1];
        byte[] t2 = [2, 2, 2];
        byte[] twin = [6, 6];
        byte[] cue = Encoding.ASCII.GetBytes("FILE \"Disc (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\nFILE \"Disc (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n");
        string root = AnalysisLibrary(workDirectory, "analysis-e2e",
            ("ps\\Alpha.bin", alpha),
            ("d\\Disc.cue", cue),
            ("d\\Disc (Track 1).bin", t1),
            ("d\\Disc (Track 2).bin", t2),
            ("misc\\Archive.zip", [80, 75, 3, 4, 0, 0]),
            ("misc\\Shared.bin", twin),
            ("misc\\unknown.rom", [9]));

        string dats = Path.Combine(workDirectory, "analysis-e2e-dats");
        string dat = WriteDat(dats, "ps.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", "USA", new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))),
            new DatGameSpec("Disc", "Europe",
                new DatRomSpec("Disc.cue", cue.Length, Sha1: Sha1Of(cue)),
                new DatRomSpec("Disc (Track 1).bin", 3, Sha1: Sha1Of(t1)),
                new DatRomSpec("Disc (Track 2).bin", 3, Sha1: Sha1Of(t2))),
            new DatGameSpec("Twin A", null, new DatRomSpec("Shared.bin", 2, Sha1: Sha1Of(twin))),
            new DatGameSpec("Twin B", null, new DatRomSpec("Shared.bin", 2, Sha1: Sha1Of(twin))));
        string dupPath = Path.Combine(dats, "ps.dat");
        string output = Path.Combine(workDirectory, "analysis-e2e-out");

        Dictionary<string, string> before = SnapshotTree(root);
        SyncProgress progress = new();
        LibraryAnalysisResult result = Analyze(root, [dat, dupPath], output, verify: true, progress);

        AssertEqual(LibraryAnalysisOutcome.Completed, result.Outcome, "Clean run completes: " + string.Join("; ", result.Issues.Select(i => i.Code + ":" + i.Message)));
        AssertEqual(1, result.Dats.Count, "The same DAT path given twice is parsed once.");
        AssertTrue(result.IsExecutable && result.Plan is { Status: PlanStatus.Ready }, "A complete clean run yields an executable-eligible plan.");
        OrganizationPlan plan = result.Plan!;

        PlanItem alphaItem = ItemFor(plan, "ps\\Alpha.bin");
        AssertEqual(DatMatchStatus.VerifiedMatch, alphaItem.MatchStatus, "SHA1 match is verified.");
        AssertEqual(Path.Combine("Sony - PlayStation", "Alpha.bin"), alphaItem.DestinationRelativePath, "Verified file goes to its platform folder.");
        AssertTrue(result.ExecutableItems.Any(i => i.SourceRelativePath == "ps\\Alpha.bin"), "Verified unflagged item is eligible.");

        PlanItem sheet = ItemFor(plan, "d\\Disc.cue");
        PlanItem track1 = ItemFor(plan, "d\\Disc (Track 1).bin");
        PlanItem track2 = ItemFor(plan, "d\\Disc (Track 2).bin");
        AssertTrue(sheet.DiscSetId is not null && sheet.DiscSetId == track1.DiscSetId && sheet.DiscSetId == track2.DiscSetId, "Multi-track disc is one set.");
        AssertEqual(Path.GetDirectoryName(sheet.DestinationRelativePath), Path.GetDirectoryName(track2.DestinationRelativePath), "Tracks land beside their sheet.");

        AssertEqual(DatMatchStatus.Unsupported, ItemFor(plan, "misc\\Archive.zip").MatchStatus, "Unmatched compressed media is unsupported, not guessed.");
        AssertEqual(PlanBucket.Review, ItemFor(plan, "misc\\Archive.zip").Bucket, "Unsupported media goes to review.");
        PlanItem shared = ItemFor(plan, "misc\\Shared.bin");
        AssertEqual(DatMatchStatus.Ambiguous, shared.MatchStatus, "Two games sharing a record stay ambiguous.");
        AssertFalse(shared.IsSafeToApply || result.ExecutableItems.Contains(shared), "Ambiguous is never eligible.");
        AssertEqual(DatMatchStatus.NotFound, ItemFor(plan, "misc\\unknown.rom").MatchStatus, "Unknown stays unknown.");

        AssertTrue(result.Report!.Contains("no files were changed") && result.Report.Contains("Content verification: requested"), "Report is a read-only preview.");
        AssertEqual(LibraryAnalysisPhase.Indexing, progress.Events[0].Phase, "Progress starts with indexing.");
        AssertEqual(LibraryAnalysisPhase.Finished, progress.Events[^1].Phase, "Progress ends finished.");
        LibraryAnalysisPhase[] order = progress.Events.Select(e => e.Phase).Distinct().ToArray();
        AssertTrue(order.SequenceEqual([
            LibraryAnalysisPhase.Indexing, LibraryAnalysisPhase.ParsingDats, LibraryAnalysisPhase.Matching,
            LibraryAnalysisPhase.Planning, LibraryAnalysisPhase.Reporting, LibraryAnalysisPhase.Finished]), "Phases are reported in pipeline order.");
        var matching = progress.Events.Where(e => e.Phase == LibraryAnalysisPhase.Matching).ToList();
        AssertEqual(7, matching[^1].Completed, "Matching progress reaches the entry count.");
        AssertTrue(matching.Zip(matching.Skip(1)).All(p => p.Second.Completed >= p.First.Completed), "Matching progress never goes backwards.");

        AssertFalse(Directory.Exists(output), "Analysis must not create the output folder.");
        AssertTreeUnchanged(before, root, "Analysis must not modify, move or re-stamp library files.");
    }

    private static void TestLibraryAnalysisDefaultsToNoHashing(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        string root = AnalysisLibrary(workDirectory, "analysis-nohash", ("Alpha.bin", alpha));
        string dat = WriteDat(Path.Combine(workDirectory, "analysis-nohash-dats"), "a.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))));

        int calls = 0;
        DatContentHasher counting = (path, kind, token) =>
        {
            Interlocked.Increment(ref calls);
            return DatHashing.Hasher(path, kind, token);
        };
        string output = Path.Combine(workDirectory, "analysis-nohash-out");

        LibraryAnalysisResult light = Analyze(root, [dat], output, verify: false, hasher: counting);
        AssertEqual(0, calls, "No file may be hashed without explicit opt-in.");
        PlanItem item = ItemFor(light.Plan!, "Alpha.bin");
        AssertEqual(DatMatchStatus.CandidateMatch, item.MatchStatus, "Name and size only is a candidate.");
        AssertEqual(0, light.ExecutableItems.Count, "A candidate is never eligible.");

        LibraryAnalysisResult verified = Analyze(root, [dat], output, verify: true, hasher: counting);
        AssertTrue(calls > 0, "Opt-in hashes the file.");
        AssertEqual(DatMatchStatus.VerifiedMatch, ItemFor(verified.Plan!, "Alpha.bin").MatchStatus, "Opt-in verifies.");
    }

    private static void TestLibraryAnalysisCrc32OnlyAndCollisions(string workDirectory)
    {
        byte[] a = [1, 2, 3];
        byte[] b = [4, 5, 6];
        string crcA = Crc32Streaming.ToHex(Crc32Streaming.Update(0, a));
        string crcB = Crc32Streaming.ToHex(Crc32Streaming.Update(0, b));
        string root = AnalysisLibrary(workDirectory, "analysis-crc", ("renamed-a.bin", a), ("renamed-b.bin", b));
        string dat = WriteDat(Path.Combine(workDirectory, "analysis-crc-dats"), "c.dat", "Sega - Mega Drive",
            new DatGameSpec("Only A", null, new DatRomSpec("a.bin", null, Crc: crcA)),
            new DatGameSpec("B One", null, new DatRomSpec("b1.bin", 3, Crc: crcB)),
            new DatGameSpec("B Two", null, new DatRomSpec("b2.bin", 3, Crc: crcB)));

        LibraryAnalysisResult result = Analyze(root, [dat], Path.Combine(workDirectory, "analysis-crc-out"), verify: true);
        PlanItem noSize = ItemFor(result.Plan!, "renamed-a.bin");
        AssertEqual(DatMatchStatus.CandidateMatch, noSize.MatchStatus, "CRC32 without a catalog size is only a candidate.");
        AssertTrue(noSize.Flags.HasFlag(PlanFlags.UnverifiedIdentity), "Weak identity is flagged.");
        PlanItem collision = ItemFor(result.Plan!, "renamed-b.bin");
        AssertEqual(DatMatchStatus.Ambiguous, collision.MatchStatus, "A CRC32 shared by two games stays ambiguous.");
        AssertEqual(0, result.ExecutableItems.Count, "Nothing CRC32-only or ambiguous is eligible.");
    }

    private static void TestLibraryAnalysisDestinationProblems(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        byte[] odd = [7, 7];
        string root = AnalysisLibrary(workDirectory, "analysis-dest", ("Alpha.bin", alpha), ("Odd.bin", odd));
        string dats = Path.Combine(workDirectory, "analysis-dest-dats");
        string dat = WriteDat(dats, "d.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))));
        string badPlatform = WriteDat(dats, "bad.dat", "Bad: Platform",
            new DatGameSpec("Odd", null, new DatRomSpec("Odd.bin", 2, Sha1: Sha1Of(odd))));
        string output = Path.Combine(workDirectory, "analysis-dest-out");
        Directory.CreateDirectory(Path.Combine(output, "Sony - PlayStation"));
        File.WriteAllText(Path.Combine(output, "Sony - PlayStation", "Alpha.bin"), "existing");

        LibraryAnalysisResult result = Analyze(root, [dat, badPlatform], output, verify: true);
        PlanItem existing = ItemFor(result.Plan!, "Alpha.bin");
        AssertTrue(existing.Flags.HasFlag(PlanFlags.DestinationExists) && !existing.IsSafeToApply, "An occupied destination is flagged.");
        PlanItem invalid = ItemFor(result.Plan!, "Odd.bin");
        AssertTrue(invalid.Flags.HasFlag(PlanFlags.InvalidDestinationName) && !invalid.IsSafeToApply, "A reserved device name is flagged.");
        AssertEqual(0, result.ExecutableItems.Count, "No flagged item is eligible.");
        AssertEqual("existing", File.ReadAllText(Path.Combine(output, "Sony - PlayStation", "Alpha.bin")), "Existing destination content is untouched.");

        LibraryAnalysisResult rejected = Analyze(root, [dat], "relative-output", verify: false);
        AssertEqual(LibraryAnalysisOutcome.Failed, rejected.Outcome, "A rejected output root fails the run.");
        AssertFalse(rejected.IsExecutable, "A rejected plan is never executable.");
        AssertEqual(0, rejected.ExecutableItems.Count, "A rejected plan offers no items.");
    }

    private static void TestLibraryAnalysisCancellation(string workDirectory)
    {
        List<byte[]> files = Enumerable.Range(0, 5).Select(i => new byte[] { (byte)i, 9 }).ToList();
        string root = AnalysisLibrary(workDirectory, "analysis-cancel",
            files.Select((b, i) => ($"g{i}.bin", b)).ToArray());
        string dat = WriteDat(Path.Combine(workDirectory, "analysis-cancel-dats"), "c.dat", "Sony - PlayStation",
            files.Select((b, i) => new DatGameSpec($"G{i}", null, new DatRomSpec($"g{i}.bin", 2, Sha1: Sha1Of(b)))).ToArray());
        string output = Path.Combine(workDirectory, "analysis-cancel-out");
        Dictionary<string, string> before = SnapshotTree(root);

        using (CancellationTokenSource pre = new())
        {
            pre.Cancel();
            LibraryAnalysisResult early = Analyze(root, [dat], output, token: pre.Token);
            AssertEqual(LibraryAnalysisOutcome.Cancelled, early.Outcome, "Pre-cancelled run is cancelled.");
            AssertTrue(early.Plan is null && !early.IsExecutable && early.IsPartial, "Cancelled run has no plan.");
        }

        using CancellationTokenSource mid = new();
        DatContentHasher cancelling = async (path, kind, token) =>
        {
            string hash = await DatHashing.Hasher(path, kind, token);
            mid.Cancel();
            return hash;
        };
        LibraryAnalysisResult partial = Analyze(root, [dat], output, verify: true, token: mid.Token, hasher: cancelling);

        AssertEqual(LibraryAnalysisOutcome.Cancelled, partial.Outcome, "Mid-matching cancellation is reported.");
        AssertEqual(LibraryAnalysisPhase.Matching, partial.LastPhase, "The interrupted phase is reported.");
        AssertEqual(5, partial.Index!.Entries.Count, "The completed index is kept.");
        AssertTrue(partial.Match is { Outcome: DatMatchOutcome.Cancelled } && partial.Match.Results.Count is > 0 and < 5, "Only the files matched so far are kept.");
        AssertTrue(partial.Plan is null && !partial.IsExecutable && partial.ExecutableItems.Count == 0, "No plan is built from partial matching.");
        AssertTrue(partial.Report!.Contains("Cancelled") && partial.Report.Contains("no plan was built"), "Report says the result is incomplete.");
        AssertFalse(Directory.Exists(output), "Cancellation creates nothing.");
        AssertTreeUnchanged(before, root, "Cancellation leaves the library untouched.");
    }

    private static void TestLibraryAnalysisScalesAndReportsMonotonicProgress(string workDirectory)
    {
        const int Count = 4000;
        string root = AnalysisLibrary(workDirectory, "analysis-scale",
            Enumerable.Range(0, Count).Select(i => ($"d{i % 40}\\f{i}.bin", new byte[] { (byte)i, (byte)(i >> 8), 7 })).ToArray());
        string dat = WriteDat(Path.Combine(workDirectory, "analysis-scale-dats"), "s.dat", "Sony - PlayStation",
            [new DatGameSpec("G0", null, new DatRomSpec("f0.bin", 3))]);
        string output = Path.Combine(workDirectory, "analysis-scale-out");

        SyncProgress full = new();
        Stopwatch clock = Stopwatch.StartNew();
        LibraryAnalysisResult result = Analyze(root, [dat], output, progress: full);
        clock.Stop();
        Console.WriteLine($"[MEASURE] Library analysis of {Count} files: {clock.ElapsedMilliseconds} ms, {full.Events.Count} progress events.");

        AssertEqual(Count, result.Index!.Entries.Count, "Every synthetic file is indexed.");
        AssertTrue(result.Plan is not null && result.Plan.Items.Count == Count, "The plan covers every file.");
        AssertTrue(clock.Elapsed < TimeSpan.FromSeconds(60), $"A {Count}-file analysis stays within its budget ({clock.ElapsedMilliseconds} ms).");

        List<LibraryAnalysisProgress> matching = full.Events.Where(e => e.Phase == LibraryAnalysisPhase.Matching).ToList();
        AssertTrue(matching.Count > 2 && matching[^1].Completed == Count && matching.All(e => e.Total == Count), "Matching progress ends at the total.");
        AssertTrue(matching.Zip(matching.Skip(1)).All(p => p.Second.Completed >= p.First.Completed), "Matching progress never moves backwards.");
        AssertTrue(full.Events.Select(e => e.Phase).Zip(full.Events.Select(e => e.Phase).Skip(1)).All(p => p.Second >= p.First), "Phases never go backwards.");

        using CancellationTokenSource mid = new();
        long cancelledAt = 0;
        SyncProgress cancelling = new() { Hook = e => { if (e.Phase == LibraryAnalysisPhase.Matching && e.Completed >= Count / 2 && !mid.IsCancellationRequested) { cancelledAt = clock.ElapsedMilliseconds; mid.Cancel(); } } };
        clock.Restart();
        LibraryAnalysisResult partial = Analyze(root, [dat], output, progress: cancelling, token: mid.Token);
        Console.WriteLine($"[MEASURE] Cancel requested at {cancelledAt} ms; run returned at {clock.ElapsedMilliseconds} ms ({clock.ElapsedMilliseconds - cancelledAt} ms latency).");

        AssertEqual(LibraryAnalysisOutcome.Cancelled, partial.Outcome, "Cancelling mid-matching is reported.");
        AssertTrue(partial.Plan is null && partial.Match is { } m && m.Results.Count >= Count / 2 && m.Results.Count < Count, "Only the files matched before cancellation are kept.");
        AssertTrue(clock.ElapsedMilliseconds - cancelledAt < 2000, "Cancellation returns promptly after the request.");
        AssertFalse(Directory.Exists(output), "Neither run creates the output folder.");
    }

    private static void TestLibraryAnalysisIncompleteInputs(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        string root = AnalysisLibrary(workDirectory, "analysis-incomplete", ("Alpha.bin", alpha));
        string dats = Path.Combine(workDirectory, "analysis-incomplete-dats");
        string good = WriteDat(dats, "good.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))));
        string bad = Path.Combine(dats, "bad.dat");
        File.WriteAllText(bad, "this is not a DAT");
        string output = Path.Combine(workDirectory, "analysis-incomplete-out");

        LibraryAnalysisResult mixed = Analyze(root, [good, bad], output, verify: true);
        AssertEqual(LibraryAnalysisOutcome.CompletedWithIssues, mixed.Outcome, "An unparsable DAT downgrades the run.");
        AssertTrue(mixed.Plan is not null && !mixed.IsExecutable && mixed.ExecutableItems.Count == 0, "An incomplete catalog is never executable.");
        AssertTrue(mixed.Issues.Any(i => i.Phase == LibraryAnalysisPhase.ParsingDats), "The DAT problem is reported.");
        AssertEqual(2, mixed.Dats.Count, "Every DAT outcome is kept.");

        AssertEqual(LibraryAnalysisOutcome.Failed, Analyze(root, [bad], output).Outcome, "No usable DAT fails the run.");
        AssertEqual(LibraryAnalysisOutcome.Failed, Analyze(root, [], output).Outcome, "No DAT fails the run.");
        LibraryAnalysisResult missingRoot = Analyze(Path.Combine(workDirectory, "analysis-missing"), [good], output);
        AssertTrue(missingRoot.Outcome == LibraryAnalysisOutcome.Failed && missingRoot.Plan is null && missingRoot.IsPartial, "A rejected library root fails without a plan.");
        AssertEqual(LibraryAnalysisOutcome.Failed, Analyze(root, [Path.Combine(dats, "missing.dat")], output).Outcome, "A missing DAT file fails the run.");
    }
}
