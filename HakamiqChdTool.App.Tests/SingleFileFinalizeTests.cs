using HakamiqChdTool.App.Core.Workflow.Extraction;
using System;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // "Skip existing output" must hold at finalization time, not only at the early check: a file that appeared at the
    // final path in the meantime (a sibling job, the user) is not replaced. With overwrite allowed it still is.
    private static void TestSingleFileFinalizeHonoursAllowOverwrite(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "single-finalize-" + Guid.NewGuid().ToString("N"));
        string pendingDir = Path.Combine(root, "pending");
        string finalDir = Path.Combine(root, "final");
        Directory.CreateDirectory(pendingDir);
        Directory.CreateDirectory(finalDir);

        string pending = Path.Combine(pendingDir, "output.iso");
        string final = Path.Combine(finalDir, "Game.iso");
        File.WriteAllBytes(pending, [9, 9, 9, 9]);
        File.WriteAllBytes(final, [1, 2, 3]);

        var contract = ExtractionOutputContract.Create(HakamiqChdTool.App.Models.Chd.ChdmanExtractionKind.ExtractDvd, pending, final);
        var validator = new ExtractionOutputBundleValidator();

        bool refused = validator.TryFinalize(contract, allowOverwrite: false, out _, out _);
        AssertFalse(refused, "Finalizing over an existing output is refused when overwrite is not allowed.");
        AssertEqual(3L, new FileInfo(final).Length, "The existing output is untouched.");
        AssertTrue(File.Exists(pending), "The produced file stays in the workspace for cleanup.");

        bool replaced = validator.TryFinalize(contract, allowOverwrite: true, out _, out _);
        AssertTrue(replaced, "Overwrite allowed: the produced file replaces the output.");
        AssertEqual(4L, new FileInfo(final).Length, "The new content is in place.");
        AssertFalse(Directory.GetFiles(finalDir, "*.hakamiq-backup-*").Length > 0, "No backup is left behind.");

        Directory.Delete(root, recursive: true);
    }

    // Two inputs that map to one final file (Game.cue and Game.iso both give Game.chd): the second job to finish must not
    // replace the first one's output when skip-existing is on.
    private static void TestPromoteIfAbsentNeverReplacesAndMovesWhenFree(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "promote-absent-" + Guid.NewGuid().ToString("N"));
        string pendingDir = Path.Combine(root, "pending");
        string finalDir = Path.Combine(root, "final");
        Directory.CreateDirectory(pendingDir);
        Directory.CreateDirectory(finalDir);
        string final = Path.Combine(finalDir, "Game.chd");

        string firstPending = Path.Combine(pendingDir, "first.chd");
        string secondPending = Path.Combine(pendingDir, "second.chd");
        File.WriteAllBytes(firstPending, [1, 1, 1]);
        File.WriteAllBytes(secondPending, [2, 2, 2, 2, 2]);

        AssertTrue(HakamiqChdTool.App.Core.Workflow.WorkflowOutputPathContract.TryPromoteProducedFileIfAbsent(firstPending, final),
            "The first job promotes into the free final path.");
        AssertFalse(HakamiqChdTool.App.Core.Workflow.WorkflowOutputPathContract.TryPromoteProducedFileIfAbsent(secondPending, final),
            "The second job is refused: the final path is taken.");

        AssertEqual(3L, new FileInfo(final).Length, "The first job's output is untouched.");
        AssertTrue(File.Exists(secondPending), "The refused job's produced file stays in its workspace for cleanup.");
        AssertFalse(File.Exists(firstPending), "The promoted file left the workspace.");

        Directory.Delete(root, recursive: true);
    }

    // CUE/BIN bundles: a file that appears at a final path after the early check (a BIN, then the CUE itself) is never
    // replaced when overwrite is off, and the promotions that already happened are rolled back.
    private static void TestCueBundleFinalizeNeverReplacesConcurrentFile(string workDirectory)
    {
        foreach (string raced in new[] { "Game.bin", "Game.cue" })
        {
            string root = Path.Combine(workDirectory, "bundle-race-" + Guid.NewGuid().ToString("N"));
            string pendingDir = Path.Combine(root, "pending");
            string finalDir = Path.Combine(root, "final");
            Directory.CreateDirectory(pendingDir);
            Directory.CreateDirectory(finalDir);

            string pendingCue = Path.Combine(pendingDir, "output.cue");
            string pendingBin = Path.Combine(pendingDir, "track.bin");
            File.WriteAllBytes(pendingBin, [1, 2, 3, 4]);
            File.WriteAllText(pendingCue, "FILE \"track.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n", Encoding.ASCII);

            string racedPath = Path.Combine(finalDir, raced);
            var contract = ExtractionOutputContract.Create(
                HakamiqChdTool.App.Models.Chd.ChdmanExtractionKind.ExtractCd, pendingCue, Path.Combine(finalDir, "Game.cue"));
            var validator = new ExtractionOutputBundleValidator
            {
                BeforeBundlePromotion = finalPath =>
                {
                    if (string.Equals(Path.GetFileName(finalPath), raced, StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllBytes(finalPath, [7, 7, 7]);
                    }
                }
            };

            bool finalized = validator.TryFinalize(contract, allowOverwrite: false, out _, out _);
            AssertFalse(finalized, "A file created concurrently at " + raced + " makes finalization fail.");
            AssertTrue(File.ReadAllBytes(racedPath).AsSpan().SequenceEqual(new byte[] { 7, 7, 7 }), "The concurrently created " + raced + " is untouched.");
            AssertTrue(File.Exists(pendingBin), "The produced BIN is back in the workspace after rollback (" + raced + ").");
            AssertEqual(1, Directory.GetFiles(finalDir).Length, "Nothing but the concurrent file is left in the output (" + raced + ").");

            Directory.Delete(root, recursive: true);
        }
    }

    // Replace mode: a file that appears at the final path after the existence check makes the move fail. That file was
    // not created by this call, so the failed promotion must leave it alone.
    private static void TestPromotionFailureNeverDeletesConcurrentFile(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "promote-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "pending"));
        Directory.CreateDirectory(Path.Combine(root, "final"));
        string pending = Path.Combine(root, "pending", "output.chd");
        string final = Path.Combine(root, "final", "Game.chd");
        File.WriteAllBytes(pending, [1, 2, 3, 4]);

        HakamiqChdTool.App.Core.Workflow.WorkflowOutputPathContract.BeforePromotionMoveForTests =
            path => File.WriteAllBytes(path, [7, 7, 7]);

        try
        {
            bool threw = false;
            try
            {
                HakamiqChdTool.App.Core.Workflow.WorkflowOutputPathContract.PromoteProducedFileToFinalLocation(pending, final);
            }
            catch (IOException)
            {
                threw = true;
            }

            AssertTrue(threw, "The promotion fails when a file appeared at the final path.");
            AssertEqual(3L, new FileInfo(final).Length, "The concurrently created file is untouched.");
            AssertTrue(File.Exists(pending), "The produced file stays in the workspace.");
        }
        finally
        {
            HakamiqChdTool.App.Core.Workflow.WorkflowOutputPathContract.BeforePromotionMoveForTests = null;
        }

        Directory.Delete(root, recursive: true);
    }
}
