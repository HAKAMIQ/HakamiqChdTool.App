using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static QueueRowData NewQueueTestRow(Guid? id = null) =>
        new() { ItemId = id ?? Guid.NewGuid(), OriginalPath = "C:/x/" + Guid.NewGuid().ToString("N") + ".iso" };

    // A batch raises one change, keeps order and indexes, and is all-or-nothing on a duplicate id.
    private static void TestQueueRowStoreAppendRangeIsOneAtomicChange(string workDirectory)
    {
        var store = new QueueRowStore();
        var view = new VirtualizedQueueCollection(
            store,
            new QueueViewportService(store, row => new TaskQueueItemViewModel(row), (_, _) => { }),
            static _ => true);

        var deltas = new List<QueueRowStoreCollectionDelta>();
        store.CollectionChanged += deltas.Add;
        int viewNotifications = 0;
        ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => viewNotifications++;

        QueueRowData existing = NewQueueTestRow();
        store.Append(existing);
        deltas.Clear();
        viewNotifications = 0;

        QueueRowData[] batch = Enumerable.Range(0, 500).Select(_ => NewQueueTestRow()).ToArray();
        store.AppendRange(batch);

        AssertEqual(1, deltas.Count, "A batch raises one store change.");
        AssertTrue(deltas[0] is QueueRowStoreAppended { StartIndex: 1, Count: 500 }, "The change describes the whole range.");
        AssertEqual(1, viewNotifications, "The virtualized view raises one notification for the batch.");
        AssertEqual(501, store.Count, "All rows are stored.");
        AssertEqual(501, view.Count, "All rows are visible.");
        AssertEqual(1 + 250, store.IndexOf(batch[250].ItemId), "Index lookup is correct inside the batch.");

        deltas.Clear();
        QueueRowData[] withDuplicate = [NewQueueTestRow(), NewQueueTestRow(existing.ItemId)];
        bool threw = false;
        try { store.AppendRange(withDuplicate); } catch (InvalidOperationException) { threw = true; }
        AssertTrue(threw, "A duplicate id rejects the batch.");
        AssertEqual(501, store.Count, "A rejected batch adds nothing.");
        AssertEqual(0, deltas.Count, "A rejected batch raises no change.");

        QueueRowData twin = NewQueueTestRow();
        threw = false;
        try { store.AppendRange([twin, NewQueueTestRow(twin.ItemId)]); } catch (InvalidOperationException) { threw = true; }
        AssertTrue(threw, "A duplicate inside the batch is rejected.");
        AssertEqual(501, store.Count, "Nothing from the rejected batch remains.");
        view.Dispose();
    }
}
