using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

internal sealed class QueueConcurrencyCoordinator : IAsyncDisposable
{
    private static readonly ILogger Logger = Log.ForContext<QueueConcurrencyCoordinator>();

    private readonly QueueRuntimeState _state;
    private readonly QueueStateStore _stateStore;
    private readonly QueueTransitionService _transitionService;
    private readonly QueueNotificationPublisher _notifications;

    public QueueConcurrencyCoordinator(
        QueueRuntimeState state,
        QueueStateStore stateStore,
        QueueTransitionService transitionService,
        QueueNotificationPublisher notifications)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _transitionService = transitionService ?? throw new ArgumentNullException(nameof(transitionService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public void UpdateMaxConcurrentItems(int maxConcurrentItems)
    {
        if (Volatile.Read(ref _state.Disposed) != 0)
        {
            Logger.Warning("Queue concurrency update ignored because queue manager has already been disposed.");
            return;
        }

        int normalized = AppSettings.NormalizeMaxConcurrentConversions(maxConcurrentItems);

        lock (_state.ProcessConcurrencyGate)
        {
            if (normalized == _state.MaxConcurrentItems)
            {
                _state.PendingMaxConcurrentItems = null;
                return;
            }

            if (HasActiveOrReservedConcurrencySlotUnsafe())
            {
                _state.PendingMaxConcurrentItems = normalized;

                Logger.Warning(
                    "Queue concurrency update deferred until queued work becomes idle. RequestedMaxConcurrentItems={RequestedMaxConcurrentItems} CurrentMaxConcurrentItems={CurrentMaxConcurrentItems} RunningCount={RunningCount}",
                    normalized,
                    _state.MaxConcurrentItems,
                    _state.RunningItems.Count);

                return;
            }

            ApplyMaxConcurrentItemsUnsafe(normalized);
        }
    }

    public void Start()
    {
        if (Volatile.Read(ref _state.Disposed) != 0)
        {
            Logger.Warning("Queue start request ignored because queue manager has already been disposed.");
            return;
        }

        if (_state.ShutdownCts.IsCancellationRequested)
        {
            Logger.Warning("Queue start request ignored because shutdown has already started.");
            return;
        }

        lock (_state.LoopGate)
        {
            if (_state.ShutdownCts.IsCancellationRequested)
            {
                Logger.Warning("Queue start request ignored inside loop gate because shutdown has already started.");
                return;
            }

            if (_state.LoopTask is not null && !_state.LoopTask.IsCompleted)
            {
                return;
            }

            _state.LoopTask = Task.Run(() => ProcessLoopAsync(_state.ShutdownCts.Token), CancellationToken.None);
        }
    }

    public void Stop()
    {
        List<ChdQueueItem>? waitingItems = null;

        lock (_state.ItemsLock)
        {
            foreach (ChdQueueItem item in _state.Items)
            {
                if (!string.Equals(item.Status, QueueConstants.StatusPending, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.Status = QueueConstants.StatusCancelled;
                waitingItems ??= [];
                waitingItems.Add(item);
            }
        }

        foreach (KeyValuePair<Guid, CancellationTokenSource> kv in _state.ItemTokens.ToArray())
        {
            try
            {
                kv.Value.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(
                    ex,
                    "Queue item cancellation token was already disposed during Stop. QueueItemId={QueueItemId}",
                    kv.Key);
            }
        }

        if (waitingItems is null)
        {
            return;
        }

        foreach (ChdQueueItem item in waitingItems)
        {
            _notifications.PublishItemUpdated(item);
            _stateStore.CompleteScheduledItem(item);
        }
    }

    private async Task ProcessLoopAsync(CancellationToken shutdownToken)
    {
        try
        {
            while (!shutdownToken.IsCancellationRequested)
            {
                try
                {
                    await _state.Signal.WaitAsync(shutdownToken).ConfigureAwait(false);

                    if (!_state.WorkQueue.TryDequeue(out ChdQueueItem? item) || item is null)
                    {
                        continue;
                    }

                    ScheduleQueuedItem(item, shutdownToken);
                }
                catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                {
                    Logger.Debug("Queue processing loop cancellation observed inside loop.");
                    break;
                }
                catch (ObjectDisposedException ex) when (shutdownToken.IsCancellationRequested)
                {
                    Logger.Debug(ex, "Queue processing loop stopped because queue resources were disposed during shutdown.");
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Queue processing loop failed while scheduling an item. The loop will continue.");
                }
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            Logger.Debug("Queue processing loop stopped because shutdown was requested.");
        }
    }

    private void ScheduleQueuedItem(ChdQueueItem item, CancellationToken shutdownToken)
    {
        if (string.Equals(item.Status, QueueConstants.StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            _stateStore.CompleteScheduledItem(item);
            return;
        }

        if (!_state.ItemTokens.TryGetValue(item.Id, out CancellationTokenSource? itemCts))
        {
            item.Status = QueueConstants.StatusFailed;
            item.Error = QueueConstants.CancellationTokenMissingKey;
            _notifications.PublishItemUpdated(item);
            _stateStore.CompleteScheduledItem(item);
            return;
        }

        Task runningTask = RunScheduledItemAsync(item, itemCts.Token, shutdownToken);

        if (_state.RunningItems.TryAdd(item.Id, runningTask))
        {
            _ = runningTask.ContinueWith(
                completedTask => CompleteRunningItemTracking(item.Id, completedTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return;
        }

        item.Status = QueueConstants.StatusFailed;
        item.Error = QueueConstants.AlreadyScheduledKey;
        _notifications.PublishItemUpdated(item);

        try
        {
            itemCts.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(
                ex,
                "Queue item cancellation token was already disposed during duplicate scheduling. QueueItemId={QueueItemId}",
                item.Id);
        }

        _stateStore.CompleteScheduledItem(item);
    }

    private async Task RunScheduledItemAsync(
        ChdQueueItem item,
        CancellationToken itemToken,
        CancellationToken shutdownToken)
    {
        try
        {
            await ProcessQueuedItemWithGateAsync(item, itemToken, shutdownToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _transitionService.ApplyWorkflowResult(item, WorkflowExecutionResult.Cancelled(QueueConstants.OperationCancelledKey));
            _notifications.PublishItemUpdated(item);

            Logger.Information(
                ex,
                "Queue item cancellation was contained at the scheduled-item boundary. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);
        }
    }

    private void CompleteRunningItemTracking(Guid itemId, Task completedTask)
    {
        ObserveCompletedRunningTask(itemId, completedTask);
        _state.RunningItems.TryRemove(itemId, out _);
        TryApplyPendingMaxConcurrentItemsIfIdle();
    }

    private void TryApplyPendingMaxConcurrentItemsIfIdle()
    {
        lock (_state.ProcessConcurrencyGate)
        {
            if (_state.PendingMaxConcurrentItems is not int pending)
            {
                return;
            }

            if (HasActiveOrReservedConcurrencySlotUnsafe())
            {
                return;
            }

            ApplyMaxConcurrentItemsUnsafe(pending);
        }
    }

    private bool HasActiveOrReservedConcurrencySlotUnsafe()
    {
        return !_state.RunningItems.IsEmpty || _state.ProcessConcurrency.CurrentCount != _state.MaxConcurrentItems;
    }

    private void ApplyMaxConcurrentItemsUnsafe(int maxConcurrentItems)
    {
        int normalized = AppSettings.NormalizeMaxConcurrentConversions(maxConcurrentItems);
        _state.ProcessConcurrency = new SemaphoreSlim(normalized, normalized);
        _state.MaxConcurrentItems = normalized;
        _state.PendingMaxConcurrentItems = null;
    }

    private void PruneCompletedRunningItemTracking()
    {
        foreach (KeyValuePair<Guid, Task> kv in _state.RunningItems.ToArray())
        {
            if (!kv.Value.IsCompleted)
            {
                continue;
            }

            ObserveCompletedRunningTask(kv.Key, kv.Value);
            _state.RunningItems.TryRemove(kv.Key, out _);
        }
    }

    private static void ObserveCompletedRunningTask(Guid itemId, Task completedTask)
    {
        if (completedTask.Exception is null)
        {
            return;
        }

        try
        {
            completedTask.Exception.Handle(static _ => true);
        }
        catch (Exception ex)
        {
            Logger.Debug(
                ex,
                "Queue item completed with an unobservable exception while cleaning running-item tracking. QueueItemId={QueueItemId}",
                itemId);
        }
    }

    private async Task ProcessQueuedItemWithGateAsync(
        ChdQueueItem item,
        CancellationToken itemToken,
        CancellationToken shutdownToken)
    {
        SemaphoreSlim processConcurrency;
        bool gateEntered = false;

        lock (_state.ProcessConcurrencyGate)
        {
            processConcurrency = _state.ProcessConcurrency;
        }

        try
        {
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(itemToken, shutdownToken);

            await processConcurrency.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            gateEntered = true;

            await _transitionService.ProcessQueuedItemAsync(item, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _transitionService.ApplyWorkflowResult(item, WorkflowExecutionResult.Cancelled(QueueConstants.OperationCancelledKey));
            _notifications.PublishItemUpdated(item);
        }
        catch (Exception ex)
        {
            _transitionService.ApplyWorkflowResult(
                item,
                WorkflowExecutionResult.Failure(
                    QueueItemFailureKind.Failed,
                    RuntimeDiagnosticFormatter.SummarizeException(ex)));

            Logger.Error(
                ex,
                "Queue item worker crashed. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            _notifications.PublishItemUpdated(item);
        }
        finally
        {
            if (gateEntered)
            {
                try
                {
                    processConcurrency.Release();
                }
                catch (ObjectDisposedException ex)
                {
                    Logger.Debug(
                        ex,
                        "Queue concurrency semaphore was already disposed before release. QueueItemId={QueueItemId}",
                        item.Id);
                }
                catch (SemaphoreFullException ex)
                {
                    Logger.Warning(
                        ex,
                        "Queue concurrency semaphore release exceeded its maximum count. QueueItemId={QueueItemId}",
                        item.Id);
                }
            }

            _stateStore.CompleteScheduledItem(item);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state.Disposed, 1) != 0)
        {
            return;
        }

        _state.ShutdownCts.Cancel();
        Stop();

        try
        {
            _state.Signal.Release();
        }
        catch (SemaphoreFullException ex)
        {
            Logger.Debug(ex, "Queue shutdown signal was already full during disposal.");
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Queue shutdown signal was already disposed during disposal.");
        }

        Task? loopTask;
        lock (_state.LoopGate)
        {
            loopTask = _state.LoopTask;
        }

        if (loopTask is not null)
        {
            try
            {
                Task completedLoop = await Task.WhenAny(
                        loopTask,
                        Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None))
                    .ConfigureAwait(false);

                if (ReferenceEquals(completedLoop, loopTask))
                {
                    await loopTask.ConfigureAwait(false);
                }
                else
                {
                    Logger.Warning("Queue processing loop did not stop within disposal timeout.");
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.Debug(ex, "Queue processing loop cancelled during disposal.");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Queue processing loop faulted during disposal.");
            }
        }

        Task[] runningItems = [.. _state.RunningItems.Values];
        bool allRunningItemsCompleted = true;

        if (runningItems.Length > 0)
        {
            try
            {
                Task runningCompletion = Task.WhenAll(runningItems);
                Task completedRunning = await Task.WhenAny(
                        runningCompletion,
                        Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None))
                    .ConfigureAwait(false);

                if (ReferenceEquals(completedRunning, runningCompletion))
                {
                    await runningCompletion.ConfigureAwait(false);
                }
                else
                {
                    allRunningItemsCompleted = false;

                    Logger.Warning(
                        "Queue disposal timed out while waiting for {RunningCount} running item(s). Queue-owned semaphores will not be disposed to avoid ObjectDisposedException in delayed workers.",
                        runningItems.Length);
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.Debug(ex, "Running queue item cancelled during disposal.");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "One or more running queue items faulted during disposal.");
            }
        }

        if (allRunningItemsCompleted)
        {
            PruneCompletedRunningItemTracking();
        }

        _state.ActivePaths.Clear();

        if (allRunningItemsCompleted && _state.RunningItems.IsEmpty)
        {
            DisposeOwnedSynchronizationResources();
        }
        else
        {
            Logger.Warning(
                "Queue-owned synchronization resources were not disposed because {RunningCount} item(s) are still tracked.",
                _state.RunningItems.Count);
        }

        foreach (Guid id in _state.ItemTokens.Keys.ToArray())
        {
            if (_state.RunningItems.ContainsKey(id))
            {
                Logger.Warning(
                    "Queue item cancellation token was not disposed because the item is still tracked as running. QueueItemId={QueueItemId}",
                    id);

                continue;
            }

            if (_state.ItemTokens.TryRemove(id, out CancellationTokenSource? cts))
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException ex)
                {
                    Logger.Debug(
                        ex,
                        "Queue item cancellation token was already disposed during QueueManager disposal. QueueItemId={QueueItemId}",
                        id);
                }
            }
        }

        _notifications.Clear();
    }

    private void DisposeOwnedSynchronizationResources()
    {
        try
        {
            _state.Signal.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Queue signal semaphore was already disposed.");
        }

        try
        {
            _state.ProcessConcurrency.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Queue concurrency semaphore was already disposed.");
        }

        _state.ShutdownCts.Dispose();
    }
}
