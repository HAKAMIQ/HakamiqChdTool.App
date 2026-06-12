using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

public sealed class QueueManager : IQueueManager
{
    private const int DefaultMaxConcurrentItems = AppSettings.DefaultMaxConcurrentConversions;

    private readonly QueueRuntimeState _state;
    private readonly QueueNotificationPublisher _notifications;
    private readonly QueueStateStore _stateStore;
    private readonly QueueTransitionService _transitionService;
    private readonly QueueConcurrencyCoordinator _concurrencyCoordinator;

    public event Action<ChdQueueItem>? ItemUpdated
    {
        add => _notifications.ItemUpdated += value;
        remove => _notifications.ItemUpdated -= value;
    }

    public IReadOnlyCollection<ChdQueueItem> Items => _stateStore.Items;

    public QueueManager(
        IChdWorkflowOrchestrator orchestrator,
        Func<AppSettings> getSettings,
        Func<string> getChdmanPath,
        int maxConcurrentItems = DefaultMaxConcurrentItems,
        Func<AppFeature, bool>? canUseAppFeature = null)
    {
        _state = new QueueRuntimeState(orchestrator, getSettings, getChdmanPath, maxConcurrentItems, canUseAppFeature);
        _notifications = new QueueNotificationPublisher();
        _stateStore = new QueueStateStore(_state, _notifications);
        _transitionService = new QueueTransitionService(_state, _notifications);
        _concurrencyCoordinator = new QueueConcurrencyCoordinator(_state, _stateStore, _transitionService, _notifications);
    }

    public QueueManager(
        IChdWorkflowOrchestrator orchestrator,
        Func<AppSettings> getSettings,
        Func<string> getChdmanPath,
        Func<Guid, QueueItemSnapshot?> resolveSnapshot,
        Func<Guid, IQueueItemStateSink?> resolveSink,
        Action onUiRefresh,
        int maxConcurrentItems = DefaultMaxConcurrentItems,
        Func<AppFeature, bool>? canUseAppFeature = null)
        : this(orchestrator, getSettings, getChdmanPath, maxConcurrentItems, canUseAppFeature)
    {
        ConfigureUiBindings(resolveSnapshot, resolveSink, onUiRefresh);
    }

    public void ConfigureUiBindings(
        Func<Guid, QueueItemSnapshot?> resolveSnapshot,
        Func<Guid, IQueueItemStateSink?> resolveSink,
        Action onUiRefresh)
    {
        _notifications.ConfigureUiBindings(resolveSnapshot, resolveSink, onUiRefresh);
    }

    public QueueEnqueueResult Enqueue(ChdQueueItem item) => _stateStore.Enqueue(item);

    public void Start() => _concurrencyCoordinator.Start();

    public void Stop() => _concurrencyCoordinator.Stop();

    public void Cancel(Guid id) => _stateStore.Cancel(id);

    public void UpdateMaxConcurrentItems(int maxConcurrentItems) => _concurrencyCoordinator.UpdateMaxConcurrentItems(maxConcurrentItems);

    public ValueTask DisposeAsync() => _concurrencyCoordinator.DisposeAsync();
}
