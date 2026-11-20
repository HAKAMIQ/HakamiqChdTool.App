using System.IO;
using System.Security.Cryptography;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Real chdman and orchestrator: with "Skip existing output" on, a second run of the same disc leaves the
    // existing CHD untouched, reports it as skipped and never deletes the source.
    private static void TestSkipExistingOutputLeavesTheExistingChd()
    {
        string chdman = RequireBundledChdman();
        string root = Path.Combine(Path.GetTempPath(), "hct-skip-existing-" + Guid.NewGuid().ToString("N"));
        try
        {
            string output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            string cue = CreateCueBin(Path.Combine(root, "src"), "Sample Disc", 0x44);
            var settings = new AppSettings
            {
                UseCustomOutputRoot = true,
                CustomOutputRoot = output,
                SkipExistingOutput = true,
                DeleteSourceAfterVerifiedConversion = true
            };

            string chd = Path.Combine(output, "Sample Disc.chd");
            File.WriteAllBytes(chd, [0x01, 0x02, 0x03]);
            byte[] before = SHA256.HashData(File.ReadAllBytes(chd));
            DateTime writtenBefore = File.GetLastWriteTimeUtc(chd);

            WorkflowExecutionResult result = RunWorkflow(chdman, settings, cue, TaskActionCodes.ConvertToChd);

            AssertEqual(QueueItemTerminalOutcome.SkippedExists, result.TerminalSuccessOutcome, "An existing output is reported as skipped.");
            AssertTrue(SHA256.HashData(File.ReadAllBytes(chd)).AsSpan().SequenceEqual(before) && File.GetLastWriteTimeUtc(chd) == writtenBefore,
                "The existing CHD is not touched.");
            AssertTrue(File.Exists(cue) && File.Exists(Path.ChangeExtension(cue, ".bin")), "A skipped item never deletes its source.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
