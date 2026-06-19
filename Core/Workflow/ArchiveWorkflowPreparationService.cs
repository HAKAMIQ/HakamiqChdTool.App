using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class ArchiveWorkflowPreparationService(
    ArchiveExtractionService archive,
    ILogger log)
{
    private const string UnsupportedSelectedOperationKey = "LocArchive_UnsupportedSelectedOperation";
    private const string PreparingConvertibleDiscImageKey = "LocArchive_PreparingConvertibleDiscImage";
    private const string PasswordRequiredKey = "LocArchive_PasswordRequired";
    private const string PlatformDetectionFallbackReasonKey = "LocArchive_PlatformDetectionFallbackReason";
    private const string PreparedConvertibleDiscImageKey = "LocArchive_PreparedConvertibleDiscImage";
    private const string OperationCancelledKey = "LocOperation_Cancelled";

    private readonly ArchiveExtractionService _archive = archive ?? throw new ArgumentNullException(nameof(archive));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly CleanupService _cleanup = new();

    public async Task<WorkflowPreparationResult> PrepareUnpackThenConvertAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        CancellationToken cancellationToken)
    {
        QueueItemSnapshot snap = ctx.Snapshot;
        IQueueItemStateSink sink = ctx.Sink;

        if (!string.Equals(snap.RequestedAction, TaskActionCodes.StageArchiveForConversion, StringComparison.Ordinal))
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, UnsupportedSelectedOperationKey);
            return WorkflowPreparationResult.Failed(
                WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, UnsupportedSelectedOperationKey, null, null));
        }

        sink.ReportStage(QueueItemStage.Extracting, PreparingConvertibleDiscImageKey);
        sink.ReportProgress(5, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 5);

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(sink);
        }

        string extractionDirectory = WorkflowPathUtilities.BuildArchiveExtractionDirectory(snap.OriginalPath);
        bool preparedSuccessfully = false;
        sink.AttachArtifact(QueueItemArtifactKind.TempWorkingDirectory, extractionDirectory);

        try
        {
            var extractionProgress = new Progress<int>(value =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                double p = WorkflowPathUtilities.MapProgressRange(value, 6, 20);
                sink.ReportProgress(p, indeterminate: false);
                WorkflowPathUtilities.RaiseProgress(request, p);
            });

            ArchiveExtractionResult extractionResult;
            try
            {
                extractionResult = await _archive.ExtractFirstConvertibleDiscImageAsync(
                        snap.OriginalPath,
                        extractionDirectory,
                        extractionProgress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(sink);
            }
            catch (Exception ex) when (IsExpectedArchiveInputException(ex))
            {
                string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                sink.ReportTerminalFailure(QueueItemFailureKind.Failed, detail);

                _log.Warning(
                    ex,
                    "Archive preparation rejected input before chdman execution. Archive={Archive}",
                    snap.OriginalPath);

                return WorkflowPreparationResult.Failed(
                    WorkflowResultBuilder.Failure(QueueItemFailureKind.Failed, detail, null, null));
            }

            if (extractionResult.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                return Cancelled(sink);
            }

            if (!extractionResult.IsSuccess)
            {
                QueueItemFailureKind kind = extractionResult.RequiresPassword
                    ? QueueItemFailureKind.PasswordRequired
                    : IsUnsupportedArchiveContentMessage(extractionResult.Message)
                        ? QueueItemFailureKind.Unsupported
                        : QueueItemFailureKind.Failed;

                string extractionDetail = extractionResult.RequiresPassword
                    ? PasswordRequiredKey
                    : extractionResult.Message;

                sink.ReportTerminalFailure(kind, extractionDetail);
                return WorkflowPreparationResult.Failed(
                    WorkflowResultBuilder.Failure(kind, extractionDetail, null, null));
            }

            string extractedPath = extractionResult.ExtractedPath;
            QueueInputClassification extractedClassification = QueueInputClassifier.Classify(extractedPath);

            if (!extractedClassification.IsConvertibleDiscImage)
            {
                const string noConvertibleImageDetail = "LocArchive_NoConvertibleDiscImage";
                sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, noConvertibleImageDetail);
                return WorkflowPreparationResult.Failed(
                    WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, noConvertibleImageDetail, null, null));
            }

            ChdWorkflowProfilePlan archivePlan;
            try
            {
                archivePlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
                    extractedPath,
                    ctx.Settings.IsoCreateCommandOverride,
                    ChdMediaContainerKind.Archive,
                    ctx.Settings.ChdPlatformProfileId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(sink);
            }
            catch (Exception ex) when (IsExpectedArchiveInputException(ex))
            {
                string detail = RuntimeDiagnosticFormatter.SummarizeException(ex);
                sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, detail);

                _log.Warning(
                    ex,
                    "Archive-extracted image profile planning failed. Archive={Archive} Extracted={Extracted}",
                    snap.OriginalPath,
                    extractedPath);

                return WorkflowPreparationResult.Failed(
                    WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, detail, null, null));
            }

            if (!archivePlan.IsSupported)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, archivePlan.FailureMessage);
                return WorkflowPreparationResult.Failed(
                    WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, archivePlan.FailureMessage, null, null));
            }

            sink.ReportStage(QueueItemStage.ReadingFile, archivePlan.StatusLine);
            sink.ReportWorkingPathPromotion(extractedPath, TaskActionCodes.ConvertToChd);

            PlatformDetectionResult postExtractDetection;
            try
            {
                postExtractDetection = await Task.Run(
                        () => PlatformDetectionService.Detect(extractedPath),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(sink);
            }
            catch (Exception ex) when (IsExpectedArchiveInputException(ex))
            {
                _log.Warning(
                    ex,
                    "Platform detection failed for archive-extracted image; continuing with unknown platform. Archive={Archive} Extracted={Extracted}",
                    snap.OriginalPath,
                    extractedPath);

                postExtractDetection = PlatformDetectionResult.Create(
                    "Unknown Platform",
                    string.Empty,
                    10,
                    PlatformDetectionFallbackReasonKey);
            }

            sink.RecordPlatformDetection(postExtractDetection.PlatformName, postExtractDetection.Reason);
            sink.ReportStage(QueueItemStage.Extracting, PreparedConvertibleDiscImageKey);
            sink.ReportProgress(20, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, 20);

            _log.Information(
                "Archive container prepared for CHD conversion. Archive={Archive}, Extracted={Extracted}, Platform={Platform}",
                snap.OriginalPath,
                extractedPath,
                postExtractDetection.PlatformName);

            preparedSuccessfully = true;

            return WorkflowPreparationResult.Prepared(new WorkflowPreparedInput(
                extractedPath,
                TaskActionCodes.ConvertToChd,
                postExtractDetection.PlatformName,
                extractionDirectory,
                20,
                string.Empty,
                string.Empty));
        }
        finally
        {
            if (!preparedSuccessfully)
            {
                CleanupFailedPreparationTemp(ctx, extractionDirectory);
            }
        }
    }

    private void CleanupFailedPreparationTemp(
        ChdWorkflowTaskContext ctx,
        string? extractionDirectory)
    {
        if (!ctx.Settings.DeleteTemporaryExtraction
            || string.IsNullOrWhiteSpace(extractionDirectory)
            || !AppPaths.IsPathUnderProcessTempRoot(extractionDirectory))
        {
            return;
        }

        CleanupStats tempCleanup = _cleanup.DeleteDirectoryTree(extractionDirectory);
        ctx.Sink.AddCleanupDeletedBytes(tempCleanup.DeletedBytes);

        _log.Debug(
            "Archive preparation cleanup completed for failed/cancelled preparation. TempDirectory={TempDirectory} Bytes={Bytes} Files={Files}",
            extractionDirectory,
            tempCleanup.DeletedBytes,
            tempCleanup.DeletedFiles);
    }

    private static bool IsUnsupportedArchiveContentMessage(string? message) =>
        string.Equals(message, "LocArchive_NoConvertibleDiscImage", StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.UnsupportedDiscImageMessageResourceKey, StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.MultipleConvertibleImageSetsMessageResourceKey, StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.EmptyArchiveMessageResourceKey, StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.DescriptorMissingDependenciesMessageResourceKey, StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.DescriptorUnsafeReferenceMessageResourceKey, StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.DescriptorHasNoTrackReferencesMessageResourceKey, StringComparison.Ordinal)
        || string.Equals(message, ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey, StringComparison.Ordinal);

    private static WorkflowPreparationResult Cancelled(IQueueItemStateSink sink)
    {
        sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, OperationCancelledKey);
        return WorkflowPreparationResult.Failed(WorkflowExecutionResult.Cancelled(OperationCancelledKey));
    }

    private static bool IsExpectedArchiveInputException(Exception ex) =>
        ex is InvalidDataException
        or InvalidOperationException
        or NotSupportedException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException;
}
