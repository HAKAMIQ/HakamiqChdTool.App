using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace HakamiqChdTool.App.ViewModels.Virtualization;

public sealed class VirtualizedQueueCollection : IList, INotifyCollectionChanged, INotifyPropertyChanged, IDisposable
{
    private readonly QueueRowStore _store;
    private readonly QueueViewportService _viewport;
    private readonly Func<QueueRowData, bool> _isVisible;
    private readonly object _syncRoot = new();

    private QueueRowData[] _visibleRows = Array.Empty<QueueRowData>();
    private bool _disposed;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public VirtualizedQueueCollection(
        QueueRowStore store,
        QueueViewportService viewport,
        Func<QueueRowData, bool>? isVisible = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _isVisible = isVisible ?? (_ => true);

        RebuildVisibleRows();
        _store.CollectionChanged += OnStoreCollectionChanged;
    }

    public int Count => Volatile.Read(ref _visibleRows).Length;

    public bool IsFixedSize => false;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => _syncRoot;

    public object? this[int index]
    {
        get
        {
            QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
            return index >= 0 && index < visibleRows.Length
                ? _viewport.Realize(visibleRows[index])
                : null;
        }
        set => throw new NotSupportedException();
    }

    public int IndexOfRowId(Guid rowId)
    {
        QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
        return IndexOfVisibleRow(visibleRows, rowId);
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => _store.Clear();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
        if (value is not TaskQueueItemViewModel vm)
        {
            return -1;
        }

        QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
        for (int i = 0; i < visibleRows.Length; i++)
        {
            if (visibleRows[i].ItemId == vm.QueueItemId)
            {
                return i;
            }
        }

        return -1;
    }

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value)
    {
        if (value is TaskQueueItemViewModel vm)
        {
            _store.TryRemove(vm.QueueItemId);
        }
    }

    public void RemoveAt(int index)
    {
        QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
        if (index >= 0 && index < visibleRows.Length)
        {
            _store.TryRemove(visibleRows[index].ItemId);
        }
    }

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);

        QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
        for (int i = 0; i < visibleRows.Length; i++)
        {
            array.SetValue(_viewport.Realize(visibleRows[i]), index + i);
        }
    }

    public IEnumerator GetEnumerator()
    {
        QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
        for (int i = 0; i < visibleRows.Length; i++)
        {
            yield return _viewport.Realize(visibleRows[i]);
        }
    }

    public void RefreshView()
    {
        RebuildVisibleRows();
        RaiseReset();
        ReleaseHiddenMaterializedRows();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.CollectionChanged -= OnStoreCollectionChanged;
        GC.SuppressFinalize(this);
    }

    private void OnStoreCollectionChanged(QueueRowStoreCollectionDelta delta)
    {
        if (_disposed)
        {
            return;
        }

        switch (delta)
        {
            case QueueRowStoreAppended appended:
                ApplyAppend(appended);
                break;

            case QueueRowStoreRemoved removed:
                ApplyRemove(removed);
                break;

            case QueueRowStoreReset:
                RebuildVisibleRows();
                RaiseReset();
                _viewport.ClearMaterialized();
                break;

            default:
                RebuildVisibleRows();
                RaiseReset();
                ReleaseHiddenMaterializedRows();
                break;
        }
    }

    private void ApplyAppend(QueueRowStoreAppended appended)
    {
        if (appended.Count <= 0)
        {
            return;
        }

        QueueRowData[] oldVisibleRows = Volatile.Read(ref _visibleRows);
        List<QueueRowData>? appendedVisibleRows = null;

        for (int i = 0; i < appended.Count; i++)
        {
            QueueRowData? row = _store.GetByIndex(appended.StartIndex + i);
            if (row is null || !_isVisible(row))
            {
                continue;
            }

            appendedVisibleRows ??= new List<QueueRowData>(appended.Count);
            appendedVisibleRows.Add(row);
        }

        if (appendedVisibleRows is null || appendedVisibleRows.Count == 0)
        {
            return;
        }

        var newVisibleRows = new QueueRowData[oldVisibleRows.Length + appendedVisibleRows.Count];
        Array.Copy(oldVisibleRows, newVisibleRows, oldVisibleRows.Length);
        appendedVisibleRows.CopyTo(newVisibleRows, oldVisibleRows.Length);
        Volatile.Write(ref _visibleRows, newVisibleRows);

        if (appendedVisibleRows.Count == 1)
        {
            RaiseAdd(_viewport.Realize(appendedVisibleRows[0]), oldVisibleRows.Length);
            return;
        }

        var addedItems = new List<TaskQueueItemViewModel>(appendedVisibleRows.Count);
        foreach (QueueRowData row in appendedVisibleRows)
        {
            addedItems.Add(_viewport.Realize(row));
        }

        RaiseAdd(addedItems, oldVisibleRows.Length);
    }

    private void ApplyRemove(QueueRowStoreRemoved removed)
    {
        QueueRowData[] oldVisibleRows = Volatile.Read(ref _visibleRows);
        int oldVisibleIndex = IndexOfVisibleRow(oldVisibleRows, removed.RemovedRow.ItemId);

        if (oldVisibleIndex < 0)
        {
            _viewport.ReleaseRemovedRow(removed.RemovedRow.ItemId);
            return;
        }

        TaskQueueItemViewModel? materializedVm = _viewport.TryGetMaterialized(removed.RemovedRow.ItemId);
        TaskQueueItemViewModel removedItem = materializedVm ?? _viewport.CreateDetached(removed.RemovedRow);

        try
        {
            QueueRowData[] newVisibleRows = RemoveVisibleRowAt(oldVisibleRows, oldVisibleIndex);
            Volatile.Write(ref _visibleRows, newVisibleRows);
            RaiseRemove(removedItem, oldVisibleIndex);

            if (materializedVm is not null)
            {
                _viewport.ReleaseRemovedRow(removed.RemovedRow.ItemId);
            }
        }
        finally
        {
            if (materializedVm is null)
            {
                DisposeDetachedViewModel(removedItem);
            }
        }
    }

    private void RebuildVisibleRows()
    {
        QueueRowData[] allRows = _store.GetRowsSnapshot();
        QueueRowData[] visible = allRows.Length == 0
            ? Array.Empty<QueueRowData>()
            : allRows.Where(_isVisible).ToArray();

        Volatile.Write(ref _visibleRows, visible);
    }

    private void ReleaseHiddenMaterializedRows()
    {
        QueueRowData[] visibleRows = Volatile.Read(ref _visibleRows);
        var visibleIds = new HashSet<Guid>(visibleRows.Select(static row => row.ItemId));

        foreach (TaskQueueItemViewModel vm in _viewport.MaterializedViewModels)
        {
            if (!visibleIds.Contains(vm.QueueItemId))
            {
                _viewport.ReleaseById(vm.QueueItemId);
            }
        }
    }

    private static QueueRowData[] RemoveVisibleRowAt(QueueRowData[] oldVisibleRows, int oldVisibleIndex)
    {
        if (oldVisibleRows.Length == 1)
        {
            return Array.Empty<QueueRowData>();
        }

        var newVisibleRows = new QueueRowData[oldVisibleRows.Length - 1];

        if (oldVisibleIndex > 0)
        {
            Array.Copy(oldVisibleRows, 0, newVisibleRows, 0, oldVisibleIndex);
        }

        int itemsAfterRemoved = oldVisibleRows.Length - oldVisibleIndex - 1;
        if (itemsAfterRemoved > 0)
        {
            Array.Copy(oldVisibleRows, oldVisibleIndex + 1, newVisibleRows, oldVisibleIndex, itemsAfterRemoved);
        }

        return newVisibleRows;
    }

    private static int IndexOfVisibleRow(QueueRowData[] visibleRows, Guid rowId)
    {
        for (int i = 0; i < visibleRows.Length; i++)
        {
            if (visibleRows[i].ItemId == rowId)
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseAdd(TaskQueueItemViewModel item, int newIndex)
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, newIndex));
        RaiseCollectionPropertiesChanged();
    }

    private void RaiseAdd(IList items, int newStartingIndex)
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, newStartingIndex));
        RaiseCollectionPropertiesChanged();
    }

    private void RaiseRemove(TaskQueueItemViewModel item, int oldIndex)
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, oldIndex));
        RaiseCollectionPropertiesChanged();
    }

    private void RaiseReset()
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RaiseCollectionPropertiesChanged();
    }

    private void RaiseCollectionPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static void DisposeDetachedViewModel(TaskQueueItemViewModel vm)
    {
        if (vm is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}