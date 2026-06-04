using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.ViewModels.Virtualization;

public sealed class QueueViewportService : IDisposable
{
    private readonly QueueRowStore _store;
    private readonly Func<QueueRowData, TaskQueueItemViewModel> _vmFactory;
    private readonly Action<TaskQueueItemViewModel, QueueRowData> _applyRowToVm;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, TaskQueueItemViewModel> _pool = new();
    private readonly Dictionary<Guid, int> _pinCounts = new();

    private Func<Guid, int>? _visibleIndexResolver;
    private VisibleQueueWindow _lastWindow = VisibleQueueWindow.Empty;
    private bool _disposed;

    public event Action<Guid, TaskQueueItemViewModel>? VmMaterialized;
    public event Action<Guid, TaskQueueItemViewModel>? VmReleased;

    public QueueViewportService(
        QueueRowStore store,
        Func<QueueRowData, TaskQueueItemViewModel> vmFactory,
        Action<TaskQueueItemViewModel, QueueRowData> applyRowToVm)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _vmFactory = vmFactory ?? throw new ArgumentNullException(nameof(vmFactory));
        _applyRowToVm = applyRowToVm ?? throw new ArgumentNullException(nameof(applyRowToVm));

        _store.RowMutated += OnRowMutated;
    }

    public VisibleQueueWindow LastWindow
    {
        get
        {
            lock (_gate)
            {
                return _lastWindow;
            }
        }
    }

    public IReadOnlyCollection<TaskQueueItemViewModel> MaterializedViewModels
    {
        get
        {
            lock (_gate)
            {
                if (_pool.Count == 0)
                {
                    return Array.Empty<TaskQueueItemViewModel>();
                }

                var snapshot = new TaskQueueItemViewModel[_pool.Count];
                _pool.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }
    }

    public int MaterializedCount
    {
        get
        {
            lock (_gate)
            {
                return _pool.Count;
            }
        }
    }

    public void SetVisibleIndexResolver(Func<Guid, int>? visibleIndexResolver)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _visibleIndexResolver = visibleIndexResolver;
        }
    }

    public TaskQueueItemViewModel? Realize(int index)
    {
        ThrowIfDisposed();

        QueueRowData? row = _store.GetByIndex(index);
        return row is null ? null : Realize(row);
    }

    public TaskQueueItemViewModel Realize(QueueRowData row)
    {
        ArgumentNullException.ThrowIfNull(row);

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_pool.TryGetValue(row.ItemId, out TaskQueueItemViewModel? existing))
            {
                return existing;
            }
        }

        TaskQueueItemViewModel created = _vmFactory(row);
        ArgumentNullException.ThrowIfNull(created);

        TaskQueueItemViewModel vm;
        bool newlyMaterialized;

        lock (_gate)
        {
            if (_disposed)
            {
                DisposeReleasedViewModel(created);
                throw new ObjectDisposedException(nameof(QueueViewportService));
            }

            if (_pool.TryGetValue(row.ItemId, out TaskQueueItemViewModel? existing))
            {
                vm = existing;
                newlyMaterialized = false;
            }
            else
            {
                _pool[row.ItemId] = created;
                vm = created;
                newlyMaterialized = true;
            }
        }

        if (!newlyMaterialized)
        {
            DisposeReleasedViewModel(created);
            return vm;
        }

        VmMaterialized?.Invoke(row.ItemId, vm);
        return vm;
    }

    public TaskQueueItemViewModel CreateDetached(QueueRowData row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ThrowIfDisposed();

        TaskQueueItemViewModel vm = _vmFactory(row);
        ArgumentNullException.ThrowIfNull(vm);

        return vm;
    }

    public bool Release(int index)
    {
        ThrowIfDisposed();

        QueueRowData? row = _store.GetByIndex(index);
        return row is not null && ReleaseById(row.ItemId);
    }

    public bool ReleaseById(Guid id)
    {
        ThrowIfDisposed();
        return ReleaseByIdCore(id, force: false);
    }

    public bool ReleaseRemovedRow(Guid id)
    {
        ThrowIfDisposed();
        return ReleaseByIdCore(id, force: true);
    }

    public void Pin(Guid id)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            _pinCounts.TryGetValue(id, out int count);
            _pinCounts[id] = count + 1;
        }
    }

    public void Unpin(Guid id)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_pinCounts.TryGetValue(id, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                _pinCounts.Remove(id);
                return;
            }

            _pinCounts[id] = count - 1;
        }
    }

    public bool IsPinned(Guid id)
    {
        lock (_gate)
        {
            return IsPinnedCore(id);
        }
    }

    public TaskQueueItemViewModel? TryGetMaterialized(Guid id)
    {
        lock (_gate)
        {
            return _pool.TryGetValue(id, out TaskQueueItemViewModel? vm) ? vm : null;
        }
    }

    public void ClearMaterialized()
    {
        List<KeyValuePair<Guid, TaskQueueItemViewModel>>? snapshot;

        lock (_gate)
        {
            ThrowIfDisposed();
            snapshot = ClearMaterializedUnsafe();
        }

        ReleaseSnapshot(snapshot);
    }

    public void UpdateWindow(VisibleQueueWindow window)
    {
        ThrowIfDisposed();

        List<KeyValuePair<Guid, TaskQueueItemViewModel>> poolSnapshot;
        Func<Guid, int>? visibleIndexResolver;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (window.Equals(_lastWindow))
            {
                return;
            }

            _lastWindow = window;
            visibleIndexResolver = _visibleIndexResolver;
            poolSnapshot = new List<KeyValuePair<Guid, TaskQueueItemViewModel>>(_pool);
        }

        List<KeyValuePair<Guid, TaskQueueItemViewModel>>? toRelease = null;

        foreach (KeyValuePair<Guid, TaskQueueItemViewModel> entry in poolSnapshot)
        {
            if (ShouldKeepMaterialized(entry.Key, window, visibleIndexResolver))
            {
                continue;
            }

            toRelease ??= new List<KeyValuePair<Guid, TaskQueueItemViewModel>>();
            toRelease.Add(entry);
        }

        if (toRelease is null)
        {
            return;
        }

        List<KeyValuePair<Guid, TaskQueueItemViewModel>>? released = null;

        lock (_gate)
        {
            if (_disposed || !window.Equals(_lastWindow))
            {
                return;
            }

            foreach (KeyValuePair<Guid, TaskQueueItemViewModel> entry in toRelease)
            {
                if (IsPinnedCore(entry.Key))
                {
                    continue;
                }

                if (_pool.TryGetValue(entry.Key, out TaskQueueItemViewModel? current)
                    && ReferenceEquals(current, entry.Value))
                {
                    _pool.Remove(entry.Key);
                    released ??= new List<KeyValuePair<Guid, TaskQueueItemViewModel>>();
                    released.Add(entry);
                }
            }
        }

        ReleaseSnapshot(released);
    }

    public void Dispose()
    {
        List<KeyValuePair<Guid, TaskQueueItemViewModel>>? snapshot;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            snapshot = ClearMaterializedUnsafe();
            _visibleIndexResolver = null;
        }

        _store.RowMutated -= OnRowMutated;

        ReleaseSnapshot(snapshot);
        GC.SuppressFinalize(this);
    }

    private bool ReleaseByIdCore(Guid id, bool force)
    {
        TaskQueueItemViewModel? vm = null;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (!force && IsPinnedCore(id))
            {
                return false;
            }

            if (force)
            {
                _pinCounts.Remove(id);
            }

            if (_pool.TryGetValue(id, out vm))
            {
                _pool.Remove(id);
            }
        }

        if (vm is null)
        {
            return false;
        }

        VmReleased?.Invoke(id, vm);
        DisposeReleasedViewModel(vm);
        return true;
    }

    private bool ShouldKeepMaterialized(
        Guid id,
        VisibleQueueWindow window,
        Func<Guid, int>? visibleIndexResolver)
    {
        lock (_gate)
        {
            if (IsPinnedCore(id))
            {
                return true;
            }
        }

        if (window.Count <= 0)
        {
            return false;
        }

        QueueRowData? row = _store.GetById(id);
        if (row is null)
        {
            return false;
        }

        int index = visibleIndexResolver?.Invoke(id) ?? _store.IndexOf(row.ItemId);
        return index >= 0 && window.Contains(index);
    }

    private bool IsPinnedCore(Guid id) => _pinCounts.TryGetValue(id, out int count) && count > 0;

    private void OnRowMutated(Guid id)
    {
        TaskQueueItemViewModel? vm;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pool.TryGetValue(id, out vm);
        }

        if (vm is null)
        {
            return;
        }

        QueueRowData? row = _store.GetById(id);
        if (row is null)
        {
            return;
        }

        _applyRowToVm(vm, row);
    }

    private List<KeyValuePair<Guid, TaskQueueItemViewModel>>? ClearMaterializedUnsafe()
    {
        if (_pool.Count == 0)
        {
            _pinCounts.Clear();
            return null;
        }

        var snapshot = new List<KeyValuePair<Guid, TaskQueueItemViewModel>>(_pool.Count);
        foreach (KeyValuePair<Guid, TaskQueueItemViewModel> entry in _pool)
        {
            snapshot.Add(entry);
        }

        _pool.Clear();
        _pinCounts.Clear();

        return snapshot;
    }

    private void ReleaseSnapshot(List<KeyValuePair<Guid, TaskQueueItemViewModel>>? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        foreach (KeyValuePair<Guid, TaskQueueItemViewModel> entry in snapshot)
        {
            VmReleased?.Invoke(entry.Key, entry.Value);
            DisposeReleasedViewModel(entry.Value);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(QueueViewportService));
        }
    }

    private static void DisposeReleasedViewModel(TaskQueueItemViewModel vm)
    {
        if (vm is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}