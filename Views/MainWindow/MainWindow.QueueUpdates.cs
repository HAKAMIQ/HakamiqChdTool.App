using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private string GetChdmanPath() => _chdmanPathResolver.ResolvePath(_settings);

    private void OnQueueItemUpdated(ChdQueueItem item)
    {
        _pendingQueueUiSnapshots[item.Id] = CloneQueueItemSnapshot(item);

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            FlushPendingQueueItemUpdates();
            return;
        }

        if (Interlocked.Exchange(ref _pendingQueueUiFlush, 1) != 0)
        {
            return;
        }

        SchedulePendingQueueUiFlush();
    }

    private void FlushPendingQueueItemUpdates()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Interlocked.Exchange(ref _pendingQueueUiFlush, 0);
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            if (Interlocked.Exchange(ref _pendingQueueUiFlush, 1) == 0)
            {
                SchedulePendingQueueUiFlush();
            }

            return;
        }

        try
        {
            foreach (KeyValuePair<Guid, ChdQueueItem> entry in _pendingQueueUiSnapshots.ToArray())
            {
                if (_pendingQueueUiSnapshots.TryRemove(entry.Key, out ChdQueueItem? snapshot))
                {
                    ApplyQueueItemUpdate(snapshot);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pendingQueueUiFlush, 0);

            if (!_pendingQueueUiSnapshots.IsEmpty
                && Interlocked.Exchange(ref _pendingQueueUiFlush, 1) == 0)
            {
                SchedulePendingQueueUiFlush();
            }
        }
    }

    private void SchedulePendingQueueUiFlush()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Interlocked.Exchange(ref _pendingQueueUiFlush, 0);
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                new Action(FlushPendingQueueItemUpdates),
                DispatcherPriority.Background);
        }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Interlocked.Exchange(ref _pendingQueueUiFlush, 0);
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            Interlocked.Exchange(ref _pendingQueueUiFlush, 0);
        }
    }

    private void ApplyQueueItemUpdate(ChdQueueItem item)
    {
        QueueRowData? row = _queueRowStore.GetById(item.Id);
        if (row is null)
        {
            return;
        }

        TaskQueueItemViewModel? task = _viewport.TryGetMaterialized(item.Id);

        if (IsTerminalQueueStatus(item.Status))
        {
            SetHasActiveQueueBindingAndSync(item.Id, false);
        }

        task?.ApplyQueueItemSnapshot(item);

        if (ShouldLogTerminalQueueError(item))
        {
            string queueSignature = $"queue|{item.Id}|{item.Status}|{item.Error}";
            if (_loggedExecutionSignatures.Count >= MaxExecutionSignatures)
            {
                _loggedExecutionSignatures.Clear();
            }

            if (_loggedExecutionSignatures.Add(queueSignature))
            {
                string errorText = ArabicUi.ResolveDisplayString(item.Error);
                AppendExecutionLog($"{row.FileName}: {errorText}");
            }
        }
    }

    private TaskQueueItemViewModel? TryResolveTaskByQueueItemId(Guid id) =>
        _viewport.TryGetMaterialized(id);

    private static ChdQueueItem CloneQueueItemSnapshot(ChdQueueItem item)
    {
        return new ChdQueueItem
        {
            Id = item.Id,
            InputPath = item.InputPath,
            Status = item.Status,
            Progress = item.Progress,
            OutputPath = item.OutputPath,
            Error = item.Error,
            Mode = item.Mode,
            ExecutionProfile = item.ExecutionProfile
        };
    }

    private static bool IsTerminalQueueStatus(string? status)
    {
        return TaskQueueStateCodes.IsTerminal(status);
    }

    private static bool ShouldLogTerminalQueueError(ChdQueueItem item)
    {
        return (string.Equals(item.Status, TaskQueueStateCodes.Failed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Status, TaskQueueStateCodes.Cancelled, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(item.Error);
    }
}