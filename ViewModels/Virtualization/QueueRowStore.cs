using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.ViewModels.Virtualization;

public abstract record QueueRowStoreCollectionDelta;

public sealed record QueueRowStoreAppended(int StartIndex, int Count) : QueueRowStoreCollectionDelta;

public sealed record QueueRowStoreRemoved(int StartIndex, int Count, QueueRowData RemovedRow) : QueueRowStoreCollectionDelta;

public sealed record QueueRowStoreReset : QueueRowStoreCollectionDelta;

public sealed class QueueRowStore
{
    private readonly object _gate = new();
    private readonly List<QueueRowData> _rows = new();
    private readonly Dictionary<Guid, int> _indexById = new();
    private QueueRowData[] _snapshot = Array.Empty<QueueRowData>();
    private long _version;
    private long _snapshotVersion = -1;

    public event Action<QueueRowStoreCollectionDelta>? CollectionChanged;
    public event Action<Guid>? RowMutated;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _rows.Count;
            }
        }
    }

    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public IReadOnlyList<QueueRowData> Rows => GetRowsSnapshot();

    public QueueRowData[] GetRowsSnapshot()
    {
        lock (_gate)
        {
            QueueRowData[] snapshot = GetRowsSnapshotUnsafe();
            return snapshot.Length == 0
                ? Array.Empty<QueueRowData>()
                : (QueueRowData[])snapshot.Clone();
        }
    }

    public void Append(QueueRowData row)
    {
        ArgumentNullException.ThrowIfNull(row);

        int newIndex;

        lock (_gate)
        {
            if (_indexById.ContainsKey(row.ItemId))
            {
                throw new InvalidOperationException();
            }

            newIndex = _rows.Count;
            _rows.Add(row);
            _indexById[row.ItemId] = newIndex;
            _version++;
        }

        CollectionChanged?.Invoke(new QueueRowStoreAppended(newIndex, 1));
    }

    /// <summary>Appends rows as one change: a single notification instead of one per row.</summary>
    public void AppendRange(IReadOnlyList<QueueRowData> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return;
        }

        int startIndex;

        lock (_gate)
        {
            var seen = new HashSet<Guid>(rows.Count);
            foreach (QueueRowData row in rows)
            {
                ArgumentNullException.ThrowIfNull(row);
                if (_indexById.ContainsKey(row.ItemId) || !seen.Add(row.ItemId))
                {
                    throw new InvalidOperationException();
                }
            }

            startIndex = _rows.Count;
            foreach (QueueRowData row in rows)
            {
                _indexById[row.ItemId] = _rows.Count;
                _rows.Add(row);
            }

            _version++;
        }

        CollectionChanged?.Invoke(new QueueRowStoreAppended(startIndex, rows.Count));
    }

    public bool TryRemove(Guid id)
    {
        int removedIndex;
        QueueRowData removedRow;

        lock (_gate)
        {
            if (!_indexById.TryGetValue(id, out removedIndex))
            {
                return false;
            }

            removedRow = _rows[removedIndex];
            _rows.RemoveAt(removedIndex);
            _indexById.Remove(id);

            for (int i = removedIndex; i < _rows.Count; i++)
            {
                _indexById[_rows[i].ItemId] = i;
            }

            _version++;
        }

        CollectionChanged?.Invoke(new QueueRowStoreRemoved(removedIndex, 1, removedRow));
        return true;
    }

    public void Clear()
    {
        bool hadAny;

        lock (_gate)
        {
            hadAny = _rows.Count > 0;
            _rows.Clear();
            _indexById.Clear();

            if (hadAny)
            {
                _version++;
            }
        }

        if (hadAny)
        {
            CollectionChanged?.Invoke(new QueueRowStoreReset());
        }
    }

    public QueueRowData? GetById(Guid id)
    {
        lock (_gate)
        {
            return _indexById.TryGetValue(id, out int index) ? _rows[index] : null;
        }
    }

    public QueueRowData? GetByIndex(int index)
    {
        lock (_gate)
        {
            return index >= 0 && index < _rows.Count ? _rows[index] : null;
        }
    }

    public int IndexOf(Guid id)
    {
        lock (_gate)
        {
            return _indexById.TryGetValue(id, out int index) ? index : -1;
        }
    }

    public bool Mutate(Guid id, Action<QueueRowData> patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        bool mutated;

        lock (_gate)
        {
            if (!_indexById.TryGetValue(id, out int index))
            {
                return false;
            }

            patch(_rows[index]);
            mutated = true;
        }

        if (mutated)
        {
            RowMutated?.Invoke(id);
        }

        return mutated;
    }

    private QueueRowData[] GetRowsSnapshotUnsafe()
    {
        if (_snapshotVersion != _version)
        {
            _snapshot = _rows.Count == 0 ? Array.Empty<QueueRowData>() : _rows.ToArray();
            _snapshotVersion = _version;
        }

        return _snapshot;
    }
}