using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Power;
using Serilog;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowVerificationStage
{
    private const string VerifyInvalidDetailKey = "LocChdVerify_Invalid";
    private const string VerifyToolStartFailedDetailKey = "LocChdVerify_ToolStartFailed";
    private const string VerifyCancelledDetailKey = "LocStatus_UserCancelled";
    private const string VerifyReadingMetadataStageKey = "LocStatus_ReadingChdMetadata";
    private const string VerifySuccessDetailKey = "LocStatus_VerifyCompletedSuccess";

    private readonly ChdInfoService _chdInfo;
    private readonly ChdVerificationService _verify;
    private readonly IConversionPowerGuard _powerGuard;
    private readonly ILogger _log;

    public WorkflowVerificationStage(
        ChdInfoService chdInfo,
        ChdVerificationService verify,
        IConversionPowerGuard powerGuard,
        ILogger log)
    {
        ArgumentNullException.ThrowIfNull(chdInfo);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(powerGuard);
        ArgumentNullException.ThrowIfNull(log);

        _chdInfo = chdInfo;
        _verify = verify;
        _powerGuard = powerGuard;
        _log = log;
    }

    public async Task<WorkflowExecutionResult> VerifyExistingAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        string chdPath,
        string _,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(chdPath);

        IQueueItemStateSink sink = ctx.Sink;
        string? lastLogPath = null;

        ChdWorkflowProfilePlan verifyPlan = ChdWorkflowProfilePlanner.PlanVerifyChd(chdPath);
        if (!verifyPlan.IsSupported)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, verifyPlan.FailureMessage);
            sink.ReportProgress(100, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, 100);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.Unsupported,
                verifyPlan.FailureMessage,
                chdPath,
                null);
        }

        string verifyChdmanPath;
        try
        {
            verifyChdmanPath = ctx.GetChdmanPath();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not resolve chdman path before CHD verification. File={File}", chdPath);

            sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, VerifyToolStartFailedDetailKey);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedVerify,
                VerifyToolStartFailedDetailKey,
                chdPath,
                null);
        }

        if (string.IsNullOrWhiteSpace(verifyChdmanPath))
        {
            _log.Error("Resolved chdman path was empty before CHD verification. File={File}", chdPath);

            sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, VerifyToolStartFailedDetailKey);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedVerify,
                VerifyToolStartFailedDetailKey,
                chdPath,
                null);
        }

        sink.ReportStage(QueueItemStage.Verifying, verifyPlan.StatusLine);
        sink.ReportProgress(5, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 5);

        var verifyProgress = new Progress<int>(value =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            double p = WorkflowPathUtilities.MapProgressRange(value, 5, 90);
            sink.ReportProgress(p, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, p);
        });

        ChdVerificationResult verificationResult;
        _powerGuard.BeginCriticalConversionSession();
        try
        {
            verificationResult = await _verify.VerifyAsync(
                verifyChdmanPath,
                chdPath,
                verifyProgress,
                onProcessStarted: null,
                cancellationToken: cancellationToken,
                priorityMode: ctx.Settings.ChdmanPriorityMode).ConfigureAwait(false);
        }
        finally
        {
            _powerGuard.EndCriticalConversionSession();
        }

        if (!string.IsNullOrWhiteSpace(verificationResult.LogPath))
        {
            sink.AttachArtifact(QueueItemArtifactKind.LogFile, verificationResult.LogPath);
            lastLogPath = verificationResult.LogPath;
        }

        if (verificationResult.WasCancelled
            || verificationResult.Status == ChdVerificationStatus.Cancelled
            || cancellationToken.IsCancellationRequested)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, VerifyCancelledDetailKey);
            return WorkflowExecutionResult.Cancelled(VerifyCancelledDetailKey, chdPath, lastLogPath);
        }

        if (verificationResult.Status == ChdVerificationStatus.ToolStartFailed)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, VerifyToolStartFailedDetailKey);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedVerify,
                VerifyToolStartFailedDetailKey,
                chdPath,
                lastLogPath);
        }

        if (!verificationResult.IsSuccess || verificationResult.Status != ChdVerificationStatus.Valid)
        {
            _log.Warning(
                "CHD verification failed. File={File}, Status={Status}, Detail={Detail}",
                chdPath,
                verificationResult.Status,
                verificationResult.Message);

            sink.ReportTerminalFailure(QueueItemFailureKind.FailedVerify, VerifyInvalidDetailKey);

            return WorkflowResultBuilder.Failure(
                QueueItemFailureKind.FailedVerify,
                VerifyInvalidDetailKey,
                chdPath,
                lastLogPath);
        }

        sink.ReportProgress(90, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 90);

        try
        {
            sink.ReportStage(QueueItemStage.ReadingFile, VerifyReadingMetadataStageKey);
            sink.ReportProgress(92, indeterminate: false);
            WorkflowPathUtilities.RaiseProgress(request, 92);

            ChdInfoResult infoResult = await _chdInfo.ReadInfoAsync(
                verifyChdmanPath,
                chdPath,
                onProcessStarted: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(infoResult.LogPath))
            {
                sink.AttachArtifact(QueueItemArtifactKind.LogFile, infoResult.LogPath);
                lastLogPath = infoResult.LogPath;
            }

            if (infoResult.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, VerifyCancelledDetailKey);
                return WorkflowExecutionResult.Cancelled(VerifyCancelledDetailKey, chdPath, lastLogPath);
            }

            if (infoResult.IsSuccess)
            {
                await WorkflowPathUtilities.ApplyChdMediaDetectionAsync(
                    sink,
                    chdPath,
                    infoResult,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _log.Warning(
                    "CHD verify succeeded but metadata read failed. File={File}, Detail={Detail}",
                    chdPath,
                    infoResult.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sink.ReportTerminalFailure(QueueItemFailureKind.Cancelled, VerifyCancelledDetailKey);
            return WorkflowExecutionResult.Cancelled(VerifyCancelledDetailKey, chdPath, lastLogPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "CHD verify succeeded but metadata enrichment failed. File={File}", chdPath);
        }

        sink.AttachArtifact(QueueItemArtifactKind.OutputFile, chdPath);
        sink.ReportTerminalSuccess(QueueItemTerminalOutcome.Healthy, VerifySuccessDetailKey);
        sink.ReportProgress(100, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 100);

        return WorkflowResultBuilder.Success(
            QueueItemTerminalOutcome.Healthy,
            VerifySuccessDetailKey,
            chdPath,
            lastLogPath);
    }
}
