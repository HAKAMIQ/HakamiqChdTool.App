using HakamiqChdTool.App.Localization;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

public sealed class QueueController : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<QueueController>();

    private const int CancellationDrainTimeoutSeconds = 15;

    private readonly IQueueManager _queue;
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _completionMap = [];
    private readonly object _completionGate = new();

    private int _activeRun;
    private int _disposed;

    public QueueController(IQueueManager queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _queue.ItemUpdated += QueueOnItemUpdated;
    }

    public async Task<bool> RunBatchAsync(IEnumerable<ChdQueueItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        List<ChdQueueItem> batch = items as List<ChdQueueItem> ?? [.. items];
        if (batch.Count == 0)
        {
            return false;
        }

        ValidateBatch(batch);

        if (Interlocked.CompareExchange(ref _activeRun, 1, 0) != 0)
        {
            throw new InvalidOperationException("QueueController does not support concurrent batch execution.");
        }

        List<ChdQueueItem> accepted = new(batch.Count);

        try
        {
            EnqueueBatch(batch, accepted, cancellationToken);

            if (accepted.Count == 0)
            {
                return false;
            }

            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(
                    static state => StopQueueFromCancellationRegistration((IQueueManager)state!),
                    _queue);

            try
            {
                _queue.Start();

                return await WaitUntilDoneAsync(accepted, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                UnregisterCompletions(accepted);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _activeRun, 0);
        }
    }

    private void EnqueueBatch(
        IReadOnlyList<ChdQueueItem> batch,
        List<ChdQueueItem> accepted,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (ChdQueueItem item in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();

                RegisterCompletion(item);

                QueueEnqueueResult enqueueResult;
                try
                {
                    enqueueResult = _queue.Enqueue(item);
                }
                catch
                {
                    UnregisterCompletion(item.Id);
                    throw;
                }

                if (enqueueResult == QueueEnqueueResult.Accepted)
                {
                    accepted.Add(item);
                    continue;
                }

                UnregisterCompletion(item.Id);

                Logger.Information(
                    "Queue item was not accepted for execution. QueueItemId={QueueItemId} Input={Input} EnqueueResult={EnqueueResult}",
                    item.Id,
                    item.InputPath,
                    enqueueResult);
            }
        }
        catch
        {
            StopQueueAfterEnqueueFailure();
            UnregisterCompletions(accepted);
            throw;
        }
    }

    private async Task<bool> WaitUntilDoneAsync(
        IReadOnlyList<ChdQueueItem> batch,
        CancellationToken cancellationToken)
    {
        Task completionTask = CaptureCompletionTask(batch);

        try
        {
            await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StopQueueForCancellation();

            Task drainTimeoutTask = Task.Delay(
                TimeSpan.FromSeconds(CancellationDrainTimeoutSeconds),
                CancellationToken.None);

            Task drainWinner = await Task.WhenAny(completionTask, drainTimeoutTask).ConfigureAwait(false);

            if (ReferenceEquals(drainWinner, completionTask))
            {
                try
                {
                    await completionTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Logger.Debug("Queue batch completion observed cooperative cancellation.");
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Queue batch completion faulted after cancellation.");
                }
            }
            else
            {
                Logger.Warning(
                    "Queue batch cancellation drain timed out after {TimeoutSeconds} seconds. Active workers were asked to stop and the UI wait was released.",
                    CancellationDrainTimeoutSeconds);
            }

            return true;
        }
    }

    private Task CaptureCompletionTask(IReadOnlyList<ChdQueueItem> batch)
    {
        lock (_completionGate)
        {
            Task[] pendingCompletions =
            [
                .. batch
                    .Select(item => item.Id)
                    .Select(id => _completionMap.TryGetValue(id, out TaskCompletionSource<bool>? completion)
                        ? completion.Task
                        : Task.CompletedTask)
            ];

            return pendingCompletions.Length == 0
                ? Task.CompletedTask
                : Task.WhenAll(pendingCompletions);
        }
    }

    private void RegisterCompletion(ChdQueueItem item)
    {
        lock (_completionGate)
        {
            if (_completionMap.ContainsKey(item.Id))
            {
                throw new InvalidOperationException("Duplicate queue completion registration.");
            }

            _completionMap[item.Id] = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void UnregisterCompletion(Guid itemId)
    {
        lock (_completionGate)
        {
            _completionMap.Remove(itemId);
        }
    }

    private void UnregisterCompletions(IEnumerable<ChdQueueItem> items)
    {
        lock (_completionGate)
        {
            foreach (ChdQueueItem item in items)
            {
                _completionMap.Remove(item.Id);
            }
        }
    }

    private static void ValidateBatch(IReadOnlyList<ChdQueueItem> batch)
    {
        HashSet<Guid> seen = [];

        foreach (ChdQueueItem item in batch)
        {
            if (item.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Queue item id cannot be empty.");
            }

            if (!seen.Add(item.Id))
            {
                throw new InvalidOperationException("Duplicate queue item id in batch.");
            }
        }
    }

    private static bool IsTerminalStatus(string? status)
    {
        return string.Equals(status, TaskQueueStateCodes.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, TaskQueueStateCodes.Skipped, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, TaskQueueStateCodes.Failed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, TaskQueueStateCodes.Cancelled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, TaskQueueStateCodes.PasswordRequired, StringComparison.OrdinalIgnoreCase);
    }

    private void QueueOnItemUpdated(ChdQueueItem item)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsTerminalStatus(item.Status))
        {
            return;
        }

        lock (_completionGate)
        {
            if (_completionMap.Remove(item.Id, out TaskCompletionSource<bool>? completion))
            {
                _ = completion.TrySetResult(true);
            }
        }
    }

    private void StopQueueForCancellation()
    {
        try
        {
            _queue.Stop();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Queue stop failed while handling cancellation.");
        }
    }

    private void StopQueueAfterEnqueueFailure()
    {
        try
        {
            _queue.Stop();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Queue stop failed after enqueue failure.");
        }
    }

    private void StopQueueForDisposal()
    {
        try
        {
            _queue.Stop();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Queue stop failed during QueueController disposal.");
        }
    }

    private static void StopQueueFromCancellationRegistration(IQueueManager queue)
    {
        try
        {
            queue.Stop();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Queue stop failed from cancellation registration.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopQueueForDisposal();

        _queue.ItemUpdated -= QueueOnItemUpdated;

        lock (_completionGate)
        {
            foreach (TaskCompletionSource<bool> completion in _completionMap.Values)
            {
                _ = completion.TrySetResult(false);
            }

            _completionMap.Clear();
        }

        Interlocked.Exchange(ref _activeRun, 0);
    }
}