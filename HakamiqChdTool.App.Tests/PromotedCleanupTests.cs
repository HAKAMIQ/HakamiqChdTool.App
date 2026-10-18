using HakamiqChdTool.App.Core.Workflow;
using System;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Rollback after a failed cue/bin finalization may delete only files this call promoted. A user file that
    // already sits at the final cue path must survive when the pending cue cannot be processed.
    private static void TestFailedCueFinalizationNeverDeletesPreExistingFinalCue(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "promote-preexisting-" + Guid.NewGuid().ToString("N"));
        string pendingDir = Path.Combine(root, "pending");
        string finalDir = Path.Combine(root, "final");
        Directory.CreateDirectory(pendingDir);
        Directory.CreateDirectory(finalDir);

        string pendingCue = Path.Combine(pendingDir, "game.cue");
        using (FileStream stream = File.Create(pendingCue))
        {
            stream.SetLength(2L * 1024 * 1024); // above the cue size bound, so reading it fails
        }

        string finalCue = Path.Combine(finalDir, "game.cue");
        File.WriteAllText(finalCue, "user's own cue sheet");

        bool ok = WorkflowSafePathValidator.TryFinalizeExtractedCueBinOutput(pendingCue, finalCue, out _);

        AssertFalse(ok, "An unreadable pending cue is not finalized.");
        AssertTrue(File.Exists(finalCue), "The pre-existing final cue is not deleted by the rollback.");
        AssertEqual("user's own cue sheet", File.ReadAllText(finalCue), "The pre-existing final cue is untouched.");
        Directory.Delete(root, recursive: true);
    }

    // The same rollback must not delete through a directory that has become a junction.
    private static void TestPromotedCleanupRefusesJunctionedTargets(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "promote-junction-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(root, "outside");
        string finalDir = Path.Combine(root, "final");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(finalDir);
        string victim = Path.Combine(outside, "game.bin");
        File.WriteAllBytes(victim, [1, 2, 3]);

        string linked = Path.Combine(finalDir, "linked");
        CreateJunction(linked, outside);

        WorkflowSafePathValidator.CleanupPromotedForTest([Path.Combine(linked, "game.bin")], Path.Combine(linked, "none.cue"));

        AssertTrue(File.Exists(victim), "A file reached through a junction is not deleted by the rollback.");
        Directory.Delete(linked);
        Directory.Delete(root, recursive: true);
    }
}
