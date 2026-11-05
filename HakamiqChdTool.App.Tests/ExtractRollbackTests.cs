using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.PostProcessing;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Real chdman, real orchestrator. When the CUE/BIN finalize fails, whatever sits at the final CUE path was not produced
    // by this job (the user's set, restored by the rollback or never touched). "Delete failed output" must not delete it.
    private static void TestFailedExtractFinalizeNeverDeletesForeignCueBin(string workDirectory)
    {
        string chdman = Path.Combine(AppContext.BaseDirectory, "Tools", "chdman.exe");
        if (!File.Exists(chdman))
        {
            throw new TestSkippedException("Bundled chdman is not available next to the test runner.");
        }

        // The restore policy reads platform hints from the whole path, and the shared work directory name contains "Ps2";
        // a neutral root keeps this a plain CD restore to CUE/BIN.
        _ = workDirectory;
        string root = Path.Combine(Path.GetTempPath(), "hct-extract-rollback-" + Guid.NewGuid().ToString("N"));
        try
        {
            RunFailedExtractFinalizeScenario(root, chdman);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RunFailedExtractFinalizeScenario(string root, string chdman)
    {
        string raw = Path.Combine(root, "raw");
        string src = Path.Combine(root, "src");
        string output = Path.Combine(root, "out");
        Directory.CreateDirectory(raw);
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(output);

        string rawCue = Path.Combine(raw, "Game.cue");
        File.WriteAllBytes(Path.Combine(raw, "Game.bin"), Enumerable.Repeat((byte)0x11, 2352 * 300).ToArray());
        File.WriteAllText(rawCue, "FILE \"Game.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
        string chd = Path.Combine(src, "Game.chd");
        ChdmanCliRunner.Result create = ChdmanCliRunner.ExecuteAsync(
                chdman, ["createcd", "-i", rawCue, "-o", chd], parseProgressPercent: false, progress: null, onProcessStarted: null)
            .GetAwaiter().GetResult();
        AssertEqual(0, create.ExitCode, "Test setup: chdman createcd succeeded. " + create.StandardError);

        // The user's own set at the final location (Game.cue -> Mine.bin). A directory named like the extracted track
        // (Game.bin) makes the finalize fail before anything is moved.
        string userCue = Path.Combine(output, "Game.cue");
        string userBin = Path.Combine(output, "Mine.bin");
        byte[] userBinBytes = Enumerable.Repeat((byte)0x55, 2352 * 10).ToArray();
        const string userCueText = "FILE \"Mine.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n";
        File.WriteAllBytes(userBin, userBinBytes);
        File.WriteAllText(userCue, userCueText);
        Directory.CreateDirectory(Path.Combine(output, "Game.bin"));

        var settings = new AppSettings
        {
            UseCustomOutputRoot = true,
            CustomOutputRoot = output,
            OrganizeByPlatform = false,
            OrganizeByRegion = false,
            SkipExistingOutput = false,
            DeleteFailedOutput = true,
            DeleteSourceAfterVerifiedExtraction = false
        };

        var orchestrator = new ChdWorkflowOrchestrator(
            new ChdConversionService(),
            new ArchiveExtractionService(),
            new ChdVerificationService(),
            new ChdInfoService(),
            new PostConversionArtifactService(),
            new LoggerConfiguration().CreateLogger());

        var context = new ChdWorkflowTaskContext
        {
            Snapshot = new QueueItemSnapshot
            {
                ItemId = Guid.NewGuid(),
                OriginalPath = chd,
                SourcePath = chd,
                FileName = "Game.chd",
                DetectedPlatform = string.Empty,
                RequestedAction = TaskActionCodes.RestoreDiscImageFromChd
            },
            Sink = NoOpQueueItemStateSink.Instance,
            Settings = settings,
            GetChdmanPath = () => chdman
        };

        WorkflowExecutionResult result = orchestrator.ProcessAsync(
                new ChdTaskRequest { InputPath = chd, Verify = false, Options = context },
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEqual(WorkflowExecutionOutcome.Failure, result.Outcome, "The extraction reports failure. Detail=" + result.StatusDetail);
        AssertTrue(File.Exists(userCue) && File.ReadAllText(userCue) == userCueText, "The user's CUE survives failed-output cleanup.");
        AssertTrue(File.Exists(userBin) && File.ReadAllBytes(userBin).AsSpan().SequenceEqual(userBinBytes), "The user's BIN survives failed-output cleanup.");
        AssertTrue(File.Exists(chd), "The source CHD is untouched.");
    }
}
