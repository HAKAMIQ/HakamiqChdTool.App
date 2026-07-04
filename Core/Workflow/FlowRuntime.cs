using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

public sealed partial class ChdWorkflowOrchestrator
{
    private const string CancelledDetailKey = "LocStatus_UserCancelled";
    private const string PreparationFailedDetailKey = "LocWorkflow_PreparationFailed";
    private const string UnsupportedStageDetailKey = "LocWorkflow_UnsupportedStage";
    private const string VerifyExistingStageNameKey = "LocWorkflow_VerifyExistingStageName";
    private const string ProcessSingleTaskStageNameKey = "LocWorkflow_ProcessSingleTaskStageName";
    private const string UnknownFileNameKey = "LocWorkflow_UnknownFileName";
    private const string StageFailureFormatKey = "LocWorkflow_StageFailureFormat";

    private static readonly TimeSpan StressShutdownTimeout = TimeSpan.FromSeconds(3);

    private static void ResetSinkForRun(ChdTaskRequest request, IQueueItemStateSink sink)
    {
        sink.ResetForRun();
        sink.ReportProgress(0, indeterminate: false);
        WorkflowPathUtilities.RaiseProgress(request, 0);
        sink.AttachArtifact(QueueItemArtifactKind.LogFile, string.Empty);
        sink.AttachArtifact(QueueItemArtifactKind.OutputFile, string.Empty);
    }

    private void RunCleanupSafe(
        ChdWorkflowTaskContext ctx,
        string originalPath,
        AppSettings settings,
        string? failedOutputCandidate,
        string? tempDirectoryToCleanup,
        bool alwaysCleanupTempDirectory,
        WorkflowExecutionResult? result,
        bool cancelled,
        bool sourceDeletionWasVerified,
        string inputPath)
    {
        try
        {
            _cleanupStage.Run(
                ctx,
                originalPath,
                settings,
                failedOutputCandidate,
                tempDirectoryToCleanup,
                alwaysCleanupTempDirectory,
                result,
                cancelled,
                sourceDeletionWasVerified);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Workflow cleanup failed after terminal result. Input={Input} TempDirectory={TempDirectory} Output={Output}",
                inputPath,
                tempDirectoryToCleanup,
                failedOutputCandidate);
        }
    }


    private static bool ResolveSourceCleanupVerifiedFlag(
        string originalPath,
        WorkflowExecutionResult? result,
        bool requestVerify,
        AppSettings settings)
    {
        if (result is null || result.Outcome != WorkflowExecutionOutcome.Success)
        {
            return false;
        }

        string sourceExtension = Path.GetExtension(originalPath);
        string outputExtension = string.IsNullOrWhiteSpace(result.OutputPath)
            ? string.Empty
            : Path.GetExtension(result.OutputPath);

        if (sourceExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
            && !outputExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
        {
            return requestVerify;
        }

        if (!sourceExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
            && outputExtension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
        {
            return requestVerify && settings.VerifyAfterConversion;
        }

        return false;
    }

    private async Task StopStressMonitorAsync(CancellationTokenSource? stressCts, Task? stressTask, string inputPath)
    {
        if (stressCts is null)
        {
            return;
        }

        try
        {
            stressCts.Cancel();

            if (stressTask is not null)
            {
                Task completed = await Task.WhenAny(stressTask, Task.Delay(StressShutdownTimeout, CancellationToken.None)).ConfigureAwait(false);
                if (!ReferenceEquals(completed, stressTask))
                {
                    _log.Warning("Stress monitor did not stop within timeout. Input={Input}", inputPath);
                    return;
                }

                await stressTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex)
        {
            _log.Debug(ex, "Stress monitor cancellation observed during workflow shutdown. Input={Input}", inputPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Stress monitor failed during workflow shutdown. Input={Input}", inputPath);
        }
        finally
        {
            stressCts.Dispose();
        }
    }

    private static WorkflowExecutionResult Unsupported(IQueueItemStateSink sink)
    {
        string detail = UnsupportedStageDetailKey;
        sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, detail);
        return WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, detail, null, null);
    }

    private static string BuildWorkflowFailureMessage(string stageKey, string inputPath, Exception exception)
    {
        string inputName = string.IsNullOrWhiteSpace(inputPath)
            ? L(UnknownFileNameKey)
            : Path.GetFileName(inputPath);

        string detail = RuntimeDiagnosticFormatter.SummarizeException(exception);

        return ArabicUi.FormatText(
            L(StageFailureFormatKey),
            L(stageKey),
            inputName,
            detail);
    }

    private static string L(string key) => ArabicUi.ResolveDisplayString(key);
}