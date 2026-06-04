using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.BinCueRescue;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowConversionStage(
    ChdConversionService conversion,
    ChdVerificationService verify,
    WorkflowPostProcessingStage postProcessingStage,
    ILogger log)
{
    private const string CancelledDetailKey = "LocOperation_Cancelled";
    private const string BinCueRescueFailedDetailKey = "LocBinCueRescue_PrepareFailed";
    private const string BinCueRescueRequiresPremiumDetailKey = "LocLicensing_BinCueRescueRequiresPremium";
    private const string ConversionFinalizingStageKey = "LocConversion_Finalizing";
    private const string VerifyingCreatedChdStageKey = "LocConversion_VerifyingCreatedChd";
    private const string SavingCreatedChdStageKey = "LocConversion_SavingCreatedChd";
    private const string ArchiveConversionSuccessDetailKey = "LocConversion_ArchiveSuccess";
    private const string ConversionSuccessDetailKey = "LocConversion_Success";
    private const string FinalOutputExistsDetailKey = "LocStatus_FinalOutputExists";

    private static readonly TimeSpan ConversionPulseInterval = TimeSpan.FromMilliseconds(750);

    private readonly ChdConversionService _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
    private readonly ChdVerificationService _verify = verify ?? throw new ArgumentNullException(nameof(verify));
    private readonly WorkflowPostProcessingStage _postProcessingStage = postProcessingStage ?? throw new ArgumentNullException(nameof(postProcessingStage));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        string inputPath,
        string detectedPlatform,
        double startingPercent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        CueRescueWorkflowAdapter? cueRescueWorkflowAdapter = null;

        try
        {
            AppSettings settings = ctx.Settings;
            IQueueItemStateSink sink = ctx.Sink;

            string conversionInputPath = inputPath;

            if (IsBinCueRescueInput(inputPath))
            {
                if (!ctx.CanUsePremiumFeature(PremiumFeature.AdvancedSafetyChecks))
                {
                    sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, BinCueRescueRequiresPremiumDetailKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Information(
                        "BIN/CUE rescue conversion blocked because the required premium feature is not licensed. Input={Input} Feature={Feature}",
                        inputPath,
                        PremiumFeature.AdvancedSafetyChecks);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.Unsupported,
                        BinCueRescueRequiresPremiumDetailKey,
                        null,
                        null);
                }

                cueRescueWorkflowAdapter = new CueRescueWorkflowAdapter();
                CueRescueWorkflowPrepareResult cueRescue = cueRescueWorkflowAdapter.TryPrepare(
                    inputPath,
                    AppPaths.ProcessTempRoot,
                    cancellationToken);

                if (cueRescue.IsFailed)
                {
                    string detail = string.IsNullOrWhiteSpace(cueRescue.Detail)
                        ? BinCueRescueFailedDetailKey
                        : cueRescue.Detail;

                    sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, detail);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Warning(
                        "BIN/CUE rescue preparation rejected conversion input. Input={Input} Detail={Detail}",
                        inputPath,
                        detail);

                    return WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, detail, null, null);
                }

                if (cueRescue.Applied && !string.IsNullOrWhiteSpace(cueRescue.EffectiveInputPath))
                {
                    conversionInputPath = cueRescue.EffectiveInputPath;
                }
            }

            if (string.Equals(Path.GetExtension(conversionInputPath), ".cue", StringComparison.OrdinalIgnoreCase))
            {
                if (!WorkflowPathUtilities.TryNormalizeCuePrimaryBinReference(
                        conversionInputPath,
                        out string cueReferenceFailureKey))
                {
                    sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, cueReferenceFailureKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Warning(
                        "CUE/BIN reference preflight rejected conversion input before chdman. Input={Input}; Detail={Detail}",
                        conversionInputPath,
                        cueReferenceFailureKey);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.Unsupported,
                        cueReferenceFailureKey,
                        null,
                        null);
                }

                FileIntegrityProbe.ProbeResult cueIntegrity = FileIntegrityProbe.Analyze(conversionInputPath);
                if (!cueIntegrity.LooksOk)
                {
                    sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, cueIntegrity.SummaryKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Warning(
                        "CUE/BIN dependency preflight rejected conversion input. Input={Input}; Summary={Summary}; Detail={Detail}",
                        conversionInputPath,
                        cueIntegrity.SummaryKey,
                        cueIntegrity.DetailKey);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.Unsupported,
                        cueIntegrity.SummaryKey,
                        null,
                        null);
                }
            }

            ChdWorkflowProfilePlan createPlan;
            try
            {
                createPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
                    conversionInputPath,
                    settings.IsoCreateCommandOverride,
                    ChdMediaContainerKind.DirectFile);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(CancelledDetailKey);
            }
            catch (Exception ex) when (IsExpectedConversionStageException(ex))
            {
                string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, detail);
                sink.ReportProgress(100, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, 100);

                _log.Warning(
                    ex,
                    "CHD profile planning rejected conversion input. Input={Input}",
                    inputPath);

                return WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, detail, null, null);
            }

            if (!createPlan.IsSupported)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, createPlan.FailureMessage);
                sink.ReportProgress(100, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, 100);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.Unsupported,
                    createPlan.FailureMessage,
                    null,
                    null);
            }

            double initialProgress = Math.Max(5, startingPercent);
            sink.ReportStage(QueueItemStage.ReadingFile, createPlan.StatusLine);
            sink.ReportProgress(initialProgress, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, initialProgress);

            string finalOutputPath = WorkflowPathUtilities.BuildFinalChdOutputPath(
                detectedPlatform,
                ctx.Snapshot.OriginalPath,
                inputPath,
                settings);

            string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(
                ctx.Snapshot.OriginalPath,
                inputPath,
                settings);

            WorkflowPathUtilities.TryBuildRegionFolderName(
                ctx.Snapshot.OriginalPath,
                inputPath,
                out string resolvedRegionFolder);

            _log.Information(
                "Resolved conversion output path. SourcePath={SourcePath} OutputRoot={OutputRoot} OrganizeByPlatform={OrganizeByPlatform} OrganizeByRegion={OrganizeByRegion} DetectedPlatform={DetectedPlatform} DetectedRegion={DetectedRegion} FinalOutputPath={FinalOutputPath} OutputExistsCheckPath={OutputExistsCheckPath}",
                ctx.Snapshot.OriginalPath,
                outputRoot,
                settings.OrganizeByPlatform,
                settings.OrganizeByRegion,
                string.IsNullOrWhiteSpace(detectedPlatform) ? "(none)" : detectedPlatform,
                string.IsNullOrWhiteSpace(resolvedRegionFolder) ? "(none)" : resolvedRegionFolder,
                finalOutputPath,
                finalOutputPath);

            sink.AttachArtifact(QueueItemArtifactKind.OutputFile, finalOutputPath);

            string? lastLogPath = null;

            if (settings.SkipExistingOutput && File.Exists(finalOutputPath))
            {
                sink.ReportTerminalSuccess(
                    QueueItemTerminalOutcome.SkippedExists,
                    FinalOutputExistsDetailKey);

                return WorkflowResultBuilder.Skipped(
                    QueueItemTerminalOutcome.SkippedExists,
                    FinalOutputExistsDetailKey,
                    finalOutputPath,
                    lastLogPath);
            }

            string pendingOutputPath;
            try
            {
                pendingOutputPath = WorkflowPathUtilities.BuildPendingOutputPath(
                    finalOutputPath,
                    inputPath,
                    ".chd",
                    outputRoot,
                    settings);

                WorkflowPendingOutputCleaner.MarkPendingRootHidden(pendingOutputPath);
                WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
            }
            catch (Exception ex) when (IsExpectedConversionStageException(ex))
            {
                string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, detail);

                WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
                WorkflowPathUtilities.TryCleanupEmptyFinalOutputDirectory(finalOutputPath);

                _log.Warning(
                    ex,
                    "Failed to prepare pending CHD output path. Input={Input} FinalOutputPath={FinalOutputPath}",
                    inputPath,
                    finalOutputPath);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedConvert,
                    detail,
                    finalOutputPath,
                    lastLogPath);
            }

            bool runPostConversionVerify = request.Verify && settings.VerifyAfterConversion;

            double conversionStartPercent = Math.Clamp(Math.Max(startingPercent, 20), 20, 88);
            double conversionEndPercent = runPostConversionVerify ? 92 : 98;

            if (conversionEndPercent <= conversionStartPercent)
            {
                conversionEndPercent = Math.Min(98, conversionStartPercent + 1);
            }

            sink.ClearRuntimeProgress();
            sink.ReportStage(QueueItemStage.Converting, createPlan.StatusLine);
            sink.ReportProgress(conversionStartPercent, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, conversionStartPercent);

            var conversionProgressGate = new WorkflowStageProgressGate(
                conversionStartPercent,
                conversionEndPercent,
                suspiciousFirstRawPercent: 70,
                suspiciousFirstWindow: TimeSpan.FromSeconds(10));

            using var conversionProgressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            void ReportConversionProgress(double percent)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                sink.ReportProgress(percent, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, percent);
            }

            Task conversionPulseTask = RunConversionPulseAsync(
                conversionProgressGate,
                ReportConversionProgress,
                _log,
                inputPath,
                conversionProgressCts.Token);

            var runtimeProgress = new WorkflowChdmanRuntimeProgressReporter(
                sink,
                createPlan.StatusLine,
                0);

            runtimeProgress.ReportCurrent();

            bool conversionFinalizingNotified = false;
            var convertProgress = new Progress<int>(value =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                runtimeProgress.ReportPercent(value);

                if (value >= 100 && !conversionFinalizingNotified)
                {
                    conversionFinalizingNotified = true;

                    WorkflowProgressContract.ReportFinalizing(
                        sink,
                        request,
                        QueueItemStage.Converting,
                        ConversionFinalizingStageKey,
                        WorkflowProgressContract.ConversionFinalizingPercent,
                        cancellationToken);
                }

                if (value >= 100)
                {
                    return;
                }

                if (!conversionProgressGate.TryMap(value, out double p))
                {
                    return;
                }

                ReportConversionProgress(p);
            });

            var conversionPerformanceProgress = new Progress<PerformanceSample>(sample =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    runtimeProgress.ReportPerformance(sample);
                }
            });

            ChdConversionResult conversionResult;
            try
            {
                try
                {
                    conversionResult = await _conversion.ConvertToChdAsync(
                        ctx.GetChdmanPath(),
                        conversionInputPath,
                        pendingOutputPath,
                        settings.MaxProcessorCount,
                        settings.EnableAutoResourceLimiter,
                        settings.ReservedLogicalCores,
                        settings.CompressionCodecs,
                        settings.HunkSizeBytes,
                        convertProgress,
                        onProcessStarted: null,
                        cancellationToken: cancellationToken,
                        performanceProgress: conversionPerformanceProgress,
                        isoCreateCommandOverride: settings.IsoCreateCommandOverride,
                        allowOverwriteOutput: !settings.SkipExistingOutput,
                        enableDiskSpaceGuard: settings.EnableDiskSpaceGuard).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                    return WorkflowExecutionResult.Cancelled(CancelledDetailKey, pendingOutputPath, lastLogPath);
                }
                catch (Exception ex) when (IsExpectedConversionStageException(ex))
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, detail);

                    _log.Warning(
                        ex,
                        "CHD conversion rejected input before chdman execution. Input={Input} Pending={Pending}",
                        inputPath,
                        pendingOutputPath);

                    return WorkflowResultBuilder.Failure(QueueItemFailureKind.FailedConvert, detail, pendingOutputPath, lastLogPath);
                }
            }
            finally
            {
                conversionProgressCts.Cancel();
                await conversionPulseTask.ConfigureAwait(false);
            }

            if (conversionResult.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(CancelledDetailKey, conversionResult.OutputPath, conversionResult.LogPath);
            }

            sink.AttachArtifact(QueueItemArtifactKind.LogFile, conversionResult.LogPath);
            lastLogPath = conversionResult.LogPath;

            if (!conversionResult.IsSuccess)
            {
                CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, conversionResult.Message);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedConvert,
                    conversionResult.Message,
                    pendingOutputPath,
                    lastLogPath);
            }

            if (conversionProgressGate.TryCompleteStage(out double completedConversionPercent))
            {
                ReportConversionProgress(completedConversionPercent);
            }

            sink.ClearRuntimeProgress();

            _log.Information(
                "Conversion process finished successfully. ExitCode={ExitCode}, Input={Input}, PendingChd={Pending}",
                conversionResult.ExitCode,
                inputPath,
                pendingOutputPath);

            if (runPostConversionVerify)
            {
                sink.ReportStage(QueueItemStage.Verifying, VerifyingCreatedChdStageKey);
                sink.ReportProgress(93, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, 93);

                var verifyProgress = new Progress<int>(value =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    double p = WorkflowPathUtilities.MapProgressRange(value, 93, 99);
                    sink.ReportProgress(p, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, p);
                });

                ChdVerificationResult verificationResult;
                try
                {
                    verificationResult = await _verify.VerifyAsync(
                        ctx.GetChdmanPath(),
                        pendingOutputPath,
                        verifyProgress,
                        onProcessStarted: null,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                    return WorkflowExecutionResult.Cancelled(CancelledDetailKey, pendingOutputPath, lastLogPath);
                }
                catch (Exception ex) when (IsExpectedConversionStageException(ex))
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, detail);

                    _log.Warning(
                        ex,
                        "Post-conversion CHD verification failed unexpectedly. Input={Input} Pending={Pending}",
                        inputPath,
                        pendingOutputPath);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedVerify,
                        detail,
                        pendingOutputPath,
                        lastLogPath);
                }

                if (verificationResult.WasCancelled || cancellationToken.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                    return WorkflowExecutionResult.Cancelled(CancelledDetailKey, pendingOutputPath, verificationResult.LogPath);
                }

                sink.AttachArtifact(QueueItemArtifactKind.LogFile, verificationResult.LogPath);
                lastLogPath = verificationResult.LogPath;

                if (!verificationResult.IsSuccess)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, verificationResult.Message);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedVerify,
                        verificationResult.Message,
                        pendingOutputPath,
                        lastLogPath);
                }
            }

            WorkflowProgressContract.ReportFinalizing(
                sink,
                request,
                QueueItemStage.Converting,
                SavingCreatedChdStageKey,
                WorkflowProgressContract.ConversionFinalizingPercent,
                cancellationToken);

            try
            {
                WorkflowPathUtilities.PromoteProducedFileToFinalLocation(pendingOutputPath, finalOutputPath);
            }
            catch (Exception ex) when (IsExpectedConversionStageException(ex))
            {
                CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, detail);

                _log.Warning(
                    ex,
                    "Failed to promote pending CHD output to final location. Pending={Pending} Final={Final}",
                    pendingOutputPath,
                    finalOutputPath);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedConvert,
                    detail,
                    finalOutputPath,
                    lastLogPath);
            }

            WorkflowPendingOutputCleaner.TryCleanupWorkspaceForPendingFile(pendingOutputPath);
            WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
            WorkflowPendingOutputCleaner.CleanupPendingRootIfEmpty(pendingOutputPath);

            PostConversionArtifactResult postProcessingResult;
            try
            {
                postProcessingResult = _postProcessingStage.RunAfterVerifiedConversion(settings, inputPath, finalOutputPath);
            }
            catch (Exception ex) when (IsNonFatalPostProcessingException(ex))
            {
                postProcessingResult = PostConversionArtifactResult.Empty;

                _log.Warning(
                    ex,
                    "Post-conversion artifact processing failed after successful CHD conversion. Base conversion remains successful. Input={Input} Output={Output}",
                    inputPath,
                    finalOutputPath);
            }

            sink.RecordPostConversionArtifacts(postProcessingResult);

            sink.AttachArtifact(QueueItemArtifactKind.OutputFile, finalOutputPath);
            sink.RecordInputOutputBytes(
                WorkflowPathUtilities.TryGetFileSize(inputPath),
                WorkflowPathUtilities.TryGetFileSize(finalOutputPath));

            string successDetail = request.IsArchive
                ? ArchiveConversionSuccessDetailKey
                : ConversionSuccessDetailKey;

            sink.ReportTerminalSuccess(QueueItemTerminalOutcome.Healthy, successDetail);
            sink.ReportProgress(100, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, 100);

            return WorkflowResultBuilder.Success(
                QueueItemTerminalOutcome.Healthy,
                successDetail,
                finalOutputPath,
                lastLogPath);
        }
        finally
        {
            cueRescueWorkflowAdapter?.Dispose();
        }
    }

    private static void CleanupPendingConversionOutput(
        string pendingOutputPath,
        string outputRoot,
        string finalOutputPath,
        AppSettings settings)
    {
        WorkflowPendingOutputCleaner.TryDeletePendingWorkspaceJobTree(pendingOutputPath, settings);
        WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
        WorkflowPathUtilities.TryCleanupEmptyFinalOutputDirectory(finalOutputPath);
    }

    private static async Task RunConversionPulseAsync(
        WorkflowStageProgressGate gate,
        Action<double> reportProgress,
        ILogger log,
        string inputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(reportProgress);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            using var timer = new PeriodicTimer(ConversionPulseInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (gate.TryAdvanceSoftly(out double p))
                {
                    reportProgress(p);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        if (cancellationToken.IsCancellationRequested)
        {
            log.Debug(
                "Conversion progress pulse stopped because cancellation was requested. Input={Input}",
                inputPath);
        }
    }

    private static bool IsBinCueRescueInput(string path) =>
        string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedConversionStageException(Exception ex) =>
        ex is InvalidDataException
        or InvalidOperationException
        or NotSupportedException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException
        or System.Security.SecurityException;

    private static bool IsNonFatalPostProcessingException(Exception ex) =>
        ex is not OperationCanceledException;
}