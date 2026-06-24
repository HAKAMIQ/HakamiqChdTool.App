using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Workflow.Extraction;
using HakamiqChdTool.App.Core.Workflow.Progress;
using HakamiqChdTool.App.Core.Chd.Commands;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowExtractionStage(
    ChdInfoService chdInfo,
    ChdConversionService conversion,
    ChdVerificationService verify,
    ILogger log)
{
    private const string CancelledDetailKey = "LocOperation_Cancelled";
    private const string ReadingMediaTypeStageKey = "LocStatus_ReadingChdMediaType";
    private const string FinalizingExtractedOutputStageKey = "LocStatus_FinalizingExtractedOutput";
    private const string MovingExtractedOutputStageKey = "LocStatus_MovingExtractedOutputFile";
    private const string VerifyingSourceChdStageKey = "LocStatus_ExtractionDoneVerifyingSourceChd";
    private const string ExtractCompletedCueBinKey = "LocStatus_ExtractCompletedCueBin";
    private const string ExtractCompletedIsoKey = "LocStatus_ExtractCompletedIso";
    private const string ExtractCompletedImgKey = "LocStatus_ExtractCompletedImg";
    private const string ExtractCompletedRawKey = "LocStatus_ExtractCompletedRaw";
    private const string ExtractionCompletedSuccessKey = "LocExtraction_Success";
    private const string OutputFileExistsDetailKey = "LocStatus_OutputFileExists";

    private readonly ChdInfoService _chdInfo = chdInfo ?? throw new ArgumentNullException(nameof(chdInfo));
    private readonly ChdConversionService _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
    private readonly ChdVerificationService _verify = verify ?? throw new ArgumentNullException(nameof(verify));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly IMetadataAwareChdExtractionPolicy _extractionPolicy = new MetadataAwareChdExtractionPolicy();
    private readonly IRestoreTargetPolicy _restoreTargetPolicy = new RestoreTargetPolicy();
    private readonly ExtractionOutputBundleValidator _outputBundleValidator = new();

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        string chdPath,
        string detectedPlatform,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(chdPath);

        AppSettings settings = ctx.Settings;
        IQueueItemStateSink sink = ctx.Sink;
        string currentDetectedPlatform = detectedPlatform;
        string? lastLogPath = null;

        sink.ReportStage(QueueItemStage.ReadingFile, ReadingMediaTypeStageKey);
        sink.ReportProgress(6, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 6);

        ChdInfoResult infoResult = await _chdInfo.ReadInfoAsync(
            ctx.GetChdmanPath(),
            chdPath,
            onProcessStarted: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        sink.AttachArtifact(QueueItemArtifactKind.LogFile, infoResult.LogPath);
        lastLogPath = infoResult.LogPath;

        if (infoResult.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
            return WorkflowExecutionResult.Cancelled(CancelledDetailKey, chdPath, infoResult.LogPath);
        }

        if (!infoResult.IsSuccess)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Failed, infoResult.Message);
            return WorkflowResultBuilder.Failure(QueueItemFailureKind.Failed, infoResult.Message, chdPath, lastLogPath);
        }

        PlatformDetectionResult detection = await WorkflowPathUtilities
            .ApplyChdMediaDetectionAsync(sink, chdPath, infoResult, cancellationToken)
            .ConfigureAwait(false);

        currentDetectedPlatform = ResolveExtractionPlatformName(
            detection.PlatformName,
            ctx.Snapshot.OriginalPath,
            chdPath);

        PlatformDetectionResult extractionContextDetection = PlatformDetectionResult.Create(
            currentDetectedPlatform,
            detection.ConfidenceLabel,
            detection.ConfidenceScore,
            detection.Reason);

        MetadataAwareChdExtractionDecision extractionDecision = _extractionPolicy.Resolve(
            new MetadataAwareChdExtractionRequest(
                infoResult.MediaType,
                chdPath,
                ctx.Snapshot.OriginalPath,
                extractionContextDetection,
                infoResult.LogicalBytes));

        if (!extractionDecision.IsSupported
            || extractionDecision.ExtractionKind == ChdmanExtractionKind.None
            || string.IsNullOrWhiteSpace(extractionDecision.OutputExtension))
        {
            string unknownMediaTypeDetail = string.IsNullOrWhiteSpace(extractionDecision.FailureMessageKey)
                ? ChdWorkflowProfilePlanner.UnknownChdExtractionMessageKey
                : extractionDecision.FailureMessageKey;

            sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, unknownMediaTypeDetail);
            sink.ReportProgress(100, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, 100);

            _log.Warning(
                "CHD extraction blocked by MetadataAwareChdExtractionPolicy. SourcePath={SourcePath}; MediaType={MediaType}; MetadataKind={MetadataKind}; ReasonCode={ReasonCode}",
                chdPath,
                infoResult.MediaType,
                extractionDecision.MetadataKind,
                extractionDecision.ReasonCode);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.Unsupported,
                unknownMediaTypeDetail,
                chdPath,
                lastLogPath);
        }

        ChdmanExtractionKind extractKind = extractionDecision.ExtractionKind;
        string outExt = extractionDecision.OutputExtension;

        string finalOutputPath = WorkflowPathUtilities.BuildFinalExtractOutputPath(
            currentDetectedPlatform,
            ctx.Snapshot.OriginalPath,
            chdPath,
            outExt,
            settings);

        string outputRoot = WorkflowPathUtilities.ResolveBaseOutputRoot(
            ctx.Snapshot.OriginalPath,
            chdPath,
            settings);

        WorkflowPathUtilities.TryBuildRegionFolderName(
            ctx.Snapshot.OriginalPath,
            chdPath,
            out string resolvedRegionFolder);

        _log.Information(
            "Resolved extraction output path. SourcePath={SourcePath} OutputRoot={OutputRoot} OrganizeByPlatform={OrganizeByPlatform} OrganizeByRegion={OrganizeByRegion} DetectedPlatform={DetectedPlatform} DetectedRegion={DetectedRegion} FinalOutputPath={FinalOutputPath} OutputExistsCheckPath={OutputExistsCheckPath}",
            chdPath,
            outputRoot,
            settings.OrganizeByPlatform,
            settings.OrganizeByRegion,
            string.IsNullOrWhiteSpace(currentDetectedPlatform) ? "(none)" : currentDetectedPlatform,
            string.IsNullOrWhiteSpace(resolvedRegionFolder) ? "(none)" : resolvedRegionFolder,
            finalOutputPath,
            finalOutputPath);

        sink.AttachArtifact(QueueItemArtifactKind.OutputFile, finalOutputPath);
        WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);

        if (cancellationToken.IsCancellationRequested)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
            return WorkflowExecutionResult.Cancelled(CancelledDetailKey, finalOutputPath, lastLogPath);
        }

        if (settings.SkipExistingOutput && File.Exists(finalOutputPath))
        {
            ExtractionOutputKind existingOutputKind = ResolveExpectedFinalOutputKind(extractionDecision);
            if (!_outputBundleValidator.TryValidateExistingFinal(
                    existingOutputKind,
                    finalOutputPath,
                    out _,
                    out string existingOutputFailureKey))
            {
                string detail = string.IsNullOrWhiteSpace(existingOutputFailureKey)
                    ? "LocWorkflow_ExtractedCueBinInvalid"
                    : existingOutputFailureKey;

                sink.ReportTerminalFailure(QueueItemFailureKind.FailedExtract, detail);
                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedExtract,
                    detail,
                    finalOutputPath,
                    lastLogPath);
            }

            sink.ReportTerminalSuccess(
                QueueItemTerminalOutcome.SkippedExists,
                OutputFileExistsDetailKey);

            return WorkflowResultBuilder.Skipped(
                QueueItemTerminalOutcome.SkippedExists,
                OutputFileExistsDetailKey,
                finalOutputPath,
                lastLogPath);
        }

        string pendingOutputPath;
        try
        {
            pendingOutputPath = WorkflowPathUtilities.BuildPendingOutputPath(
                finalOutputPath,
                chdPath,
                outExt,
                outputRoot,
                settings);

            WorkflowPendingOutputCleaner.MarkPendingRootHidden(pendingOutputPath);
        }
        catch (Exception ex) when (IsExpectedExtractionStageException(ex))
        {
            string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
            sink.ReportTerminalFailure(QueueItemFailureKind.FailedExtract, detail);

            WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
            WorkflowPathUtilities.TryCleanupEmptyFinalOutputDirectory(finalOutputPath);

            _log.Warning(
                ex,
                "Failed to prepare pending extraction output path. SourcePath={SourcePath} FinalOutputPath={FinalOutputPath}",
                chdPath,
                finalOutputPath);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedExtract,
                detail,
                finalOutputPath,
                lastLogPath);
        }

        RestoreTargetDecision restoreTarget = _restoreTargetPolicy.Resolve(
            new RestoreTargetRequest(
                extractionDecision,
                pendingOutputPath,
                finalOutputPath));

        ExtractionOutputContract outputContract = ExtractionOutputContract.Create(
            restoreTarget.FinalizationKind,
            pendingOutputPath,
            finalOutputPath);

        string extractionStageLine = string.IsNullOrWhiteSpace(extractionDecision.WarningMessageKey)
            ? extractionDecision.StatusLine
            : extractionDecision.WarningMessageKey;

        if (extractionDecision.IsLegacyWrongProfile)
        {
            _log.Warning(
                "Wrong-profile / Legacy CHD detected. SourcePath={SourcePath}; MediaType={MediaType}; Platform={Platform}; ReasonCode={ReasonCode}; Recommendation=Extract to ISO then rebuild using createdvd profile.",
                chdPath,
                infoResult.MediaType,
                currentDetectedPlatform,
                extractionDecision.ReasonCode);
        }

        sink.ReportStage(QueueItemStage.Extracting, extractionStageLine);
        sink.ReportProgress(14, indeterminate: true);
        WorkflowPathUtilities.RaiseProgress(request, 14);

        var runtimeProgress = new WorkflowChdmanRuntimeProgressReporter(
            sink,
            extractionStageLine,
            infoResult.LogicalBytes ?? 0L);

        runtimeProgress.ReportCurrent();

        bool extractionFinalizingNotified = false;
        int lastExtractionPercent = 0;
        object extractionProgressGate = new();

        void ReportReliableExtractionPercent(int value)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            value = Math.Clamp(value, 0, 100);
            bool shouldReportFinalizing;

            lock (extractionProgressGate)
            {
                if (value < lastExtractionPercent)
                {
                    return;
                }

                lastExtractionPercent = value;
                shouldReportFinalizing = value >= 100 && !extractionFinalizingNotified;
                if (shouldReportFinalizing)
                {
                    extractionFinalizingNotified = true;
                }
            }

            runtimeProgress.ReportPercent(value);

            if (shouldReportFinalizing)
            {
                WorkflowProgressContract.ReportFinalizing(
                    sink,
                    request,
                    QueueItemStage.Extracting,
                    FinalizingExtractedOutputStageKey,
                    96,
                    cancellationToken);
            }

            double p = WorkflowPathUtilities.MapProgressRange(value, 14, 96);
            sink.ReportProgress(p, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, p);
        }

        var extractProgress = new Progress<int>(ReportReliableExtractionPercent);

        var extractionPerformanceProgress = new Progress<PerformanceSample>(sample =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            runtimeProgress.ReportPerformance(sample);

            long logicalBytes = infoResult.LogicalBytes ?? 0L;
            if (logicalBytes > 0 && sample.OutputBytes > 0)
            {
                int derivedPercent = (int)Math.Clamp(
                    Math.Round(sample.OutputBytes * 100d / logicalBytes, MidpointRounding.AwayFromZero),
                    0d,
                    100d);

                ReportReliableExtractionPercent(derivedPercent);
            }
        });

        using var estimatedRuntimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task estimatedRuntimeProgressTask = MonitorEstimatedExtractionProgressAsync(
            outputContract,
            runtimeProgress,
            ReportReliableExtractionPercent,
            infoResult.LogicalBytes ?? 0L,
            estimatedRuntimeCts.Token);

        ChdConversionResult conversionResult;
        try
        {
            conversionResult = await _conversion.ConvertToChdAsync(
                ctx.GetChdmanPath(),
                chdPath,
                restoreTarget.ChdmanOutputPath,
                settings.MaxProcessorCount,
                settings.EnableAutoResourceLimiter,
                settings.ReservedLogicalCores,
                settings.CompressionCodecs,
                settings.HunkSizeBytes,
                extractProgress,
                onProcessStarted: null,
                cancellationToken: cancellationToken,
                extractionKind: extractKind,
                isoCreateCommandOverride: settings.IsoCreateCommandOverride,
                expectedOutputBytes: infoResult.LogicalBytes,
                allowOverwriteOutput: !settings.SkipExistingOutput,
                performanceProgress: extractionPerformanceProgress,
                enableDiskSpaceGuard: settings.EnableDiskSpaceGuard,
                performanceMode: settings.PerformanceMode,
                priorityMode: settings.ChdmanPriorityMode,
                extractionMetadataDecisionConfirmed: true,
                extractCdCueOutputPath: restoreTarget.ExtractCdCueOutputPath,
                extractCdBinOutputPath: restoreTarget.ExtractCdBinOutputPath,
                verifyExtractCdCueBinContract: restoreTarget.VerifyExtractCdCueBinContract).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopEstimatedExtractionProgressAsync(estimatedRuntimeCts, estimatedRuntimeProgressTask).ConfigureAwait(false);
            CleanupPendingExtractionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
            return WorkflowExecutionResult.Cancelled(
                CancelledDetailKey,
                pendingOutputPath,
                lastLogPath);
        }
        catch (Exception ex) when (IsExpectedExtractionStageException(ex))
        {
            await StopEstimatedExtractionProgressAsync(estimatedRuntimeCts, estimatedRuntimeProgressTask).ConfigureAwait(false);
            CleanupPendingExtractionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

            string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
            sink.ReportTerminalFailure(QueueItemFailureKind.FailedExtract, detail);

            _log.Warning(
                ex,
                "CHD extraction command failed before producing a valid output. SourcePath={SourcePath} PendingOutputPath={PendingOutputPath}",
                chdPath,
                pendingOutputPath);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedExtract,
                detail,
                pendingOutputPath,
                lastLogPath);
        }

        await StopEstimatedExtractionProgressAsync(estimatedRuntimeCts, estimatedRuntimeProgressTask).ConfigureAwait(false);

        if (conversionResult.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            CleanupPendingExtractionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
            return WorkflowExecutionResult.Cancelled(
                CancelledDetailKey,
                conversionResult.OutputPath,
                conversionResult.LogPath);
        }

        sink.AttachArtifact(QueueItemArtifactKind.LogFile, conversionResult.LogPath);
        lastLogPath = conversionResult.LogPath;

        if (!conversionResult.IsSuccess)
        {
            if (conversionResult.Status == ChdConversionStatus.SkippedOutputExists)
            {
                sink.ReportTerminalSuccess(
                    QueueItemTerminalOutcome.SkippedExists,
                    OutputFileExistsDetailKey);
                sink.ReportProgress(100, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, 100);

                return WorkflowResultBuilder.Skipped(
                    QueueItemTerminalOutcome.SkippedExists,
                    OutputFileExistsDetailKey,
                    finalOutputPath,
                    lastLogPath);
            }

            CleanupPendingExtractionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

            sink.ReportTerminalFailure(QueueItemFailureKind.FailedExtract, conversionResult.Message);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedExtract,
                conversionResult.Message,
                pendingOutputPath,
                lastLogPath);
        }

        sink.ClearRuntimeProgress();

        WorkflowProgressContract.ReportFinalizing(
            sink,
            request,
            QueueItemStage.Extracting,
            MovingExtractedOutputStageKey,
            WorkflowProgressContract.ExtractionFinalizingPercent,
            cancellationToken);

        if (!_outputBundleValidator.TryFinalize(
                outputContract,
                out ExtractionOutputBundle finalOutputBundle,
                out string finalizeFailureMessageKey))
        {
            string detail = string.IsNullOrWhiteSpace(finalizeFailureMessageKey)
                ? "LocWorkflow_ExtractedCueBinInvalid"
                : finalizeFailureMessageKey;

            sink.ReportTerminalFailure(QueueItemFailureKind.FailedExtract, detail);
            CleanupPendingExtractionOutput(pendingOutputPath, outputRoot, finalOutputPath, settings);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedExtract,
                detail,
                finalOutputPath,
                lastLogPath);
        }

        WorkflowPendingOutputCleaner.TryCleanupWorkspaceForPendingFile(pendingOutputPath);
        WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);

        if (request.Verify)
        {
            sink.ReportStage(QueueItemStage.Verifying, VerifyingSourceChdStageKey);
            sink.ReportProgress(96, indeterminate: true);
            WorkflowPathUtilities.RaiseProgress(request, 96);

            ChdVerificationResult extractedFlowVerify;
            try
            {
                extractedFlowVerify = await _verify.VerifyAsync(
                    ctx.GetChdmanPath(),
                    chdPath,
                    progress: null,
                    onProcessStarted: null,
                    cancellationToken: cancellationToken,
                    priorityMode: settings.ChdmanPriorityMode).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(
                    CancelledDetailKey,
                    finalOutputPath,
                    lastLogPath);
            }
            catch (Exception ex) when (IsExpectedExtractionStageException(ex))
            {
                string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, detail);

                _log.Warning(
                    ex,
                    "Source CHD verification failed unexpectedly after extraction. SourcePath={SourcePath} FinalOutputPath={FinalOutputPath}",
                    chdPath,
                    finalOutputPath);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedVerify,
                    detail,
                    finalOutputPath,
                    lastLogPath);
            }

            if (extractedFlowVerify.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(
                    CancelledDetailKey,
                    finalOutputPath,
                    extractedFlowVerify.LogPath);
            }

            sink.AttachArtifact(QueueItemArtifactKind.LogFile, extractedFlowVerify.LogPath);
            lastLogPath = extractedFlowVerify.LogPath;

            if (!extractedFlowVerify.IsSuccess)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, extractedFlowVerify.Message);

                return WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.FailedVerify,
                    extractedFlowVerify.Message,
                    finalOutputPath,
                    lastLogPath);
            }
        }

        sink.AttachArtifact(QueueItemArtifactKind.OutputFile, finalOutputBundle.PrimaryPath);
        sink.RecordInputOutputBytes(
            WorkflowPathUtilities.TryGetFileSize(chdPath),
            finalOutputBundle.TotalBytes);

        string extractedDetail = string.IsNullOrWhiteSpace(restoreTarget.SuccessMessageKey)
            ? BuildExtractionSuccessDetail(restoreTarget.SuccessKind)
            : restoreTarget.SuccessMessageKey;

        sink.ReportTerminalSuccess(QueueItemTerminalOutcome.Extracted, extractedDetail);
        sink.ReportProgress(100, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 100);

        return WorkflowResultBuilder.Success(
            QueueItemTerminalOutcome.Extracted,
            extractedDetail,
            finalOutputBundle.PrimaryPath,
            lastLogPath);
    }

    private static async Task MonitorEstimatedExtractionProgressAsync(
        ExtractionOutputContract outputContract,
        WorkflowChdmanRuntimeProgressReporter runtimeProgress,
        Action<int> reportReliableExtractionPercent,
        long expectedOutputBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputContract);
        ArgumentNullException.ThrowIfNull(runtimeProgress);
        ArgumentNullException.ThrowIfNull(reportReliableExtractionPercent);

        var estimator = new ExtractionOutputByteProgressEstimator(outputContract, expectedOutputBytes);
        long lastObservedBytes = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            WorkflowRuntimeProgressSample sample = estimator.Capture();
            runtimeProgress.ReportEstimatedRuntime(sample);

            if (sample.CurrentBytes > lastObservedBytes && sample.Percent is double percent)
            {
                lastObservedBytes = sample.CurrentBytes;
                reportReliableExtractionPercent((int)Math.Floor(percent));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task StopEstimatedExtractionProgressAsync(
        CancellationTokenSource cancellationTokenSource,
        Task progressTask)
    {
        ArgumentNullException.ThrowIfNull(cancellationTokenSource);
        ArgumentNullException.ThrowIfNull(progressTask);

        try
        {
            cancellationTokenSource.Cancel();
            await progressTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void CleanupPendingExtractionOutput(
        string pendingOutputPath,
        string outputRoot,
        string finalOutputPath,
        AppSettings settings)
    {
        WorkflowPendingOutputCleaner.TryDeletePendingWorkspaceJobTree(pendingOutputPath, settings);
        WorkflowPendingOutputCleaner.TryCleanupLegacyOutputRootPending(outputRoot);
        WorkflowPathUtilities.TryCleanupEmptyFinalOutputDirectory(finalOutputPath);
    }

    private static string ResolveExtractionPlatformName(
        string? detectedPlatform,
        string originalPath,
        string chdPath)
    {
        if (PlatformDetectionService.IsActionablePlatformName(detectedPlatform))
        {
            return detectedPlatform!.Trim();
        }

        PlatformDetectionResult originalPathDetection = PlatformDetectionService.Detect(originalPath);
        if (PlatformDetectionService.IsActionablePlatformName(originalPathDetection.PlatformName))
        {
            return originalPathDetection.PlatformName.Trim();
        }

        PlatformDetectionResult chdPathDetection = PlatformDetectionService.Detect(chdPath);
        if (PlatformDetectionService.IsActionablePlatformName(chdPathDetection.PlatformName))
        {
            return chdPathDetection.PlatformName.Trim();
        }

        return string.Empty;
    }

    private static ExtractionOutputKind ResolveExpectedFinalOutputKind(
        MetadataAwareChdExtractionDecision extractionDecision)
    {
        ArgumentNullException.ThrowIfNull(extractionDecision);

        if (extractionDecision.RestoreTargetMode == ChdRestoreTargetMode.LegacyCdProfileToIso)
        {
            return ExtractionOutputKind.SingleFile;
        }

        return extractionDecision.ExtractionKind == ChdmanExtractionKind.ExtractCd
            ? ExtractionOutputKind.CueBinBundle
            : ExtractionOutputKind.SingleFile;
    }

    private static string BuildExtractionSuccessDetail(ChdmanExtractionKind kind) => kind switch
    {
        ChdmanExtractionKind.ExtractCd => ExtractCompletedCueBinKey,
        ChdmanExtractionKind.ExtractDvd => ExtractCompletedIsoKey,
        ChdmanExtractionKind.ExtractHd => ExtractCompletedImgKey,
        ChdmanExtractionKind.ExtractRaw => ExtractCompletedRawKey,
        _ => ExtractionCompletedSuccessKey
    };

    private static bool IsExpectedExtractionStageException(Exception ex) =>
        ex is InvalidDataException
        or InvalidOperationException
        or NotSupportedException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException
        or System.Security.SecurityException;
}
