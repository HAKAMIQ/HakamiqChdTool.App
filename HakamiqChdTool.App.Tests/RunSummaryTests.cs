using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static SessionRunMetrics Metrics(int completed, int failed = 0, int skipped = 0, int cancelled = 0, long savedBytes = 0) =>
        new(completed + failed + skipped + cancelled, completed, failed, skipped, cancelled, 0, 0, 0, 0, savedBytes, 0, 0, 0, 0, 0,
            Array.Empty<ConversionPerformanceReport>(), Array.Empty<SessionRunFailedItem>());

    // The run summary shows the outcome, non-zero counts, space saved and time, in both languages.
    private static void TestRunSummaryCardContent()
    {
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        string En(string key) => english[key];
        static string Plain(string value) => value.Replace("‎", string.Empty, StringComparison.Ordinal);

        RunSummaryCard done = RunSummaryCardBuilder.Build(Metrics(12, savedBytes: 4_521_000_000), false, TimeSpan.FromSeconds(1085), En);
        AssertEqual(RunSummaryOutcome.Success, done.Outcome, "A run without failures is a success.");
        AssertEqual("Finished", done.Title, "The success title is shown.");
        AssertEqual("12 done · 4.21 GB saved · took 18:05", Plain(done.Message), "Zero counts are left out; saved space and time are shown.");

        RunSummaryCard problems = RunSummaryCardBuilder.Build(Metrics(3, failed: 1, skipped: 2), false, TimeSpan.FromSeconds(3725), En);
        AssertEqual(RunSummaryOutcome.WithProblems, problems.Outcome, "A failure marks the run as having problems.");
        AssertEqual("3 done · 1 failed · 2 skipped · took 1:02:05", Plain(problems.Message), "Failed and skipped counts are shown; no saving means no saved part.");

        RunSummaryCard stopped = RunSummaryCardBuilder.Build(Metrics(0, cancelled: 4), true, TimeSpan.Zero, En);
        AssertEqual(RunSummaryOutcome.Stopped, stopped.Outcome, "A cancelled run is stopped.");
        AssertEqual("0 done · 4 cancelled", Plain(stopped.Message), "Done is always shown and an empty time is left out.");

        RunSummaryCard arabicCard = RunSummaryCardBuilder.Build(Metrics(5, savedBytes: 300 * 1024 * 1024), false, TimeSpan.FromSeconds(42), key => arabic[key]);
        AssertEqual("اكتملت المعالجة", arabicCard.Title, "The Arabic title is used.");
        AssertEqual("5 ناجح · وفرت 300.0 MB · استغرقت 0:42", Plain(arabicCard.Message), "The Arabic line is built from Arabic text.");
        AssertTrue(arabicCard.Message.Contains("‎300.0 MB‎", StringComparison.Ordinal), "Sizes are kept left-to-right inside Arabic text.");

        foreach (string key in new[]
        {
            "LocRunSummary_TitleDone", "LocRunSummary_TitleWithProblems", "LocRunSummary_TitleStopped", "LocRunSummary_Done",
            "LocRunSummary_Failed", "LocRunSummary_Skipped", "LocRunSummary_Cancelled", "LocRunSummary_Saved", "LocRunSummary_Time",
            "LocRunSummary_Separator", "LocRunSummary_Details", "LocRunSummary_Close"
        })
        {
            AssertTrue(english.ContainsKey(key) && arabic.ContainsKey(key), $"{key} exists in both languages.");
            if (key is not ("LocRunSummary_Separator" or "LocRunSummary_TitleDone" or "LocRunSummary_TitleWithProblems" or "LocRunSummary_TitleStopped" or "LocRunSummary_Details" or "LocRunSummary_Close"))
            {
                AssertTrue(english[key].Contains("{0}", StringComparison.Ordinal) && arabic[key].Contains("{0}", StringComparison.Ordinal), $"{key} has a value slot in both languages.");
            }
        }

        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Main", "QueueView.xaml")));
        AssertTrue(view.Contains("x:Name=\"RunSummaryBar\"", StringComparison.Ordinal), "The queue view has the summary bar.");
        AssertTrue(view.Contains("AutomationProperties.LiveSetting=\"Polite\"", StringComparison.Ordinal), "Screen readers announce the summary.");
        AssertTrue(view.Contains("AutomationProperties.Name=\"{DynamicResource LocRunSummary_Details}\"", StringComparison.Ordinal)
            && view.Contains("AutomationProperties.Name=\"{DynamicResource LocRunSummary_Close}\"", StringComparison.Ordinal), "The bar's buttons have accessible names.");
    }
}
