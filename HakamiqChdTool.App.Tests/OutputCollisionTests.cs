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
using System.Security.Cryptography;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Real chdman and orchestrator. Job B pauses after its ownership decision; job A then publishes and loses its
    // source before B resumes, deterministically exposing a stale check-then-promote decision without timing delays.
    private static void TestOverwriteNeverReplacesAnotherSourcesOutputFromThisSession(string workDirectory)
    {
        string chdman = RequireBundledChdman();
        _ = workDirectory;
        string root = Path.Combine(Path.GetTempPath(), "hct-collision-conv-" + Guid.NewGuid().ToString("N"));
        try
        {
            string output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            string cueA = CreateCueBin(Path.Combine(root, "A"), "Disc 1", 0x11);
            string cueB = CreateCueBin(Path.Combine(root, "B"), "Disc 1", 0x22);

            var settings = new AppSettings
            {
                UseCustomOutputRoot = true,
                CustomOutputRoot = output,
                OrganizeByPlatform = false,
                OrganizeByRegion = false,
                SkipExistingOutput = false,
                DeleteSourceAfterVerifiedConversion = true
            };

            string chd = Path.Combine(output, "Disc 1.chd");
            string expectedA = CreateChd(chdman, Path.Combine(root, "expected-A"), "Disc 1", 0x11);
            string expectedB = CreateChd(chdman, Path.Combine(root, "expected-B"), "Disc 1", 0x22);
            using var bAtDecision = new ManualResetEventSlim(false);
            using var releaseB = new ManualResetEventSlim(false);
            WorkflowSessionOutputRegistry.BeforePromotionReservationForTests = source =>
            {
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(cueB), StringComparison.OrdinalIgnoreCase))
                {
                    bAtDecision.Set();
                    if (!releaseB.Wait(TimeSpan.FromSeconds(60)))
                    {
                        throw new TimeoutException("Job B was not released after job A published and cleaned its source.");
                    }
                }
            };

            Task<WorkflowExecutionResult> secondTask = Task.Run(() => RunWorkflow(chdman, settings, cueB, TaskActionCodes.ConvertToChd));
            Task<WorkflowExecutionResult>? firstTask = null;
            try
            {
                AssertTrue(bAtDecision.Wait(TimeSpan.FromSeconds(60)), "Job B reached the synchronized final-path decision.");
                firstTask = Task.Run(() => RunWorkflow(chdman, settings, cueA, TaskActionCodes.ConvertToChd));
                WorkflowExecutionResult first = firstTask.GetAwaiter().GetResult();
                AssertEqual(WorkflowExecutionOutcome.Success, first.Outcome, "Job A publishes and verifies the shared CHD while B is paused.");
                AssertFalse(File.Exists(cueA), "Job A's verified source was cleaned after its output was published.");
            }
            finally
            {
                releaseB.Set();
                WorkflowSessionOutputRegistry.BeforePromotionReservationForTests = null;
            }

            AssertTrue(firstTask is not null, "Job A ran after job B reached the synchronization point.");
            Task.WaitAll(secondTask);
            WorkflowExecutionResult second = secondTask.Result;
            AssertTrue(File.Exists(chd), "Exactly one verified output exists at the shared final path.");
            AssertFalse(File.Exists(cueA), "The first source remains cleaned only because its output remains intact.");
            AssertFalse(File.Exists(Path.ChangeExtension(cueA, ".bin")), "The first source's referenced BIN was cleaned only after publication.");
            bool outputBelongsToA = SHA256.HashData(File.ReadAllBytes(chd)).AsSpan()
                .SequenceEqual(SHA256.HashData(File.ReadAllBytes(expectedA)));
            bool outputBelongsToB = SHA256.HashData(File.ReadAllBytes(chd)).AsSpan()
                .SequenceEqual(SHA256.HashData(File.ReadAllBytes(expectedB)));
            bool rejectedSourceRemains = File.Exists(cueB) && File.Exists(Path.ChangeExtension(cueB, ".bin"));
            bool wasRejected = second.TerminalSuccessOutcome == QueueItemTerminalOutcome.SkippedExists;
            AssertTrue(outputBelongsToA && rejectedSourceRemains && wasRejected,
                "The final output must match job A, job B's source must remain, and B must be skipped. "
                + "OutputMatchesA=" + outputBelongsToA
                + "; OutputMatchesB=" + outputBelongsToB
                + "; RejectedSourceExists=" + rejectedSourceRemains
                + "; BOutcome=" + second.Outcome
                + "; BTerminal=" + second.TerminalSuccessOutcome
                + "; BDetail=" + second.StatusDetail);

            // Re-running the same source still replaces its own output, as overwrite mode promises.
            string cueC = CreateCueBin(Path.Combine(root, "C"), "Disc 2", 0x33);
            settings.DeleteSourceAfterVerifiedConversion = false;
            AssertEqual(WorkflowExecutionOutcome.Success, RunWorkflow(chdman, settings, cueC, TaskActionCodes.ConvertToChd).Outcome, "Test setup: job C converted.");
            WorkflowExecutionResult again = RunWorkflow(chdman, settings, cueC, TaskActionCodes.ConvertToChd);
            AssertEqual(QueueItemTerminalOutcome.Healthy, again.TerminalSuccessOutcome, "The same source replaces its own output again.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The same collision on the extraction side: A\Game.chd and B\Game.chd both restore to Out\Game.cue/.bin.
    private static void TestExtractOverwriteNeverReplacesAnotherSourcesOutputFromThisSession(string workDirectory)
    {
        string chdman = RequireBundledChdman();
        _ = workDirectory;
        string root = Path.Combine(Path.GetTempPath(), "hct-collision-ext-" + Guid.NewGuid().ToString("N"));
        try
        {
            string output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            string chdA = CreateChd(chdman, Path.Combine(root, "A"), "Game", 0x11);
            string chdB = CreateChd(chdman, Path.Combine(root, "B"), "Game", 0x22);

            var settings = new AppSettings
            {
                UseCustomOutputRoot = true,
                CustomOutputRoot = output,
                OrganizeByPlatform = false,
                OrganizeByRegion = false,
                SkipExistingOutput = false,
                DeleteSourceAfterVerifiedExtraction = true
            };

            WorkflowExecutionResult first = RunWorkflow(chdman, settings, chdA, TaskActionCodes.RestoreDiscImageFromChd);
            AssertEqual(WorkflowExecutionOutcome.Success, first.Outcome, "Test setup: job A extracted. Detail=" + first.StatusDetail);
            AssertFalse(File.Exists(chdA), "Test setup: job A deleted its verified source.");
            byte[] firstBin = File.ReadAllBytes(Path.Combine(output, "Game.bin"));

            WorkflowExecutionResult second = RunWorkflow(chdman, settings, chdB, TaskActionCodes.RestoreDiscImageFromChd);
            AssertTrue(File.ReadAllBytes(Path.Combine(output, "Game.bin")).AsSpan().SequenceEqual(firstBin), "Job B did not replace job A's BIN. Outcome=" + second.Outcome);
            AssertEqual(QueueItemTerminalOutcome.SkippedExists, second.TerminalSuccessOutcome, "Job B is reported as skipped because the output exists.");
            AssertTrue(File.Exists(chdB), "Job B's source is kept.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RequireBundledChdman()
    {
        string chdman = Path.Combine(AppContext.BaseDirectory, "Tools", "chdman.exe");
        if (!File.Exists(chdman))
        {
            throw new TestSkippedException("Bundled chdman is not available next to the test runner.");
        }

        return chdman;
    }

    private static string CreateCueBin(string directory, string stem, byte fill)
    {
        Directory.CreateDirectory(directory);
        string cue = Path.Combine(directory, stem + ".cue");
        File.WriteAllBytes(Path.Combine(directory, stem + ".bin"), Enumerable.Repeat(fill, 2352 * 300).ToArray());
        File.WriteAllText(cue, "FILE \"" + stem + ".bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
        return cue;
    }

    private static string CreateChd(string chdman, string directory, string stem, byte fill)
    {
        string cue = CreateCueBin(Path.Combine(directory, "raw"), stem, fill);
        string chd = Path.Combine(directory, stem + ".chd");
        ChdmanCliRunner.Result create = ChdmanCliRunner.ExecuteAsync(
                chdman, ["createcd", "-i", cue, "-o", chd], parseProgressPercent: false, progress: null, onProcessStarted: null)
            .GetAwaiter().GetResult();
        AssertEqual(0, create.ExitCode, "Test setup: chdman createcd succeeded. " + create.StandardError);
        return chd;
    }

    private static WorkflowExecutionResult RunWorkflow(string chdman, AppSettings settings, string inputPath, string requestedAction)
    {
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
                OriginalPath = inputPath,
                SourcePath = inputPath,
                FileName = Path.GetFileName(inputPath),
                DetectedPlatform = string.Empty,
                RequestedAction = requestedAction
            },
            Sink = NoOpQueueItemStateSink.Instance,
            Settings = settings,
            GetChdmanPath = () => chdman
        };

        return orchestrator.ProcessAsync(
                new ChdTaskRequest { InputPath = inputPath, Verify = true, Options = context },
                CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}
