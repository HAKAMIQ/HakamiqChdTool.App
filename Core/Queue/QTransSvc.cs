using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

internal sealed class QueueTransitionService
{
    private static readonly ILogger Logger = Log.ForContext<QueueTransitionService>();

    private readonly QueueRuntimeState _state;
    private readonly QueueNotificationPublisher _notifications;

    public QueueTransitionService(QueueRuntimeState state, QueueNotificationPublisher notifications)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    internal static AppSettings BuildEffectiveSettings(AppSettings source, QueueExecutionProfile executionProfile)
    {
        AppSettings result = source.Clone();

        if (executionProfile is QueueExecutionProfile.QuickConvert or QueueExecutionProfile.QuickExtract)
        {
            result.SkipExistingOutput = true;
            result.EnableDeepIntegrityCheck = false;
            result.ApplyStandardNamingBasedOnHash = false;
        }

        return result;
    }

    public async Task ProcessQueuedItemAsync(ChdQueueItem item, CancellationToken itemToken)
    {
        try
        {
            await ProcessItemAsync(item, itemToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            WorkflowExecutionResult cancelled = WorkflowExecutionResult.Cancelled(QueueConstants.OperationCancelledKey);
            ApplyWorkflowResult(item, cancelled);

            Logger.Information(
                ex,
                "Queue worker observed cancellation outside item processor. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            _notifications.PublishItemUpdated(item);
        }
        catch (Exception ex)
        {
            WorkflowExecutionResult failure = WorkflowExecutionResult.Failure(
                QueueItemFailureKind.Failed,
                RuntimeDiagnosticFormatter.SummarizeException(ex));

            ApplyWorkflowResult(item, failure);

            Logger.Error(
                ex,
                "Queue worker failed outside item processor. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            _notifications.PublishItemUpdated(item);
        }
    }

    private async Task ProcessItemAsync(ChdQueueItem item, CancellationToken itemToken)
    {
        QueueItemSnapshot? snapshot = _notifications.ResolveSnapshot(item.Id);
        IQueueItemStateSink? sink = _notifications.ResolveSink(item.Id);

        if (snapshot is null || sink is null)
        {
            item.Status = QueueConstants.StatusFailed;
            item.Error = QueueConstants.UiBindFailedKey;

            Logger.Error(
                "Queue item UI bind failed. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            _notifications.PublishItemUpdated(item);
            return;
        }

        if (itemToken.IsCancellationRequested)
        {
            item.Status = QueueConstants.StatusCancelled;
            item.Error = QueueConstants.CancelledBeforeStartKey;

            Logger.Information(
                "Queue item cancelled before execution started. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            _notifications.PublishItemUpdated(item);
            return;
        }

        item.Status = QueueConstants.StatusRunning;
        item.Progress = 0;
        _notifications.PublishItemUpdated(item);

        try
        {
            AppSettings settings = BuildEffectiveSettings(_state.GetSettings(), item.ExecutionProfile);

            bool verifyOnly = string.Equals(item.Mode, QueueConstants.DefaultModeVerify, StringComparison.OrdinalIgnoreCase);
            ChdWorkflowMode workflowMode = verifyOnly
                ? ChdWorkflowMode.VerifyExistingChd
                : ChdWorkflowMode.ProcessQueueItem;

            var request = new ChdTaskRequest
            {
                InputPath = item.InputPath,
                IsArchive = !verifyOnly && WorkflowPathHelpers.IsArchivePath(snapshot.SourcePath),
                Verify = verifyOnly || settings.VerifyAfterConversion,
                OnProgress = p =>
                {
                    if (itemToken.IsCancellationRequested || IsTerminalStatus(item.Status))
                    {
                        return;
                    }

                    double nextProgress = Math.Clamp(p, 0, 99);

                    lock (item)
                    {
                        if (nextProgress < item.Progress)
                        {
                            return;
                        }

                        item.Progress = nextProgress;
                    }

                    _notifications.PublishItemUpdated(item);
                },
                Options = new ChdWorkflowTaskContext
                {
                    Snapshot = snapshot,
                    Sink = sink,
                    Settings = settings,
                    GetChdmanPath = _state.GetChdmanPath,
                    CanUseAppFeature = _state.CanUseAppFeature,
                    Mode = workflowMode,
                    OnUiRefresh = _notifications.RefreshUi
                }
            };

            WorkflowExecutionResult result = await _state.Orchestrator.ProcessAsync(request, itemToken).ConfigureAwait(false);

            if (itemToken.IsCancellationRequested
                && result.Outcome is WorkflowExecutionOutcome.Success or WorkflowExecutionOutcome.Skipped)
            {
                result = WorkflowExecutionResult.Cancelled(QueueConstants.OperationCancelledKey, result.OutputPath, result.LogPath);
            }

            ApplyWorkflowResult(item, result);
        }
        catch (OperationCanceledException)
        {
            WorkflowExecutionResult cancelled = WorkflowExecutionResult.Cancelled(QueueConstants.OperationCancelledKey);
            ApplyWorkflowResult(item, cancelled);

            Logger.Information(
                "Queue item cancelled during processing. QueueItemId={QueueItemId} Input={Input} Outcome={Outcome}",
                item.Id,
                item.InputPath,
                cancelled.Outcome);
        }
        catch (Exception ex)
        {
            WorkflowExecutionResult failure = WorkflowExecutionResult.Failure(
                QueueItemFailureKind.Failed,
                RuntimeDiagnosticFormatter.SummarizeException(ex));

            ApplyWorkflowResult(item, failure);

            Logger.Error(
                ex,
                "Queue item processing failed. QueueItemId={QueueItemId} Input={Input} Mode={Mode} Outcome={Outcome}",
                item.Id,
                item.InputPath,
                item.Mode,
                failure.Outcome);
        }
        finally
        {
            _notifications.PublishItemUpdated(item);
        }
    }

    public void ApplyWorkflowResult(ChdQueueItem item, WorkflowExecutionResult result)
    {
        if (IsTerminalStatus(item.Status)
            && !(result.Outcome == WorkflowExecutionOutcome.Cancelled
                && string.Equals(item.Status, QueueConstants.StatusSkipped, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        item.Status = ResolveQueueStatusForWorkflowResult(result);

        if (result.Outcome is WorkflowExecutionOutcome.Failure or WorkflowExecutionOutcome.Cancelled
            && !string.IsNullOrWhiteSpace(result.StatusDetail))
        {
            item.Error = result.StatusDetail;
        }

        if (result.Outcome == WorkflowExecutionOutcome.Success
            && result.TerminalSuccessOutcome is QueueItemTerminalOutcome.Healthy or QueueItemTerminalOutcome.Extracted or QueueItemTerminalOutcome.Moved)
        {
            item.Progress = 100;
        }

        item.OutputPath = string.IsNullOrWhiteSpace(result.OutputPath) ? null : result.OutputPath;
    }

    internal static string ResolveQueueStatusForWorkflowResult(WorkflowExecutionResult result)
    {
        return result.Outcome switch
        {
            WorkflowExecutionOutcome.Skipped => QueueConstants.StatusSkipped,
            WorkflowExecutionOutcome.Success => QueueConstants.StatusCompleted,
            WorkflowExecutionOutcome.Cancelled => QueueConstants.StatusCancelled,
            WorkflowExecutionOutcome.Failure => result.TerminalFailureKind switch
            {
                QueueItemFailureKind.PasswordRequired => QueueConstants.StatusPasswordRequired,
                _ => QueueConstants.StatusFailed
            },
            _ => QueueConstants.StatusFailed
        };
    }

    internal static bool IsTerminalStatus(string? status) =>
        string.Equals(status, QueueConstants.StatusCompleted, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, QueueConstants.StatusSkipped, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, QueueConstants.StatusFailed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, QueueConstants.StatusCancelled, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, QueueConstants.StatusPasswordRequired, StringComparison.OrdinalIgnoreCase);
}
