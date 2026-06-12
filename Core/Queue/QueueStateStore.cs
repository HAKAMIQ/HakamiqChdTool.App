using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace HakamiqChdTool.App.Core.Queue;

internal sealed class QueueStateStore
{
    private static readonly ILogger Logger = Log.ForContext<QueueStateStore>();

    private readonly QueueRuntimeState _state;
    private readonly QueueNotificationPublisher _notifications;

    public QueueStateStore(QueueRuntimeState state, QueueNotificationPublisher notifications)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public IReadOnlyCollection<ChdQueueItem> Items
    {
        get
        {
            lock (_state.ItemsLock)
            {
                return [.. _state.Items];
            }
        }
    }

    public QueueEnqueueResult Enqueue(ChdQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Volatile.Read(ref _state.Disposed) != 0)
        {
            Logger.Warning(
                "Queue item rejected because queue manager has already been disposed. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            return QueueEnqueueResult.RejectedQueueShuttingDown;
        }

        if (string.IsNullOrWhiteSpace(item.InputPath))
        {
            Logger.Warning(
                "Queue item rejected because the input path is empty. QueueItemId={QueueItemId}",
                item.Id);

            return QueueEnqueueResult.RejectedInvalidItem;
        }

        if (_state.ShutdownCts.IsCancellationRequested)
        {
            Logger.Warning(
                "Queue item rejected because queue shutdown has already started. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            return QueueEnqueueResult.RejectedQueueShuttingDown;
        }

        if (string.Equals(item.Status, QueueConstants.StatusRunning, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning(
                "Queue item rejected because it is already running. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            return QueueEnqueueResult.RejectedAlreadyRunning;
        }

        string pathKey = NormalizeQueuePath(item.InputPath);
        if (!_state.ActivePaths.TryAdd(pathKey, 0))
        {
            Logger.Information(
                "Queue item rejected because an item with the same normalized input path is already active. QueueItemId={QueueItemId} Input={Input} NormalizedPath={NormalizedPath}",
                item.Id,
                item.InputPath,
                pathKey);

            return QueueEnqueueResult.RejectedDuplicatePath;
        }

        var cts = new CancellationTokenSource();
        if (!_state.ItemTokens.TryAdd(item.Id, cts))
        {
            cts.Dispose();
            _state.ActivePaths.TryRemove(pathKey, out _);
            throw new InvalidOperationException("Duplicate queue item id.");
        }

        try
        {
            lock (_state.ItemsLock)
            {
                _state.Items.Add(item);
            }

            _state.WorkQueue.Enqueue(item);
            _state.Signal.Release();
            _notifications.PublishItemUpdated(item);

            return QueueEnqueueResult.Accepted;
        }
        catch
        {
            if (_state.ItemTokens.TryRemove(item.Id, out CancellationTokenSource? cleanupCts))
            {
                try
                {
                    cleanupCts.Dispose();
                }
                catch (ObjectDisposedException ex)
                {
                    Logger.Debug(
                        ex,
                        "Queue item cancellation token was already disposed during enqueue rollback. QueueItemId={QueueItemId}",
                        item.Id);
                }
            }

            _state.ActivePaths.TryRemove(pathKey, out _);

            lock (_state.ItemsLock)
            {
                _state.Items.Remove(item);
            }

            throw;
        }
    }

    public void Cancel(Guid id)
    {
        if (_state.ItemTokens.TryGetValue(id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(
                    ex,
                    "Queue item cancellation token was already disposed during Cancel. QueueItemId={QueueItemId}",
                    id);
            }
        }

        ChdQueueItem? pending;

        lock (_state.ItemsLock)
        {
            pending = _state.Items.FirstOrDefault(x => x.Id == id);
            if (pending is not null && string.Equals(pending.Status, QueueConstants.StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                pending.Status = QueueConstants.StatusCancelled;
            }
        }

        if (pending is null)
        {
            return;
        }

        _notifications.PublishItemUpdated(pending);

        if (string.Equals(pending.Status, QueueConstants.StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            CompleteScheduledItem(pending);
        }
    }

    public void CompleteScheduledItem(ChdQueueItem item)
    {
        if (_state.ItemTokens.TryRemove(item.Id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(
                    ex,
                    "Queue item cancellation token was already disposed while completing item. QueueItemId={QueueItemId}",
                    item.Id);
            }
        }

        ReleaseQueuePath(item);

        lock (_state.ItemsLock)
        {
            _state.Items.Remove(item);
        }
    }

    public void ReleaseQueuePath(ChdQueueItem item)
    {
        _state.ActivePaths.TryRemove(NormalizeQueuePath(item.InputPath), out _);
    }

    internal static string NormalizeQueuePath(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return "\0empty";
        }

        string trimmed = inputPath.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "\0empty";
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because the path contains invalid characters. InputPath={InputPath}", trimmed);
            return trimmed;
        }
        catch (NotSupportedException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because the path format is not supported. InputPath={InputPath}", trimmed);
            return trimmed;
        }
        catch (PathTooLongException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because the path is too long. InputPath={InputPath}", trimmed);
            return trimmed;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because access to the path was denied. InputPath={InputPath}", trimmed);
            return trimmed;
        }
    }
}
