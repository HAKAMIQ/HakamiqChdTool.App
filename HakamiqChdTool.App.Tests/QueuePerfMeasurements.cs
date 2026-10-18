using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Diagnostics;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

// Opt-in (--perf-mode queue|all): sizes the row store and the virtualized collection without a window.
internal static partial class Program
{
    private static void MeasureQueueStore(int rows, int removals)
    {
        var store = new QueueRowStore();
        var view = new VirtualizedQueueCollection(
            store,
            new QueueViewportService(store, row => new TaskQueueItemViewModel(row), (_, _) => { }),
            static _ => true);

        var ids = new Guid[rows];
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < rows; i++)
        {
            ids[i] = Guid.NewGuid();
            store.Append(new QueueRowData { ItemId = ids[i], OriginalPath = "C:/x/" + i + ".iso" });
        }

        long appendMs = sw.ElapsedMilliseconds;

        var batchStore = new QueueRowStore();
        var batchView = new VirtualizedQueueCollection(
            batchStore,
            new QueueViewportService(batchStore, row => new TaskQueueItemViewModel(row), (_, _) => { }),
            static _ => true);
        var batch = new QueueRowData[rows];
        for (int i = 0; i < rows; i++)
        {
            batch[i] = new QueueRowData { ItemId = Guid.NewGuid(), OriginalPath = "C:/x/" + i + ".iso" };
        }

        sw.Restart();
        batchStore.AppendRange(batch);
        Console.WriteLine($"[PERF] queue rows={rows:N0}: AppendRange {sw.ElapsedMilliseconds} ms (view count {batchView.Count:N0})");
        batchView.Dispose();
        sw.Restart();
        GC.Collect();
        long memBefore = GC.GetTotalMemory(true);

        sw.Restart();
        for (int i = 0; i < removals; i++)
        {
            store.TryRemove(ids[i]);
        }

        long removeMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"[PERF] queue rows={rows:N0}: append one-by-one {appendMs} ms (view count {view.Count:N0}); remove {removals:N0} one-by-one {removeMs} ms; heap {memBefore / 1024 / 1024} MiB");
        view.Dispose();
    }
}
