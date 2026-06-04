using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowInputPreparationService
{
    private const string UnsupportedArchiveModeDetailKey = "LocWorkflow_ArchiveStageRequestedButInputIsNotArchive";
    private const string ReadChdInfoForExtractionStatusKey = "LocWorkflow_StatusReadChdInfoForExtraction";

    private readonly ArchiveWorkflowPreparationService _archivePreparation;

    public WorkflowInputPreparationService(ArchiveExtractionService archive, Serilog.ILogger log)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(log);

        _archivePreparation = new ArchiveWorkflowPreparationService(archive, log);
    }

    public async Task<WorkflowPreparationResult> PrepareAsync(
        ChdTaskRequest request,
        ChdWorkflowTaskContext ctx,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        QueueItemSnapshot snap = ctx.Snapshot;
        QueueInputClassification sourceClassification = QueueInputClassifier.Classify(snap.SourcePath);

        if (sourceClassification.IsArchiveContainer)
        {
            return await _archivePreparation
                .PrepareUnpackThenConvertAsync(request, ctx, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.Equals(snap.RequestedAction, TaskActionCodes.StageArchiveForConversion, StringComparison.Ordinal))
        {
            ctx.Sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, UnsupportedArchiveModeDetailKey);

            return WorkflowPreparationResult.Failed(
                WorkflowResultBuilder.Failure(
                    QueueItemFailureKind.Unsupported,
                    UnsupportedArchiveModeDetailKey,
                    null,
                    null));
        }

        WorkflowPreparationResult? directPlanFailure = ValidateDirectInputPlan(snap, ctx);
        if (directPlanFailure is not null)
        {
            return directPlanFailure;
        }

        return WorkflowPreparationResult.Prepared(new WorkflowPreparedInput(
            snap.SourcePath,
            snap.RequestedAction,
            snap.DetectedPlatform,
            null,
            5,
            string.Empty,
            string.Empty));
    }

    private static WorkflowPreparationResult? ValidateDirectInputPlan(
        QueueItemSnapshot snap,
        ChdWorkflowTaskContext ctx)
    {
        ChdWorkflowProfilePlan plan = snap.RequestedAction switch
        {
            TaskActionCodes.ConvertToChd => ChdWorkflowProfilePlanner.PlanCreateFromSource(
                snap.SourcePath,
                ctx.Settings.IsoCreateCommandOverride,
                ChdMediaContainerKind.DirectFile),

            TaskActionCodes.VerifyChd => ChdWorkflowProfilePlanner.PlanVerifyChd(snap.SourcePath),

            TaskActionCodes.RestoreDiscImageFromChd when QueueInputClassifier.Classify(snap.SourcePath).IsChdImage
                => new ChdWorkflowProfilePlan
                {
                    IsSupported = true,
                    ContainerKind = ChdMediaContainerKind.DirectFile,
                    MediaKind = ChdMediaFormatKind.Chd,
                    ProfileKind = ChdWorkflowProfileKind.Unsupported,
                    StatusLine = ReadChdInfoForExtractionStatusKey
                },

            _ => ChdWorkflowProfilePlan.Unsupported(ChdWorkflowProfilePlanner.UnsupportedMessageKey)
        };

        if (!plan.IsSupported)
        {
            string detail = string.IsNullOrWhiteSpace(plan.FailureMessage)
                ? ChdWorkflowProfilePlanner.UnsupportedMessageKey
                : plan.FailureMessage;

            ctx.Sink.ReportTerminalFailure(QueueItemFailureKind.Unsupported, detail);

            return WorkflowPreparationResult.Failed(
                WorkflowResultBuilder.Failure(QueueItemFailureKind.Unsupported, detail, null, null));
        }

        if (!string.IsNullOrWhiteSpace(plan.StatusLine))
        {
            ctx.Sink.ReportStage(QueueItemStage.ReadingFile, plan.StatusLine);
        }

        return null;
    }
}
