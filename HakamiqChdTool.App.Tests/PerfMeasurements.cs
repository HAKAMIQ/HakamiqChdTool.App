using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Library;
using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

// Opt-in measurements (--perf-mode all|library|redump). They report numbers and assert nothing:
// they are a repeatable way to size a suspected bottleneck, not a regression gate.
internal static partial class Program
{
    private static int RunPerfMeasurements(string mode, AppReflection app)
    {
        string root = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Perf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            if (mode is "all" or "library")
            {
                foreach (int rows in new[] { 10_000, 100_000 })
                {
                    RunOnSta(() => MeasureLibraryFilterAsync(root, rows, withGrid: false));
                    RunOnSta(() => MeasureLibraryFilterAsync(root, rows, withGrid: true));
                }
            }

            if (mode is "all" or "queue")
            {
                RunOnSta(() => { MeasureQueueStore(10_000, 5_000); MeasureQueueStore(100_000, 5_000); MeasureQueueStore(100_000, 50_000); return Task.CompletedTask; });
            }

            if (mode is "all" or "redump")
            {
                MeasureRedumpLookups(app, root);
            }

            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void RunOnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                }
            });
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("Perf measurement failed.", failure);
        }
    }

    private static async Task MeasureLibraryFilterAsync(string root, int rowCount, bool withGrid)
    {
        DatMatchStatus[] statuses = Enum.GetValues<DatMatchStatus>();
        var items = new List<PlanItem>(rowCount);

        for (int i = 0; i < rowCount; i++)
        {
            items.Add(new PlanItem(
                $"Library\\Sub{i % 50}\\Game {i} (Europe).chd",
                $"Platform\\Game {i} (Europe)\\Game {i} (Europe).chd",
                PlanBucket.Platform,
                statuses[i % statuses.Length],
                "Sony - PlayStation",
                $"Game {i}",
                null,
                PlanFlags.None,
                ["catalog name"],
                Region: "Europe"));
        }

        var plan = new OrganizationPlan(PlanStatus.Ready, Path.Combine(root, "out"), items, []);
        var result = new LibraryAnalysisResult(
            LibraryAnalysisOutcome.Completed, LibraryAnalysisPhase.Finished, null, [], null, plan, "report", [], false);

        string library = Path.Combine(root, "lib");
        Directory.CreateDirectory(library);
        string dat = Path.Combine(root, "x.dat");
        File.WriteAllText(dat, "<datafile/>");

        LibraryAnalysisViewModel vm = NewLibraryVmFor(library, dat, Path.Combine(root, "out"), (_, _, _) => Task.FromResult(result));

        System.Windows.Window? window = null;

        if (withGrid)
        {
            var grid = new System.Windows.Controls.DataGrid
            {
                ItemsSource = vm.Rows,
                AutoGenerateColumns = false,
                IsReadOnly = true
            };
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Source",
                Binding = new System.Windows.Data.Binding(nameof(LibraryResultRow.Source))
            });
            window = new System.Windows.Window
            {
                Content = grid,
                ShowInTaskbar = false,
                WindowStyle = System.Windows.WindowStyle.None,
                Left = -32000,
                Top = -32000,
                Width = 1000,
                Height = 600
            };
            window.Show();
        }

        int collectionEvents = 0;
        vm.Rows.CollectionChanged += (_, _) => collectionEvents++;

        void Settle() => window?.UpdateLayout();

        long memBefore = GC.GetTotalMemory(forceFullCollection: true);
        var applyWatch = Stopwatch.StartNew();
        await vm.StartCommand.ExecuteAsync(null).ConfigureAwait(true);
        Settle();
        applyWatch.Stop();
        long memAfter = GC.GetTotalMemory(forceFullCollection: true);
        int applyEvents = collectionEvents;
        int shown = vm.Rows.Count;

        // Switching the status filter is the interactive path (ApplyFilter only).
        collectionEvents = 0;
        var filterWatch = Stopwatch.StartNew();
        vm.SelectedFilter = vm.Filters[1];
        Settle();
        filterWatch.Stop();
        int filterEvents = collectionEvents;
        int filtered = vm.Rows.Count;

        collectionEvents = 0;
        var allWatch = Stopwatch.StartNew();
        vm.SelectedFilter = vm.Filters[0];
        Settle();
        allWatch.Stop();

        Console.WriteLine(
            $"[PERF] library rows={rowCount,7} grid={withGrid,-5} apply={applyWatch.ElapsedMilliseconds,6} ms ({applyEvents} CollectionChanged, {shown} rows shown, heap +{(memAfter - memBefore) / (1024 * 1024)} MB) "
            + $"filter->status={filterWatch.ElapsedMilliseconds,6} ms ({filterEvents} events, {filtered} rows) filter->all={allWatch.ElapsedMilliseconds,6} ms ({collectionEvents} events)");

        window?.Close();
    }

    private static void MeasureRedumpLookups(AppReflection app, string root)
    {
        const int romCount = 100_000;
        var xml = new StringBuilder();
        xml.Append("<datafile><header><name>Sony - PlayStation - Datfile</name></header>");

        for (int i = 0; i < romCount; i++)
        {
            string h = i.ToString("x8", CultureInfo.InvariantCulture);
            xml.Append($"<game name=\"Game {i}\"><rom name=\"Game {i}.bin\" size=\"{1000 + i}\" crc=\"{h}\" md5=\"{h.PadLeft(32, '0')}\" sha1=\"{h.PadLeft(40, '0')}\" /></game>");
        }

        xml.Append("</datafile>");
        string dat = Path.Combine(root, "big.dat");
        File.WriteAllText(dat, xml.ToString());

        object databaseObject = app.CreateRedumpDatabase(Path.Combine(root, "perf.db"));
        var importWatch = Stopwatch.StartNew();
        AssertTrue(GetBool(app.CleanRebuildRedumpDatabase(databaseObject, [dat]), "Success"), "Perf DAT import failed.");
        importWatch.Stop();
        var database = (RedumpSqliteManager)databaseObject;

        const int lookups = 2000;
        var random = new Random(7);

        // Hits through the single-file path: one connection per call.
        var hitWatch = Stopwatch.StartNew();
        for (int i = 0; i < lookups; i++)
        {
            int n = random.Next(romCount);
            string h = n.ToString("x8", CultureInfo.InvariantCulture);
            if (!database.TryMatchHash(h.PadLeft(32, '0'), h.PadLeft(40, '0'), h, 1000 + n, out _))
            {
                throw new InvalidOperationException("Expected a hit.");
            }
        }

        hitWatch.Stop();

        var missWatch = Stopwatch.StartNew();
        for (int i = 0; i < lookups; i++)
        {
            _ = database.TryMatchHash(new string('a', 32), new string('b', 40), "ffffffff", 5, out _);
        }

        missWatch.Stop();

        // Batch API: one connection serves every file of a disc.
        var queries = new List<RedumpHashQuery>();
        for (int i = 0; i < 40; i++)
        {
            int n = random.Next(romCount);
            string h = n.ToString("x8", CultureInfo.InvariantCulture);
            queries.Add(new RedumpHashQuery(h.PadLeft(32, '0'), h.PadLeft(40, '0'), h, 1000 + n));
        }

        const int batches = 100;
        var oneWatch = Stopwatch.StartNew();
        for (int i = 0; i < batches; i++)
        {
            _ = database.FindCommonDiscs(queries.Take(1).ToList());
        }

        oneWatch.Stop();

        var fortyWatch = Stopwatch.StartNew();
        for (int i = 0; i < batches; i++)
        {
            _ = database.FindCommonDiscs(queries);
        }

        fortyWatch.Stop();

        Console.WriteLine(
            $"[PERF] redump rows={romCount} import={importWatch.ElapsedMilliseconds} ms | TryMatchHash hit {hitWatch.Elapsed.TotalMilliseconds * 1000 / lookups:F0} us/call "
            + $"({lookups * 1000.0 / Math.Max(1, hitWatch.ElapsedMilliseconds):F0}/s), miss {missWatch.Elapsed.TotalMilliseconds * 1000 / lookups:F0} us/call | "
            + $"FindCommonDiscs 1 file {oneWatch.Elapsed.TotalMilliseconds * 1000 / batches:F0} us/call, 40 files {fortyWatch.Elapsed.TotalMilliseconds * 1000 / batches:F0} us/call");
    }
}
