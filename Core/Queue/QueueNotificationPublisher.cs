using System;

namespace HakamiqChdTool.App.Core.Queue;

internal sealed class QueueNotificationPublisher
{
    private Func<Guid, QueueItemSnapshot?> _resolveSnapshot = static _ => null;
    private Func<Guid, IQueueItemStateSink?> _resolveSink = static _ => null;
    private Action _onUiRefresh = static () => { };

    public event Action<ChdQueueItem>? ItemUpdated;

    public QueueItemSnapshot? ResolveSnapshot(Guid itemId) => _resolveSnapshot(itemId);

    public IQueueItemStateSink? ResolveSink(Guid itemId) => _resolveSink(itemId);

    public void RefreshUi() => _onUiRefresh();

    public void PublishItemUpdated(ChdQueueItem item) => ItemUpdated?.Invoke(item);

    public void ConfigureUiBindings(
        Func<Guid, QueueItemSnapshot?> resolveSnapshot,
        Func<Guid, IQueueItemStateSink?> resolveSink,
        Action onUiRefresh)
    {
        ArgumentNullException.ThrowIfNull(resolveSnapshot);
        ArgumentNullException.ThrowIfNull(resolveSink);
        ArgumentNullException.ThrowIfNull(onUiRefresh);

        _resolveSnapshot = resolveSnapshot;
        _resolveSink = resolveSink;
        _onUiRefresh = onUiRefresh;
    }

    public void Clear()
    {
        ItemUpdated = null;
        _resolveSnapshot = static _ => null;
        _resolveSink = static _ => null;
        _onUiRefresh = static () => { };
    }
}
