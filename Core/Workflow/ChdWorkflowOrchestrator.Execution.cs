using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Workflow;

public sealed partial class ChdWorkflowOrchestrator
{
    private async Task<WorkflowExecutionResult> ProcessVerifyExistingWorkflowAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        CancellationToken ct)
    {
        IQueueItemStateSink sink = ctx.Sink;
        string cancelled = CancelledDetailKey;

        try
        {
            WorkflowExecutionResult result = await _verificationStage
                .VerifyExistingAsync(request, ctx, ctx.Snapshot.SourcePath, ctx.Snapshot.DetectedPlatform, ct)
                .ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, cancelled);
                return WorkflowExecutionResult.Cancelled(cancelled, result.OutputPath, result.LogPath);
            }

            _log.Information(
                "Verify-only workflow completed. Outcome={Outcome}, Input={Input}, Detail={Detail}",
                result.Outcome,
                request.InputPath,
                result.StatusDetail);

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, cancelled);
            _log.Information("Verify-only workflow cancelled. Input={Input}", request.InputPath);
            return WorkflowExecutionResult.Cancelled(cancelled);
        }
        catch (Exception ex)
        {
            string failureMessage = BuildWorkflowFailureMessage(VerifyExistingStageNameKey, request.InputPath, ex);
            sink.ReportTerminalFailure(QueueItemFailureKind.Failed, failureMessage);

            _log.Error(
                ex,
                "Verify-only workflow failed with exception. Stage={Stage} Input={Input}",
                "VerifyExistingChd",
                request.InputPath);

            return WorkflowResultBuilder.Failure(QueueItemFailureKind.Failed, failureMessage, null, null);
        }
    }

    private async Task<WorkflowExecutionResult> ExecutePreparedInputAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        WorkflowPreparedInput prepared,
        CancellationToken cancellationToken)
    {
        string currentSourcePath = prepared.SourcePath;
        string currentRequestedAction = prepared.RequestedAction;
        string currentDetectedPlatform = prepared.DetectedPlatform;
        QueueInputClassification currentClassification = QueueInputClassifier.Classify(currentSourcePath);

        return currentRequestedAction switch
        {
            TaskActionCodes.ConvertToChd when currentClassification.IsConvertibleDiscImage
                => await _conversionStage
                    .ExecuteAsync(
                        request,
                        ctx,
                        currentSourcePath,
                        currentDetectedPlatform,
                        prepared.LastProgressPercent,
                        cancellationToken)
                    .ConfigureAwait(false),

            TaskActionCodes.RestoreDiscImageFromChd when currentClassification.IsChdImage
                => await _extractionStage
                    .ExecuteAsync(request, ctx, currentSourcePath, currentDetectedPlatform, cancellationToken)
                    .ConfigureAwait(false),

            _ => Unsupported(ctx.Sink)
        };
    }
}
