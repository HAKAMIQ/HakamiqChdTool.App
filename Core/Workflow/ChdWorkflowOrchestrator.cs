using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.PostProcessing;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

public sealed partial class ChdWorkflowOrchestrator : IChdWorkflowOrchestrator
{
    private readonly WorkflowInputPreparationService _inputPreparation;
    private readonly WorkflowPreflightStage _preflightStage;
    private readonly WorkflowConversionStage _conversionStage;
    private readonly WorkflowExtractionStage _extractionStage;
    private readonly WorkflowVerificationStage _verificationStage;
    private readonly WorkflowCleanupStage _cleanupStage;
    private readonly StressMonitorService _stressMonitor = new();
    private readonly ILogger _log;

    public ChdWorkflowOrchestrator(
        ChdConversionService conversion,
        ArchiveExtractionService archive,
        ChdVerificationService verify,
        ChdInfoService chdInfo,
        PostConversionArtifactService postConversionArtifacts,
        ILogger log)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(chdInfo);
        ArgumentNullException.ThrowIfNull(postConversionArtifacts);
        ArgumentNullException.ThrowIfNull(log);

        _log = log;

        CleanupService cleanup = new();

        WorkflowPostProcessingStage postProcessingStage = new(postConversionArtifacts, log);

        _inputPreparation = new WorkflowInputPreparationService(archive, log);
        _preflightStage = new WorkflowPreflightStage(log);
        _conversionStage = new WorkflowConversionStage(conversion, verify, postProcessingStage, log);
        _extractionStage = new WorkflowExtractionStage(chdInfo, conversion, verify, log);
        _verificationStage = new WorkflowVerificationStage(chdInfo, verify, log);
        _cleanupStage = new WorkflowCleanupStage(cleanup, log);
    }

    public async Task<WorkflowExecutionResult> ProcessAsync(ChdTaskRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options is not ChdWorkflowTaskContext ctx)
        {
            throw new ArgumentException("ChdTaskRequest.Options must be ChdWorkflowTaskContext.", nameof(request));
        }

        if (ctx.Mode == ChdWorkflowMode.VerifyExistingChd)
        {
            return await ProcessVerifyExistingWorkflowAsync(request, ctx, ct).ConfigureAwait(false);
        }

        try
        {
            return await ProcessSingleTaskCoreAsync(request, ctx, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ctx.Sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, CancelledDetailKey);

            _log.Information(
                "Workflow cancelled. Input={Input}",
                request.InputPath);

            return WorkflowExecutionResult.Cancelled(CancelledDetailKey);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Workflow failed for {Input}",
                request.InputPath);

            throw;
        }
    }

    private async Task<WorkflowExecutionResult> ProcessSingleTaskCoreAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        CancellationToken cancellationToken)
    {
        QueueItemSnapshot snap = ctx.Snapshot;
        AppSettings settings = ctx.Settings;
        IQueueItemStateSink sink = ctx.Sink;

        string cancelledDetail = CancelledDetailKey;
        string? tempDirectoryToCleanup = null;
        string? failedOutputCandidate = null;
        string? lastOutputPath = string.Empty;
        double lastProgressPercent = 0;
        bool alwaysCleanupTempDirectory = false;
        CancellationTokenSource? stressCts = null;
        Task? stressTask = null;
        WorkflowExecutionResult? result = null;
        bool cancelled = false;

        if (cancellationToken.IsCancellationRequested)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, cancelledDetail);
            return WorkflowExecutionResult.Cancelled(cancelledDetail);
        }

        ResetSinkForRun(request, sink);

        try
        {
            WorkflowPreflightResult preflight = _preflightStage.Run(request, ctx);
            if (preflight.FirstBlocker is WorkflowPreflightIssue blocker)
            {
                string detail = WorkflowPreflightStage.BuildUserDetail(blocker);
                sink.ReportTerminalFailure(QueueItemFailureKind.Failed, detail);

                result = WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.Failed,
                    detail,
                    null,
                    null);

                return result;
            }

            if (settings.EnableStressMode)
            {
                stressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stressTask = _stressMonitor.RunAsync(Path.GetFileNameWithoutExtension(snap.FileName), stressCts.Token);
            }

            WorkflowPreparationResult preparation = await _inputPreparation
                .PrepareAsync(request, ctx, cancellationToken)
                .ConfigureAwait(false);

            if (!preparation.IsPrepared)
            {
                result = preparation.FailureResult ?? WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.Failed,
                    PreparationFailedDetailKey,
                    null,
                    null);

                failedOutputCandidate = result.OutputPath;
                lastOutputPath = result.OutputPath;
                return result;
            }

            WorkflowPreparedInput prepared = preparation.PreparedInput!;

            tempDirectoryToCleanup = prepared.TempDirectoryToCleanup;
            alwaysCleanupTempDirectory = prepared.AlwaysCleanupTempDirectory;
            lastProgressPercent = prepared.LastProgressPercent;

            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, cancelledDetail);
                result = WorkflowExecutionResult.Cancelled(cancelledDetail);
                return result;
            }

            result = await ExecutePreparedInputAsync(request, ctx, prepared, cancellationToken)
                .ConfigureAwait(false);

            failedOutputCandidate = result.OutputPath;
            lastOutputPath = result.OutputPath;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, cancelledDetail);
            result = WorkflowExecutionResult.Cancelled(cancelledDetail);
            return result;
        }
        catch (Exception ex)
        {
            string failureMessage = BuildWorkflowFailureMessage(
                ProcessSingleTaskStageNameKey,
                request.InputPath,
                ex);

            sink.ReportTerminalFailure(QueueItemFailureKind.Failed, failureMessage);

            _log.Error(
                ex,
                "Workflow failed with exception. Stage={Stage} Input={Input} CurrentOutput={Output}",
                "ProcessSingleTask",
                request.InputPath,
                lastOutputPath);

            result = WorkflowResultBuilder.Failure(
                QueueItemFailureKind.Failed,
                failureMessage,
                lastOutputPath,
                null);

            return result;
        }
        finally
        {
            if (result is null)
            {
                sink.ReportProgress(lastProgressPercent, indeterminate: false);
            }

            RunCleanupSafe(
                ctx,
                snap.OriginalPath,
                settings,
                failedOutputCandidate,
                tempDirectoryToCleanup,
                alwaysCleanupTempDirectory,
                result,
                cancelled,
                ResolveSourceCleanupVerifiedFlag(snap.OriginalPath, result, request.Verify, settings),
                request.InputPath);

            await StopStressMonitorAsync(stressCts, stressTask, request.InputPath)
                .ConfigureAwait(false);

            if (result is not null)
            {
                _log.Information(
                    "Workflow terminal result. Outcome={Outcome}, Input={Input}, Detail={Detail}",
                    result.Outcome,
                    request.InputPath,
                    result.StatusDetail);
            }
        }
    }
}