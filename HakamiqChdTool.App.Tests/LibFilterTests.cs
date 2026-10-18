using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services.Library;
using HakamiqChdTool.App.ViewModels;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Applying a result used to rebuild the visible rows twice (once through the filter change,
    // once explicitly), doubling the collection notifications a bound grid must process.
    private static void TestLibraryResultAppliesFilterOnce(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-filter-once");
        string library = Path.Combine(root, "lib");
        Directory.CreateDirectory(library);
        string dat = Path.Combine(root, "x.dat");
        File.WriteAllText(dat, "<datafile/>");

        const int rowCount = 200;
        List<PlanItem> items = [.. Enumerable.Range(0, rowCount).Select(i => new PlanItem(
            $"Game {i}.chd",
            $"Platform/Game {i}.chd",
            PlanBucket.Platform,
            DatMatchStatus.VerifiedMatch,
            "Sony - PlayStation",
            $"Game {i}",
            null,
            PlanFlags.None,
            []))];

        var plan = new OrganizationPlan(PlanStatus.Ready, Path.Combine(root, "out"), items, []);
        var result = new LibraryAnalysisResult(
            LibraryAnalysisOutcome.Completed, LibraryAnalysisPhase.Finished, null, [], null, plan, "report", [], false);

        LibraryAnalysisViewModel vm = NewLibraryVmFor(library, dat, Path.Combine(root, "out"), (_, _, _) => Task.FromResult(result));

        int events = 0;
        vm.Rows.CollectionChanged += (_, _) => events++;
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        AssertEqual(rowCount, vm.Rows.Count, "Every row is shown under the All filter.");
        AssertTrue(events >= rowCount, "Each row is added.");
        AssertTrue(events <= rowCount + 6, $"Rows are rebuilt once, not twice ({events} notifications for {rowCount} rows).");

        // The filter still narrows and restores the rows.
        vm.SelectedFilter = vm.Filters.First(f => f.Status == DatMatchStatus.NotFound);
        AssertEqual(0, vm.Rows.Count, "A filter with no matching status shows no rows.");
        vm.SelectedFilter = vm.Filters[0];
        AssertEqual(rowCount, vm.Rows.Count, "The All filter restores every row.");
    }
}
