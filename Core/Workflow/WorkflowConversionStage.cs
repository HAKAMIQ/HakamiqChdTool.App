using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Chd.Commands;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Conversion;
using HakamiqChdTool.App.Services.Storage;
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
    ConversionSessionGuard sessionGuard,
    StorageTopologyService storageTopology,
    ConversionSafetyPolicy safetyPolicy,
    ConversionPerformanceReportFactory performanceReportFactory,
    ILogger log)
{
    private const string CancelledDetailKey = "LocOperation_Cancelled";
    private const string BinCueRescueFailedDetailKey = "LocBinCueRescue_PrepareFailed";
    private const string ConversionFinalizingStageKey = "LocConversion_Finalizing";
    private const string VerifyingCreatedChdStageKey = "LocConversion_VerifyingCreatedChd";
    private const string SavingCreatedChdStageKey = "LocConversion_SavingCreatedChd";
    private const string ArchiveConversionSuccessDetailKey = "LocConversion_ArchiveSuccess";
    private const string ConversionSuccessDetailKey = "LocConversion_Success";
    private const string FinalOutputExistsDetailKey = "LocStatus_FinalOutputExists";
    private const string DeepHashSafetyStageKey = "LocConversionSafety_DeepHashChecking";
    private const string StorageTemperatureAbortKey = "LocStorageTemperature_Abort";

    private static readonly TimeSpan ConversionPulseInterval = TimeSpan.FromMilliseconds(750);

    private readonly ChdConversionService _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
    private readonly ChdVerificationService _verify = verify ?? throw new ArgumentNullException(nameof(verify));
    private readonly WorkflowPostProcessingStage _postProcessingStage = postProcessingStage ?? throw new ArgumentNullException(nameof(postProcessingStage));
    private readonly ConversionSessionGuard _sessionGuard = sessionGuard ?? throw new ArgumentNullException(nameof(sessionGuard));
    private readonly StorageTopologyService _storageTopology = storageTopology ?? throw new ArgumentNullException(nameof(storageTopology));
    private readonly ConversionSafetyPolicy _safetyPolicy = safetyPolicy ?? throw new ArgumentNullException(nameof(safetyPolicy));
    private readonly ConversionPerformanceReportFactory _performanceReportFactory = performanceReportFactory ?? throw new ArgumentNullException(nameof(performanceReportFactory));
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

        HakamiqChdTool.App.Services.BinCueRescue.CueRescueWorkflowAdapter? cueRescueWorkflowAdapter = null;

        try
        {
            AppSettings settings = ctx.Settings;
            IQueueItemStateSink sink = ctx.Sink;

            string conversionInputPath = inputPath;
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
            string? pendingOutputPath = null;
            bool allowConstrainedAbsoluteCueReference = false;

            if (cancellationToken.IsCancellationRequested)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(CancelledDetailKey, finalOutputPath, lastLogPath);
            }

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

            if (IsBinCueRescueInput(inputPath))
            {
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
                        "Failed to prepare BIN/CUE pending workspace before temporary CUE generation. Input={Input} FinalOutputPath={FinalOutputPath}",
                        inputPath,
                        finalOutputPath);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedConvert,
                        detail,
                        finalOutputPath,
                        lastLogPath);
                }

                string? operationWorkspaceDirectory = Path.GetDirectoryName(pendingOutputPath);
                if (string.IsNullOrWhiteSpace(operationWorkspaceDirectory))
                {
                    TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, BinCueRescueFailedDetailKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedConvert,
                        BinCueRescueFailedDetailKey,
                        pendingOutputPath,
                        lastLogPath);
                }

                cueRescueWorkflowAdapter = new HakamiqChdTool.App.Services.BinCueRescue.CueRescueWorkflowAdapter();
                HakamiqChdTool.App.Services.BinCueRescue.CueRescueWorkflowPrepareResult cueRescue = cueRescueWorkflowAdapter.TryPrepare(
                    inputPath,
                    operationWorkspaceDirectory,
                    cancellationToken,
                    HakamiqChdTool.App.Services.DiscLayout.DiscLayoutTrustMode.StrictEvidence,
                    allowConstrainedAbsoluteBinFallback: true);

                if (cueRescue.IsFailed)
                {
                    string detail = string.IsNullOrWhiteSpace(cueRescue.Detail)
                        ? BinCueRescueFailedDetailKey
                        : cueRescue.Detail;

                    TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, detail);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Warning(
                        "BIN/CUE rescue preparation rejected conversion input. Input={Input} Detail={Detail}",
                        inputPath,
                        detail);

                    return WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, detail, pendingOutputPath, lastLogPath);
                }

                if (cueRescue.Applied && !string.IsNullOrWhiteSpace(cueRescue.EffectiveInputPath))
                {
                    conversionInputPath = cueRescue.EffectiveInputPath;
                    allowConstrainedAbsoluteCueReference = !string.IsNullOrWhiteSpace(cueRescue.TempDirectoryToCleanup);
                }
            }

            if (string.Equals(Path.GetExtension(conversionInputPath), ".cue", StringComparison.OrdinalIgnoreCase))
            {
                if (!WorkflowPathUtilities.TryNormalizeCuePrimaryBinReference(
                        conversionInputPath,
                        allowConstrainedAbsoluteCueReference,
                        out string cueReferenceFailureKey))
                {
                    sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, cueReferenceFailureKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Warning(
                        "CUE/BIN reference preflight rejected conversion input before chdman. Input={Input}; Detail={Detail}",
                        conversionInputPath,
                        cueReferenceFailureKey);

                    TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.Unsupported,
                        cueReferenceFailureKey,
                        pendingOutputPath,
                        lastLogPath);
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

                    TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.Unsupported,
                        cueIntegrity.SummaryKey,
                        pendingOutputPath,
                        lastLogPath);
                }
            }

            ChdWorkflowProfilePlan createPlan;
            try
            {
                createPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
                    conversionInputPath,
                    settings.IsoCreateCommandOverride,
                    ChdMediaContainerKind.DirectFile,
                    settings.ChdPlatformProfileId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(CancelledDetailKey, pendingOutputPath, lastLogPath);
            }
            catch (Exception ex) when (IsExpectedConversionStageException(ex))
            {
                bool sourceReadFailure = IsSourceReadFailureException(ex);
                string detail = sourceReadFailure
                    ? ConversionSafetyPolicy.InputReadFailureMessageKey
                    : RuntimeDiagnosticFormatter.SummarizeException(ex);

                QueueItemFailureKind failureKind = sourceReadFailure
                    ? QueueItemFailureKind.SourceUnreadable
                    : QueueItemFailureKind.Unsupported;

                sink.ReportTerminalFailure(failureKind, detail);
                sink.ReportProgress(100, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, 100);

                _log.Warning(
                    ex,
                    "CHD profile planning rejected conversion input. Input={Input}",
                    inputPath);

                TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                return WorkflowResultBuilder.Failure(failureKind, detail, pendingOutputPath, lastLogPath);
            }

            if (!createPlan.IsSupported)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, createPlan.FailureMessage);
                sink.ReportProgress(100, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, 100);

                TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.Unsupported,
                    createPlan.FailureMessage,
                    pendingOutputPath,
                    lastLogPath);
            }

            bool hadInputReadWarning = false;
            if (settings.EnableDeepIntegrityCheck)
            {
                sink.ReportStage(QueueItemStage.ReadingFile, DeepHashSafetyStageKey);
                sink.ReportProgress(Math.Max(5, startingPercent), indeterminate: true);
                WorkflowPathUtilities.RaiseProgress(request, Math.Max(5, startingPercent));

                DeepHashAnalysisResult deepHashSafetyResult = await _safetyPolicy
                    .RunDeepHashInputReadCheckAsync(conversionInputPath, cancellationToken)
                    .ConfigureAwait(false);

                ConversionSafetyDecision safetyDecision = _safetyPolicy.EvaluateDeepHashResult(deepHashSafetyResult);
                hadInputReadWarning = safetyDecision.HadInputReadWarning;

                if (!safetyDecision.CanStartConversion)
                {
                    sink.ReportTerminalFailure(QueueItemFailureKind.SourceUnreadable, safetyDecision.UserMessageKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    _log.Warning(
                        "Conversion blocked before chdman by deep hash safety policy. Input={Input}; FailureCode={FailureCode}",
                        conversionInputPath,
                        safetyDecision.FailureCode);

                    TryCleanupPreparedPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.SourceUnreadable,
                        safetyDecision.UserMessageKey,
                        pendingOutputPath,
                        lastLogPath);
                }
            }

            double initialProgress = Math.Max(5, startingPercent);
            sink.ReportStage(QueueItemStage.ReadingFile, createPlan.StatusLine);
            sink.ReportProgress(initialProgress, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, initialProgress);

            if (string.IsNullOrWhiteSpace(pendingOutputPath))
            {
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
            }

            StorageTopologySnapshot topology = _storageTopology.Analyze(
                conversionInputPath,
                pendingOutputPath,
                finalOutputPath);

            if (_safetyPolicy.ShouldWarnSameSourceAndOutputVolume(topology))
            {
                sink.ReportStage(QueueItemStage.ReadingFile, ConversionSafetyPolicy.SameDiskWarningMessageKey);

                _log.Warning(
                    "Storage topology warning before conversion. Source and final output are on the same volume. Source={Source}; Pending={Pending}; Final={Final}; SourceRoot={SourceRoot}; PendingRoot={PendingRoot}; FinalRoot={FinalRoot}",
                    conversionInputPath,
                    pendingOutputPath,
                    finalOutputPath,
                    topology.SourceRoot,
                    topology.PendingRoot,
                    topology.FinalOutputRoot);
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
            using var conversionSafetyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            void ReportConversionProgress(double percent)
            {
                if (cancellationToken.IsCancellationRequested || conversionSafetyCts.IsCancellationRequested)
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

            StorageDeviceIdentity monitoredDevice = topology.SourceIsExternal
                ? topology.SourceDevice
                : topology.OutputIsExternal
                    ? topology.OutputDevice
                    : topology.SourceDevice;

            await using ConversionSessionScope conversionSession = _sessionGuard.BeginCriticalConversionSession(
                monitoredDevice,
                SelectTemperaturePolicy(monitoredDevice),
                ReportSessionNotification,
                cancellationToken);

            void ReportSessionNotification(ConversionSessionNotification notification)
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(notification.MessageKey))
                    {
                        return;
                    }

                    sink.ReportStage(QueueItemStage.Converting, notification.MessageKey);

                    if (notification.Severity == StorageHealthSeverity.Abort
                        && !conversionSafetyCts.IsCancellationRequested)
                    {
                        _log.Warning(
                            "Storage temperature abort requested. Device={Device}; Temperature={Temperature}",
                            monitoredDevice.DisplayName,
                            notification.CurrentTemperatureCelsius);

                        conversionSafetyCts.Cancel();
                    }
                }
                catch (Exception ex) when (IsNonFatalPostProcessingException(ex))
                {
                    _log.Debug(ex, "Storage temperature notification could not be reported to the queue sink.");
                }
            }

            bool conversionFinalizingNotified = false;
            var convertProgress = new Progress<int>(value =>
            {
                if (cancellationToken.IsCancellationRequested || conversionSafetyCts.IsCancellationRequested)
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
                    if (!cancellationToken.IsCancellationRequested && !conversionSafetyCts.IsCancellationRequested)
                    {
                        runtimeProgress.ReportPerformance(sample);
                    }
            });

            ChdConversionResult conversionResult;
            TimeSpan verifyDuration = TimeSpan.Zero;
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
                        cancellationToken: conversionSafetyCts.Token,
                        performanceProgress: conversionPerformanceProgress,
                        isoCreateCommandOverride: settings.IsoCreateCommandOverride,
                        allowOverwriteOutput: !settings.SkipExistingOutput,
                        enableDiskSpaceGuard: settings.EnableDiskSpaceGuard,
                        performanceMode: settings.PerformanceMode,
                        priorityMode: settings.ChdmanPriorityMode,
                        platformProfileId: settings.ChdPlatformProfileId).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                    return WorkflowExecutionResult.Cancelled(CancelledDetailKey, pendingOutputPath, lastLogPath);
                }
                catch (OperationCanceledException) when (conversionSafetyCts.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, StorageTemperatureAbortKey);
                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedConvert,
                        StorageTemperatureAbortKey,
                        pendingOutputPath,
                        lastLogPath);
                }
                catch (Exception ex) when (IsExpectedConversionStageException(ex))
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    bool sourceReadFailure = IsSourceReadFailureException(ex);
                    string detail = sourceReadFailure
                        ? ConversionSafetyPolicy.ChdmanInputReadWarningMessageKey
                        : RuntimeDiagnosticFormatter.SummarizeException(ex);
                    QueueItemFailureKind failureKind = sourceReadFailure
                        ? QueueItemFailureKind.SourceUnreadable
                        : QueueItemFailureKind.FailedConvert;

                    hadInputReadWarning = hadInputReadWarning || sourceReadFailure;
                    sink.ReportTerminalFailure(failureKind, detail);

                    _log.Warning(
                        ex,
                        "CHD conversion rejected input before chdman execution. Input={Input} Pending={Pending}",
                        inputPath,
                        pendingOutputPath);

                    return WorkflowResultBuilder.Failure(failureKind, detail, pendingOutputPath, lastLogPath);
                }
            }
            finally
            {
                conversionProgressCts.Cancel();
                await conversionPulseTask.ConfigureAwait(false);
            }

            if (conversionResult.WasCancelled && conversionSafetyCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                sink.ReportTerminalFailure(QueueItemFailureKind.FailedConvert, StorageTemperatureAbortKey);
                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedConvert,
                    StorageTemperatureAbortKey,
                    conversionResult.OutputPath,
                    conversionResult.LogPath);
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
                if (conversionResult.Status == ChdConversionStatus.SkippedOutputExists)
                {
                    sink.ReportTerminalSuccess(
                        QueueItemTerminalOutcome.SkippedExists,
                        FinalOutputExistsDetailKey);
                    sink.ReportProgress(100, indeterminate: false);
                    WorkflowPathUtilities.RaiseProgress(request, 100);

                    return WorkflowResultBuilder.Skipped(
                        QueueItemTerminalOutcome.SkippedExists,
                        FinalOutputExistsDetailKey,
                        finalOutputPath,
                        lastLogPath);
                }

                CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                bool chdmanInputReadFailure = _safetyPolicy.LooksLikeChdmanInputReadFailure(conversionResult);
                string failureMessage = chdmanInputReadFailure
                    ? ConversionSafetyPolicy.ChdmanInputReadWarningMessageKey
                    : conversionResult.Message;

                if (chdmanInputReadFailure)
                {
                    hadInputReadWarning = true;
                    _log.Warning(
                        "chdman output indicates an input read problem. Input={Input}; Pending={Pending}; ExitCode={ExitCode}; LogPath={LogPath}",
                        inputPath,
                        pendingOutputPath,
                        conversionResult.ExitCode,
                        conversionResult.LogPath);
                }

                QueueItemFailureKind failureKind = chdmanInputReadFailure
                    ? QueueItemFailureKind.SourceUnreadable
                    : QueueItemFailureKind.FailedConvert;

                sink.ReportTerminalFailure(failureKind, failureMessage);

                return WorkflowResultBuilder.Failure(
                    failureKind,
                    failureMessage,
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
                    if (cancellationToken.IsCancellationRequested || conversionSafetyCts.IsCancellationRequested)
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
                        cancellationToken: conversionSafetyCts.Token,
                        priorityMode: settings.ChdmanPriorityMode).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                    return WorkflowExecutionResult.Cancelled(CancelledDetailKey, pendingOutputPath, lastLogPath);
                }
                catch (OperationCanceledException) when (conversionSafetyCts.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, StorageTemperatureAbortKey);
                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedVerify,
                        StorageTemperatureAbortKey,
                        pendingOutputPath,
                        lastLogPath);
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

                if (verificationResult.WasCancelled && conversionSafetyCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

                    sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, StorageTemperatureAbortKey);
                    return WorkflowResultBuilder.Failure(
                        QueueItemFailureKind.FailedVerify,
                        StorageTemperatureAbortKey,
                        pendingOutputPath,
                        verificationResult.LogPath);
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

                verifyDuration = verificationResult.Duration;
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

            cueRescueWorkflowAdapter?.Dispose();
            cueRescueWorkflowAdapter = null;

            WorkflowPendingOutputCleaner.CleanupPendingRootIfEmpty(pendingOutputPath);
            WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);

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
            long inputBytes = ResolveConversionInputBytes(inputPath, conversionResult);
            long outputBytes = WorkflowPathUtilities.TryGetFileSize(finalOutputPath);
            sink.RecordInputOutputBytes(inputBytes, outputBytes);

            ConversionPerformanceReport performanceReport = _performanceReportFactory.Create(
                inputBytes,
                outputBytes,
                conversionResult,
                verifyDuration,
                topology,
                conversionSession.PowerGuardEnabled,
                conversionSession.TemperatureAvailable,
                conversionSession.TemperatureCapability,
                conversionSession.MaxTemperatureCelsius,
                hadInputReadWarning);

            sink.RecordConversionPerformanceReport(performanceReport);

            _log.Information(
                "Conversion performance report. InputBytes={InputBytes}; OutputBytes={OutputBytes}; CompressionRatio={CompressionRatio}; ChdmanDuration={ChdmanDuration}; VerifyDuration={VerifyDuration}; NumProcessors={NumProcessors}; Compression={Compression}; RequestedPreset={RequestedPreset}; ResolvedCompression={ResolvedCompression}; EffectiveCompression={EffectiveCompression}; SameAsMameDefault={SameAsMameDefault}; HunkSize={HunkSize}; SourceAndOutputSameVolume={SourceAndOutputSameVolume}; SourceExternal={SourceExternal}; OutputExternal={OutputExternal}; PowerGuardEnabled={PowerGuardEnabled}; TemperatureAvailable={TemperatureAvailable}; TemperatureCapability={TemperatureCapability}; MaxTemperature={MaxTemperature}; Explanation={Explanation}; CompressionTruthNote={CompressionTruthNote}",
                performanceReport.InputBytes,
                performanceReport.OutputBytes,
                performanceReport.CompressionRatio,
                performanceReport.ChdmanDuration,
                performanceReport.VerifyDuration,
                performanceReport.NumProcessors,
                performanceReport.CompressionCodecs,
                performanceReport.RequestedCompressionPreset,
                performanceReport.ResolvedCompressionCodecs,
                performanceReport.EffectiveCompressionCodecs,
                performanceReport.EffectiveCompressionSameAsMameDefault,
                performanceReport.HunkSizeBytes,
                performanceReport.SourceAndOutputSameVolume,
                performanceReport.SourceIsExternal,
                performanceReport.OutputIsExternal,
                performanceReport.PowerGuardEnabled,
                performanceReport.TemperatureAvailable,
                performanceReport.TemperatureCapability,
                performanceReport.MaxTemperatureCelsius,
                performanceReport.CompressionExplanationKey,
                performanceReport.CompressionTruthNoteKey);

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


    private static long ResolveConversionInputBytes(string inputPath, ChdConversionResult conversionResult)
    {
        if (conversionResult.LogicalInputBytes > 0)
        {
            return conversionResult.LogicalInputBytes;
        }

        return WorkflowPathUtilities.TryGetFileSize(inputPath);
    }

    private static void TryCleanupPreparedPendingConversionOutput(
        string? pendingOutputPath,
        string outputRoot,
        string finalOutputPath,
        AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(pendingOutputPath))
        {
            return;
        }

        CleanupPendingConversionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);
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

    private static StorageTemperaturePolicy SelectTemperaturePolicy(StorageDeviceIdentity device)
    {
        return device.DeviceKind is StorageDeviceKind.SataSsd or StorageDeviceKind.NvmeSsd
            ? DefaultStorageTemperaturePolicies.ExternalSsd
            : DefaultStorageTemperaturePolicies.ExternalHdd;
    }

    private static bool IsExpectedConversionStageException(Exception ex) =>
        ex is InvalidDataException
        or InvalidOperationException
        or NotSupportedException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException
        or System.Security.SecurityException;

    private static bool IsSourceReadFailureException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonFatalPostProcessingException(Exception ex) =>
        ex is not OperationCanceledException;
}
