using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Data;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static QueueRowData NamedQueueRow(string fileName, bool visible = true) =>
        new() { ItemId = Guid.NewGuid(), OriginalPath = "C:/x/" + fileName, FileName = fileName, IsVisibleInCurrentOperationMode = visible };

    private static VirtualizedQueueCollection NewQueueView(QueueRowStore store) =>
        new(store, new QueueViewportService(store, row => new TaskQueueItemViewModel(row), (_, _) => { }), static row => row.IsVisibleInCurrentOperationMode);

    // A batch that mixes visible and filtered-out rows raises one Add for the visible rows only, in store order.
    private static void TestQueueBatchRespectsOperationModeFilter(string workDirectory)
    {
        var store = new QueueRowStore();
        using VirtualizedQueueCollection view = NewQueueView(store);
        var events = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)view).CollectionChanged += (_, e) => events.Add(e);

        QueueRowData[] rows = Enumerable.Range(0, 10).Select(i => NamedQueueRow("f" + i + ".iso", visible: i % 2 == 0)).ToArray();
        store.AppendRange(rows);

        AssertEqual(10, store.Count, "All rows are stored.");
        AssertEqual(5, view.Count, "Only visible rows appear.");
        AssertEqual(5, events.Count, "A small batch raises one single-item Add per visible row (WPF rejects range Adds).");
        AssertTrue(events.All(e => e.Action == NotifyCollectionChangedAction.Add && e.NewItems!.Count == 1), "Every notification is a single-item Add.");
        AssertEqual(new[] { 0, 1, 2, 3, 4 }.Sum(), events.Sum(e => e.NewStartingIndex), "Add indexes are consecutive.");
        AssertEqual("f0.iso", ((TaskQueueItemViewModel)view[0]!).FileName, "Order is the store order.");
        AssertEqual("f8.iso", ((TaskQueueItemViewModel)view[4]!).FileName, "The last visible row is last.");

        // A large batch is published as one Reset.
        events.Clear();
        store.AppendRange(Enumerable.Range(0, 100).Select(i => NamedQueueRow("big" + i + ".iso")).ToArray());
        AssertEqual(1, events.Count, "A large batch raises one notification.");
        AssertEqual(NotifyCollectionChangedAction.Reset, events[0].Action, "The notification is a Reset.");
        AssertEqual(105, view.Count, "All rows of the large batch are visible.");

        // A batch that is entirely filtered out raises nothing.
        events.Clear();
        store.AppendRange([NamedQueueRow("hidden.iso", visible: false)]);
        AssertEqual(0, events.Count, "A fully filtered batch raises no change.");
        AssertEqual(105, view.Count, "The visible count is unchanged.");
    }

    // A real WPF sorted/filtered CollectionView over the collection stays consistent across batches.
    private static void TestQueueBatchKeepsSortedCollectionViewConsistent(string workDirectory)
    {
        RunOnSta(() =>
        {
            var store = new QueueRowStore();
            using VirtualizedQueueCollection view = NewQueueView(store);
            var collectionView = new ListCollectionView(view);
            collectionView.SortDescriptions.Add(new SortDescription(nameof(TaskQueueItemViewModel.FileName), ListSortDirection.Descending));

            store.AppendRange(Enumerable.Range(0, 300).Select(i => NamedQueueRow("n" + i.ToString("D4") + ".iso")).ToArray());
            store.AppendRange(Enumerable.Range(300, 200).Select(i => NamedQueueRow("n" + i.ToString("D4") + ".iso")).ToArray());

            AssertEqual(500, collectionView.Count, "The sorted view sees every row after two batches.");
            AssertEqual("n0499.iso", ((TaskQueueItemViewModel)collectionView.GetItemAt(0)).FileName, "Descending sort puts the newest first.");
            AssertEqual("n0000.iso", ((TaskQueueItemViewModel)collectionView.GetItemAt(499)).FileName, "Descending sort puts the oldest last.");

            collectionView.Filter = item => ((TaskQueueItemViewModel)item).FileName.EndsWith("7.iso", StringComparison.Ordinal);
            AssertEqual(50, collectionView.Count, "A view filter applies on top of the batch.");

            store.AppendRange([NamedQueueRow("n9997.iso"), NamedQueueRow("n9998.iso")]);
            AssertEqual(51, collectionView.Count, "A later batch is filtered by the view filter too.");
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    // Repeated sessions: views that were disposed no longer observe the store, and a new view sees the current rows.
    private static void TestQueueBatchAcrossRepeatedViewSessions(string workDirectory)
    {
        var store = new QueueRowStore();
        int notificationsToDisposed = 0;

        for (int session = 0; session < 25; session++)
        {
            VirtualizedQueueCollection view = NewQueueView(store);
            ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => notificationsToDisposed++;
            AssertEqual(store.Count, view.Count, "A new session starts from the current rows.");
            store.AppendRange([NamedQueueRow("s" + session + "a.iso"), NamedQueueRow("s" + session + "b.iso")]);
            AssertEqual(store.Count, view.Count, "The live session sees the batch.");
            view.Dispose();
        }

        int before = notificationsToDisposed;
        store.AppendRange([NamedQueueRow("after.iso")]);
        AssertEqual(before, notificationsToDisposed, "Disposed views receive no further notifications.");
        AssertEqual(51, store.Count, "All batches are in the store.");
    }

    // The intake loop adds rows that were built before a cancellation: AppendRange in a finally block keeps them.
    private static void TestQueueBatchKeepsPartialProgressWhenLoopIsCancelled(string workDirectory)
    {
        var store = new QueueRowStore();
        using var cts = new System.Threading.CancellationTokenSource();
        var built = new List<QueueRowData>();

        try
        {
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    built.Add(NamedQueueRow("p" + i + ".iso"));
                    if (i == 3)
                    {
                        cts.Cancel();
                    }
                }
            }
            finally
            {
                store.AppendRange(built);
            }
        }
        catch (OperationCanceledException)
        {
        }

        AssertEqual(4, store.Count, "Rows built before the cancellation are kept, as with per-row appends.");
    }
}
