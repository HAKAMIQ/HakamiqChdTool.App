using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services.Library;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestOrganizerExecuteJournalUndo(string workDirectory) =>
        TestOrganizerExecuteJournalUndoAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerExecuteJournalUndoAsync(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "organizer-e2e-library");
        string output = Path.Combine(workDirectory, "organizer-e2e-output");
        string source = Path.Combine(root, "Game.iso");
        Directory.CreateDirectory(root);
        WriteIsoImage(source, 2048, 0, "BOOT2 = cdrom0:\\SLES_999.99;1\r\nVER = 1.00\r\n");
        byte[] content = await File.ReadAllBytesAsync(source);
        string dat = WriteDat(Path.Combine(workDirectory, "organizer-e2e-dats"), "synthetic.dat", "Sony - PlayStation",
            new DatGameSpec("Synthetic Game", "World", new DatRomSpec("Game.iso", content.LongLength, Sha1: Sha1Of(content))));

        LibraryAnalysisResult analysis = await new LibraryAnalysisService().AnalyzeAsync(
            new LibraryAnalysisRequest(root, [dat], output, VerifyContent: true), progress: null, CancellationToken.None);
        AssertEqual(LibraryAnalysisOutcome.Completed, analysis.Outcome, "Synthetic intake, index, DAT match and preview should complete cleanly.");
        AssertTrue(analysis.Index?.Entries.Count == 1, "Analysis should classify and index the synthetic library input.");
        AssertTrue(analysis.Plan is { Status: PlanStatus.Ready }, "Analysis should produce a ready preview.");
        OrganizationPlan preview = analysis.Plan!;
        AssertEqual(DatMatchStatus.VerifiedMatch, preview.Items.Single().MatchStatus, "DAT matching should verify content before execution.");

        string journalPath = Path.Combine(workDirectory, "organizer-e2e-journal", "operations.jsonl");
        LibraryOperationJournal journal = new(journalPath);
        LibraryMovePlan plan = await new LibraryPlanValidator().CreateAsync(preview, root);
        LibraryMoveOperation operation = plan.Operations.Single();
        AssertTrue(operation.Eligible, "Content-verified planner row should be eligible.");
        AssertEqual(content.LongLength, operation.FileSize, "Execution plan records the source size.");
        AssertEqual(Convert.ToHexString(SHA256.HashData(content)), operation.Sha256, "Execution plan records a SHA-256 snapshot.");

        LibraryOrganizerService organizer = new(journal);
        LibraryOperationResult moved = (await organizer.ExecuteAsync(plan, new HashSet<Guid> { operation.OperationId }, confirmed: true)).Single();
        string destination = Path.Combine(output, "Sony - PlayStation", "Game.iso");
        AssertEqual(LibraryOperationState.Completed, moved.State, "Execution should report a completed same-volume move.");
        AssertTrue(!File.Exists(source) && File.Exists(destination), "Execution should move, not copy, the source file.");
        AssertEqual(Convert.ToHexString(SHA256.HashData(content)), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(destination))),
            "Moved content must match the original manifest hash.");

        LibraryUndoService undo = new(journal);
        LibraryOperationResult previewUndo = (await undo.PreviewUndoAsync(plan.PlanId)).Single();
        AssertEqual(LibraryOperationState.Completed, previewUndo.State, "Undo preview should identify the recorded move as restorable.");
        LibraryOperationResult undone = (await undo.UndoAsync(plan.PlanId)).Single();
        AssertEqual(LibraryOperationState.Undone, undone.State, "Undo should record completion.");
        AssertTrue(File.Exists(source) && !File.Exists(destination), "Undo should restore the original path without a duplicate.");
        AssertEqual(Convert.ToHexString(SHA256.HashData(content)), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))),
            "Restored content must match the original manifest hash.");

        LibraryOperationState[] states = (await journal.ReadAllAsync()).Select(entry => entry.State).ToArray();
        AssertTrue(states.Contains(LibraryOperationState.IntentRecorded)
            && states.Contains(LibraryOperationState.Completed)
            && states.Contains(LibraryOperationState.UndoPending)
            && states.Contains(LibraryOperationState.Undone), "Journal must persist execution and undo transitions.");
    }

    private static void TestOrganizerRejectsSourceChangesAfterPreview(string workDirectory) =>
        TestOrganizerRejectsSourceChangesAfterPreviewAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerRejectsSourceChangesAfterPreviewAsync(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "organizer-changed-library");
        string output = Path.Combine(workDirectory, "organizer-changed-output");
        string source = Path.Combine(root, "Game.iso");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        OrganizationPlan preview = new(PlanStatus.Ready, output,
        [
            new PlanItem("Game.iso", "Console/Game.iso", PlanBucket.Platform,
                DatMatchStatus.VerifiedMatch, "Console", "Synthetic Game", null,
                PlanFlags.None, [], "Synthetic DAT", "World")
        ], []);

        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-changed-journal", "operations.jsonl"));
        LibraryMovePlan plan = await new LibraryPlanValidator().CreateAsync(preview, root);
        LibraryMoveOperation operation = plan.Operations.Single();
        await File.WriteAllBytesAsync(source, [9, 8, 7]);

        LibraryOperationResult result = (await new LibraryOrganizerService(journal)
            .ExecuteAsync(plan, new HashSet<Guid> { operation.OperationId }, confirmed: true)).Single();
        AssertEqual(LibraryOperationState.Failed, result.State, "Changed source must be rejected after preview.");
        AssertTrue(File.Exists(source) && !File.Exists(Path.Combine(output, "Console", "Game.iso")),
            "A failed freshness check must leave the source in place and destination absent.");
        AssertEqual("source-changed-after-preview", result.ErrorCode, "Failure should have a stable reason code.");
    }

    private static void TestOrganizerRecoversIntentAfterMove(string workDirectory) =>
        TestOrganizerRecoversIntentAfterMoveAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerRecoversIntentAfterMoveAsync(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "organizer-recovery-library");
        string output = Path.Combine(workDirectory, "organizer-recovery-output");
        string source = Path.Combine(root, "Game.iso");
        string destination = Path.Combine(output, "Console", "Game.iso");
        byte[] content = [5, 7, 9, 11];
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(source, content);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        Guid operationId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        string hash = Convert.ToHexString(SHA256.HashData(content));
        LibraryMoveOperation operation = new(operationId, source, destination, content.LongLength,
            hash, null, true, LibraryOperationState.IntentRecorded, LibraryRoot: root, OutputRoot: output);
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-recovery-journal", "operations.jsonl"));
        await journal.AppendAsync(operation, planId, LibraryOperationState.IntentRecorded, null, default);
        File.Move(source, destination);

        LibraryOperationResult recovered = (await new LibraryUndoService(journal).RecoverAsync(planId)).Single();
        AssertEqual(LibraryOperationState.Completed, recovered.State, "Recovery should recognize a verified destination after a crash.");
        AssertTrue(!File.Exists(source) && File.Exists(destination), "Recovery must record the completed move without moving it again.");
        AssertEqual(LibraryOperationState.Completed, (await journal.ReadAllAsync()).Last().State,
            "Recovery must append the reconciled state to the persistent journal.");
    }

    private static void TestOrganizerUndoRefusesModifiedDestination(string workDirectory) =>
        TestOrganizerUndoRefusesModifiedDestinationAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerUndoRefusesModifiedDestinationAsync(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "organizer-undo-conflict-library");
        string output = Path.Combine(workDirectory, "organizer-undo-conflict-output");
        string source = Path.Combine(root, "Game.iso");
        byte[] content = [1, 3, 3, 7];
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(source, content);
        OrganizationPlan preview = new(PlanStatus.Ready, output,
        [
            new PlanItem("Game.iso", "Console/Game.iso", PlanBucket.Platform,
                DatMatchStatus.VerifiedMatch, "Console", "Synthetic Game", null,
                PlanFlags.None, [], "Synthetic DAT", "World")
        ], []);
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-undo-conflict-journal", "operations.jsonl"));
        LibraryMovePlan plan = await new LibraryPlanValidator().CreateAsync(preview, root);
        LibraryMoveOperation operation = plan.Operations.Single();
        await new LibraryOrganizerService(journal).ExecuteAsync(plan, new HashSet<Guid> { operation.OperationId }, confirmed: true);
        string destination = Path.Combine(output, "Console", "Game.iso");
        byte[] edited = [8, 6, 4, 2];
        await File.WriteAllBytesAsync(destination, edited);

        LibraryOperationResult assessed = (await new LibraryUndoService(journal).PreviewUndoAsync(plan.PlanId)).Single();
        AssertEqual(LibraryOperationState.Conflict, assessed.State, "Undo preview must identify a modified destination as conflict.");
        LibraryOperationResult undo = (await new LibraryUndoService(journal).UndoAsync(plan.PlanId)).Single();
        AssertEqual(LibraryOperationState.Conflict, undo.State, "Undo must refuse to move modified content.");
        AssertTrue(!File.Exists(source) && File.Exists(destination), "A conflict must leave both paths untouched.");
        AssertEqual(Convert.ToHexString(edited), Convert.ToHexString(await File.ReadAllBytesAsync(destination)),
            "Undo must preserve the user's edited destination bytes.");
    }

    private static async Task TestOrganizerUndoGroupPreflightsConflictsAsync(string workDirectory)
    {
        var fixture = BuildVerifiedOrganizerCuePlan(workDirectory, "organizer-cue-undo-conflict");
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-cue-undo-conflict-journal", "operations.jsonl"));
        HashSet<Guid> selected = fixture.Plan.Operations.Select(operation => operation.OperationId).ToHashSet();
        await new LibraryOrganizerService(journal).ExecuteAsync(fixture.Plan, selected, confirmed: true);
        LibraryMoveOperation blocked = fixture.Plan.Operations[0];
        await File.WriteAllBytesAsync(blocked.SourcePath, [0xEF, 0xBE]);

        LibraryOperationResult[] results = (await new LibraryUndoService(journal).UndoAsync(fixture.Plan.PlanId)).ToArray();
        AssertTrue(results.Length == 2 && results.All(result => result.State == LibraryOperationState.Conflict),
            "An occupied original path should prevent Undo of every member in the disc group.");
        AssertTrue(fixture.Plan.Operations.All(operation => File.Exists(operation.DestinationPath)),
            "Group preflight must prevent partial restoration when any source path conflicts.");
    }

    private static (string Root, string Output, LibraryMovePlan Plan, byte[] Cue, byte[] Track)
        BuildVerifiedOrganizerCuePlan(string workDirectory, string name)
    {
        byte[] cue = Encoding.ASCII.GetBytes("FILE \"Track.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n");
        byte[] track = [0x11, 0x22, 0x33, 0x44, 0x55];
        (string Root, System.Collections.Generic.IReadOnlyList<LibraryIndexEntry> Entries) library =
            BuildMatchLibrary(workDirectory, name,
                (Path.Combine("Disc", "Disc.cue"), cue),
                (Path.Combine("Disc", "Track.bin"), track));
        string dat = WriteDat(Path.Combine(workDirectory, name + "-dats"), "synthetic.dat", "Sony - PlayStation",
            new DatGameSpec("Synthetic Disc", "Europe",
                new DatRomSpec("Disc.cue", cue.LongLength, Sha1: Sha1Of(cue)),
                new DatRomSpec("Track.bin", track.LongLength, Sha1: Sha1Of(track))));
        DatMatchReport matches = RunMatch(new DatMatcher([ParseDatOrFail(dat)], DatHashing.Hasher),
            library.Root, library.Entries, verify: true);
        string output = Path.Combine(workDirectory, name + "-output");
        OrganizationPlan preview = OrganizationPlanner.Plan(library.Root, output, library.Entries, matches.Results);
        AssertEqual(2, preview.Items.Count, "The fixture should form one cue and one track row.");
        AssertTrue(preview.Items.All(item => item.IsSafeToApply && item.DiscSetId is not null),
            "Every member of the fully verified CUE/BIN set should be eligible as a unit.");
        LibraryMovePlan plan = new LibraryPlanValidator().CreateAsync(preview, library.Root).GetAwaiter().GetResult();
        AssertTrue(plan.Operations.Select(item => item.GroupId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1,
            "The execution plan should retain the disc-set identity.");
        return (library.Root, output, plan, cue, track);
    }

    private static void TestOrganizerMovesAndUndoesCueBinSet(string workDirectory) =>
        TestOrganizerMovesAndUndoesCueBinSetAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerMovesAndUndoesCueBinSetAsync(string workDirectory)
    {
        var fixture = BuildVerifiedOrganizerCuePlan(workDirectory, "organizer-cue-success");
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-cue-success-journal", "operations.jsonl"));
        HashSet<Guid> selected = fixture.Plan.Operations.Select(operation => operation.OperationId).ToHashSet();
        LibraryOperationResult[] moved = (await new LibraryOrganizerService(journal)
            .ExecuteAsync(fixture.Plan, selected, confirmed: true)).ToArray();
        AssertTrue(moved.All(result => result.State == LibraryOperationState.Completed), "Every CUE/BIN member should complete together.");

        foreach (LibraryMoveOperation operation in fixture.Plan.Operations)
        {
            byte[] expected = operation.SourcePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ? fixture.Cue : fixture.Track;
            AssertEqual(Convert.ToHexString(SHA256.HashData(expected)),
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(operation.DestinationPath))),
                "Each moved set member should retain its original bytes.");
        }

        LibraryOperationResult[] undone = (await new LibraryUndoService(journal)
            .UndoAsync(fixture.Plan.PlanId)).ToArray();
        AssertTrue(undone.Length == 2 && undone.All(result => result.State == LibraryOperationState.Undone),
            "Undo should restore every member of the CUE/BIN group.");
        AssertTrue(fixture.Plan.Operations.All(operation => File.Exists(operation.SourcePath) && !File.Exists(operation.DestinationPath)),
            "Every group file should return to its original path.");
    }

    private static async Task TestOrganizerMovesAndUndoesGdiSetAsync(string workDirectory)
    {
        byte[] gdi = Encoding.ASCII.GetBytes("3\r\n1 0 4 2352 \"track01.bin\" 0\r\n2 756 0 2352 \"track02.raw\" 0\r\n3 45000 4 2352 \"track03.bin\" 0\r\n");
        byte[] track1 = [0x01, 0x02, 0x03];
        byte[] track2 = [0x04, 0x05];
        byte[] track3 = [0x06, 0x07, 0x08, 0x09];
        string fixtureName = "organizer-gdi-success";
        var library = BuildMatchLibrary(workDirectory, fixtureName,
            (Path.Combine("Disc", "disc.gdi"), gdi),
            (Path.Combine("Disc", "track01.bin"), track1),
            (Path.Combine("Disc", "track02.raw"), track2),
            (Path.Combine("Disc", "track03.bin"), track3));
        string dat = WriteDat(Path.Combine(workDirectory, fixtureName + "-dats"), "synthetic.dat", "Sega - Dreamcast",
            new DatGameSpec("Synthetic GDI Disc", "World",
                new DatRomSpec("disc.gdi", gdi.LongLength, Sha1: Sha1Of(gdi)),
                new DatRomSpec("track01.bin", track1.LongLength, Sha1: Sha1Of(track1)),
                new DatRomSpec("track02.raw", track2.LongLength, Sha1: Sha1Of(track2)),
                new DatRomSpec("track03.bin", track3.LongLength, Sha1: Sha1Of(track3))));
        DatMatchReport matches = RunMatch(new DatMatcher([ParseDatOrFail(dat)], DatHashing.Hasher),
            library.Root, library.Entries, verify: true);
        string output = Path.Combine(workDirectory, fixtureName + "-output");
        OrganizationPlan preview = OrganizationPlanner.Plan(library.Root, output, library.Entries, matches.Results);
        AssertTrue(preview.Items.Count == 4 && preview.Items.All(item => item.IsSafeToApply && item.DiscSetId is not null),
            "The verified GDI sheet and its three referenced tracks should be one eligible group.");
        LibraryMovePlan plan = await new LibraryPlanValidator().CreateAsync(preview, library.Root);
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, fixtureName + "-journal", "operations.jsonl"));
        HashSet<Guid> selected = plan.Operations.Select(operation => operation.OperationId).ToHashSet();
        LibraryOperationResult[] moved = (await new LibraryOrganizerService(journal)
            .ExecuteAsync(plan, selected, confirmed: true)).ToArray();
        AssertTrue(moved.Length == 4 && moved.All(result => result.State == LibraryOperationState.Completed),
            "Every verified GDI group member should move successfully.");
        LibraryOperationResult[] undone = (await new LibraryUndoService(journal).UndoAsync(plan.PlanId)).ToArray();
        AssertTrue(undone.Length == 4 && undone.All(result => result.State == LibraryOperationState.Undone),
            "Undo should restore every GDI group member.");
        AssertTrue(plan.Operations.All(operation => File.Exists(operation.SourcePath) && !File.Exists(operation.DestinationPath)),
            "GDI undo should restore the original file tree without leaving destination copies.");
    }

    private static void TestOrganizerRollsBackCueBinSetOnMidGroupConflict(string workDirectory) =>
        TestOrganizerRollsBackCueBinSetOnMidGroupConflictAsync(workDirectory).GetAwaiter().GetResult();

    private static void TestOrganizerCancellationRestoresCueBinGroup(string workDirectory) =>
        TestOrganizerCancellationRestoresCueBinGroupAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerCancellationRestoresCueBinGroupAsync(string workDirectory)
    {
        var fixture = BuildVerifiedOrganizerCuePlan(workDirectory, "organizer-cue-cancel");
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-cue-cancel-journal", "operations.jsonl"));
        using CancellationTokenSource cancellation = new();
        InlineProgress<(int Completed, int Total)> progress = new(value =>
        {
            if (value.Completed == 1) cancellation.Cancel();
        });
        HashSet<Guid> selected = fixture.Plan.Operations.Select(operation => operation.OperationId).ToHashSet();
        bool cancelled = false;
        try
        {
            await new LibraryOrganizerService(journal).ExecuteAsync(fixture.Plan, selected, confirmed: true,
                progress, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        AssertTrue(cancelled, "Cancellation requested between group members should be propagated.");
        AssertTrue(fixture.Plan.Operations.All(operation => File.Exists(operation.SourcePath)
            && !File.Exists(operation.DestinationPath)), "Cancellation should roll back the earlier member and preserve the entire source set.");
        AssertTrue((await journal.ReadAllAsync()).Any(entry => entry.State == LibraryOperationState.Undone),
            "Cancellation rollback should be represented in the durable journal.");
    }

    private static async Task TestOrganizerRollsBackCueBinSetOnMidGroupConflictAsync(string workDirectory)
    {
        var fixture = BuildVerifiedOrganizerCuePlan(workDirectory, "organizer-cue-rollback");
        LibraryOperationJournal journal = new(Path.Combine(workDirectory, "organizer-cue-rollback-journal", "operations.jsonl"));
        LibraryMoveOperation conflicting = fixture.Plan.Operations[1];
        byte[] sentinel = [0xFA, 0xCE];
        InlineProgress<(int Completed, int Total)> progress = new(value =>
        {
            if (value.Completed == 1)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(conflicting.DestinationPath)!);
                File.WriteAllBytes(conflicting.DestinationPath, sentinel);
            }
        });
        HashSet<Guid> selected = fixture.Plan.Operations.Select(operation => operation.OperationId).ToHashSet();
        LibraryOperationResult[] results = (await new LibraryOrganizerService(journal)
            .ExecuteAsync(fixture.Plan, selected, confirmed: true, progress)).ToArray();
        AssertEqual(LibraryOperationState.Undone, results[0].State,
            "A previously moved group member should report its rollback, not stale Completed state.");
        AssertEqual(LibraryOperationState.Conflict, results[1].State,
            "The member whose destination appeared concurrently should report Conflict.");
        AssertTrue(fixture.Plan.Operations.All(operation => File.Exists(operation.SourcePath)),
            "All original group members should remain after rollback.");
        AssertTrue(!File.Exists(fixture.Plan.Operations[0].DestinationPath),
            "The earlier member should have been restored from the destination.");
        AssertEqual(Convert.ToHexString(sentinel), Convert.ToHexString(await File.ReadAllBytesAsync(conflicting.DestinationPath)),
            "Rollback must preserve the concurrent destination file.");
    }
}
