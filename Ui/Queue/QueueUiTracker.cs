using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App.Ui.Queue;

internal readonly record struct QueueUiSnapshot(
    int TotalCount,
    int WaitingCount,
    int ActiveCount,
    int CompletedCount,
    int FailedCount,
    int SkippedCount,
    int QueuedRunnableCount,
    int RemoveCompletedCount,
    double ProgressTotal,
    bool HasFailedRows);

internal sealed class QueueUiTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, QueueUiRowState> _index = new();

    private int _totalCount;
    private int _waitingCount;
    private int _activeCount;
    private int _completedCount;
    private int _failedCount;
    private int _skippedCount;
    private int _queuedRunnableCount;
    private int _removeCompletedCount;
    private double _progressTotal;
    private int _taskbarErrorCount;

    public QueueUiSnapshot Capture()
    {
        lock (_gate)
        {
            return new QueueUiSnapshot(
                _totalCount,
                _waitingCount,
                _activeCount,
                _completedCount,
                _failedCount,
                _skippedCount,
                _queuedRunnableCount,
                _removeCompletedCount,
                _progressTotal,
                _taskbarErrorCount > 0);
        }
    }

    public QueueProgressSnapshot[] CaptureProgressSnapshots()
    {
        lock (_gate)
        {
            return _index.Values
                .Where(static row => row.IsTracked)
                .Select(static row => new QueueProgressSnapshot(
                    row.IsTerminal,
                    row.IsActive,
                    row.IsIntegrityValidating,
                    row.ProgressValue))
                .ToArray();
        }
    }

    public void Rebuild(IEnumerable<QueueRowData> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        lock (_gate)
        {
            _index.Clear();
            ResetCounters();

            foreach (QueueRowData row in rows)
            {
                QueueUiRowState state = BuildRowState(row);
                if (!state.IsTracked)
                {
                    continue;
                }

                _index[row.ItemId] = state;
                ApplyDelta(default, state);
            }
        }
    }

    public void Upsert(QueueRowData row)
    {
        lock (_gate)
        {
            QueueUiRowState next = BuildRowState(row);
            _index.TryGetValue(row.ItemId, out QueueUiRowState previous);

            if (next.IsTracked)
            {
                _index[row.ItemId] = next;
            }
            else
            {
                _index.Remove(row.ItemId);
            }

            ApplyDelta(previous, next);
        }
    }

    public void Remove(QueueRowData row)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(row.ItemId, out QueueUiRowState existing))
            {
                _index.Remove(row.ItemId);
                ApplyDelta(existing, default);
            }
        }
    }

    private void ResetCounters()
    {
        _totalCount = 0;
        _waitingCount = 0;
        _activeCount = 0;
        _completedCount = 0;
        _failedCount = 0;
        _skippedCount = 0;
        _queuedRunnableCount = 0;
        _removeCompletedCount = 0;
        _progressTotal = 0d;
        _taskbarErrorCount = 0;
    }

    private static QueueUiRowState BuildRowState(QueueRowData row)
    {
        if (!row.IsVisibleInCurrentOperationMode)
        {
            return default;
        }

        return new QueueUiRowState(
            true,
            TaskQueueStateCodes.IsWaiting(row.CurrentState),
            TaskQueueStateCodes.IsActiveRunning(row.CurrentState),
            row.CurrentState == TaskQueueStateCodes.Completed,
            row.CurrentState is TaskQueueStateCodes.Failed
                or TaskQueueStateCodes.PasswordRequired
                or TaskQueueStateCodes.Cancelled,
            row.CurrentState == TaskQueueStateCodes.Skipped,
            IsQueuedForProcessing(row),
            IsCompletedSuccessful(row.FinalResult),
            row.CurrentState == TaskQueueStateCodes.Failed,
            Math.Clamp(row.Progress, 0d, 100d),
            TaskQueueStateCodes.IsTerminal(row.CurrentState),
            row.IntegrityState == IntegrityValidationState.Validating);
    }

    private void ApplyDelta(QueueUiRowState previous, QueueUiRowState next)
    {
        _totalCount += BoolToDelta(next.IsTracked) - BoolToDelta(previous.IsTracked);
        _waitingCount += BoolToDelta(next.IsWaiting) - BoolToDelta(previous.IsWaiting);
        _activeCount += BoolToDelta(next.IsActive) - BoolToDelta(previous.IsActive);
        _completedCount += BoolToDelta(next.IsCompleted) - BoolToDelta(previous.IsCompleted);
        _failedCount += BoolToDelta(next.IsFailed) - BoolToDelta(previous.IsFailed);
        _skippedCount += BoolToDelta(next.IsSkipped) - BoolToDelta(previous.IsSkipped);
        _queuedRunnableCount += BoolToDelta(next.IsQueuedRunnable) - BoolToDelta(previous.IsQueuedRunnable);
        _removeCompletedCount += BoolToDelta(next.IsCompletedSuccessful) - BoolToDelta(previous.IsCompletedSuccessful);
        _progressTotal += next.ProgressValue - previous.ProgressValue;
        _taskbarErrorCount += BoolToDelta(next.HasTaskbarError) - BoolToDelta(previous.HasTaskbarError);
    }

    private static bool IsQueuedForProcessing(QueueRowData row) =>
        QueueModeResolver.IsRequestedActionRunnable(row);

    private static bool IsCompletedSuccessful(string finalResult) =>
        finalResult is TaskFinalResultCodes.Healthy
            or TaskFinalResultCodes.Moved
            or TaskFinalResultCodes.Extracted;

    private static int BoolToDelta(bool value) => value ? 1 : 0;

    private readonly record struct QueueUiRowState(
        bool IsTracked,
        bool IsWaiting,
        bool IsActive,
        bool IsCompleted,
        bool IsFailed,
        bool IsSkipped,
        bool IsQueuedRunnable,
        bool IsCompletedSuccessful,
        bool HasTaskbarError,
        double ProgressValue,
        bool IsTerminal,
        bool IsIntegrityValidating);
}