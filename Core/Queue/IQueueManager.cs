using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

public enum QueueEnqueueResult
{
    Accepted = 0,
    RejectedInvalidItem = 1,
    RejectedAlreadyRunning = 2,
    RejectedDuplicatePath = 3,
    RejectedQueueShuttingDown = 4
}

public interface IQueueManager : IAsyncDisposable
{
    IReadOnlyCollection<ChdQueueItem> Items { get; }

    event Action<ChdQueueItem>? ItemUpdated;

    QueueEnqueueResult Enqueue(ChdQueueItem item);

    void Start();

    void Stop();

    void Cancel(Guid id);
}