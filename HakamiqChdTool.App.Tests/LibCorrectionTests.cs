using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services.Library;
using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint bufferLength);

    private static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, Arguments = $"/c mklink /J \"{link}\" \"{target}\"" };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Junction fixture could not start.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertEqual(0, process.ExitCode, "Junction fixture failed: " + output);
    }

    private static void TestLibraryUiRerendersEveryStringOnLanguageSwitch(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        string root = AnalysisLibrary(workDirectory, "ui-lang", ("Alpha.bin", alpha), ("Guess.bin", [7, 7, 7]));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-lang-dats"), "l.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))),
            new DatGameSpec("Guess", null, new DatRomSpec("Guess.bin", 3, Sha1: new string('a', 40))));
        string output = Path.Combine(workDirectory, "ui-lang-out");

        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        Dictionary<string, string> current = english;
        LibraryAnalysisViewModel vm = new(
            RealRunner(),
            new FakePicker(),
            key => current.TryGetValue(key, out string? value) ? value : "MISSING:" + key,
            action => action())
        {
            LibraryRoot = root,
            OutputRoot = output,
            VerifyContent = true
        };
        vm.AddDatPath(dat);
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        vm.SelectedFilter = vm.Filters.Single(f => f.Status == DatMatchStatus.Mismatch);
        vm.SelectedRow = vm.Rows.Single(r => r.Source == "Guess.bin");
        AssertEqual("Mismatch", vm.SelectedRow.StatusText, "Precondition: English status text.");

        void AssertLanguage(Dictionary<string, string> s, string language)
        {
            AssertEqual(s["LocLib_Outcome_Completed"], vm.OutcomeText, language + ": outcome is re-rendered.");
            AssertEqual(string.Format(s["LocLib_Fmt_Summary"], 2, 1, 1), vm.SummaryText, language + ": summary is re-rendered.");
            AssertEqual(s["LocLib_Phase_Idle"], vm.PhaseText, language + ": phase is re-rendered.");
            AssertEqual(string.Format(s["LocLib_Fmt_FilterItem"], s["LocLib_Filter_All"], 2), vm.Filters[0].Label, language + ": filter label and count are re-rendered.");
            AssertEqual(string.Format(s["LocLib_Fmt_FilterItem"], s[LibraryAnalysisViewModel.StatusKey(DatMatchStatus.VerifiedMatch)], 1), vm.Filters.Single(f => f.Status == DatMatchStatus.VerifiedMatch).Label, language + ": per-status filter label is re-rendered.");
            AssertEqual(DatMatchStatus.Mismatch, vm.SelectedFilter?.Status, language + ": the chosen filter is kept.");
            LibraryResultRow row = vm.Rows.Single();
            AssertEqual(s[LibraryAnalysisViewModel.StatusKey(DatMatchStatus.Mismatch)], row.StatusText, language + ": row status is re-rendered.");
            AssertEqual("Guess.bin", vm.SelectedRow?.Source, language + ": the selected row is kept.");
            AssertEqual(s[LibraryAnalysisViewModel.StatusHintKey(DatMatchStatus.Mismatch)], vm.SelectedRow?.StatusHint, language + ": the details hint is re-rendered.");
            AssertEqual(s["LocLib_Bucket_Review"], vm.SelectedRow?.Bucket, language + ": the details bucket is re-rendered.");
        }

        current = arabic;
        vm.RefreshLanguage();
        AssertLanguage(arabic, "Arabic");
        AssertTrue(vm.OutcomeIsClean && vm.HasPreview, "A language switch keeps the clean-outcome state.");
        current = english;
        vm.RefreshLanguage();
        AssertLanguage(english, "English");

        // Validation messages follow the language too.
        vm.LibraryRoot = string.Empty;
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(english["LocLib_Err_NoLibrary"], vm.ErrorText, "Precondition: English validation message.");
        current = arabic;
        vm.RefreshLanguage();
        AssertEqual(arabic["LocLib_Err_NoLibrary"], vm.ErrorText, "The validation message is re-rendered.");
        current = english;
        vm.RefreshLanguage();
        AssertEqual(english["LocLib_Err_NoLibrary"], vm.ErrorText, "The validation message follows the switch back.");
    }

    private static void TestLibraryUiNeverShowsStaleResults(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        string root = AnalysisLibrary(workDirectory, "ui-stale", ("Alpha.bin", alpha));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-stale-dats"), "s.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))));
        string output = Path.Combine(workDirectory, "ui-stale-out");
        int calls = 0;
        LibraryAnalysisRunner real = RealRunner();
        LibraryAnalysisViewModel vm = NewLibraryVmFor(root, dat, output, (r, p, t) => { Interlocked.Increment(ref calls); return real(r, p, t); });
        vm.VerifyContent = true;

        void AssertNoResults(string context)
        {
            AssertEqual(0, vm.Rows.Count, context + ": no rows remain.");
            AssertFalse(vm.HasOutcome || vm.OutcomeIsClean, context + ": no outcome or success indicator remains.");
            AssertEqual(string.Empty, vm.OutcomeText, context + ": no outcome text remains.");
            AssertEqual(string.Empty, vm.SummaryText, context + ": no summary remains.");
            AssertEqual(string.Empty, vm.ReportText, context + ": no report remains.");
            AssertFalse(vm.HasPreview || vm.HasIssues, context + ": no preview or issues remain.");
            AssertTrue(vm.LastOutcome is null, context + ": no last outcome remains.");
            AssertTrue(vm.Filters.All(f => f.Label.EndsWith("(0)", StringComparison.Ordinal)), context + ": filter counts are reset.");
        }

        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertTrue(vm.OutcomeIsClean && vm.Rows.Count == 1, "Precondition: a completed run is shown.");

        // Changing an input invalidates the results that were produced from the old input.
        vm.LibraryRoot = Path.Combine(workDirectory, "ui-stale-missing");
        AssertNoResults("After the library changed");

        // A validation failure starts no run and shows the message instead of old results.
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(1, calls, "A validation failure never reaches the service.");
        AssertEqual("The library folder does not exist.", vm.ErrorText, "The validation message is shown.");
        AssertNoResults("After a validation failure");

        // Fixing the input clears the message; a new run replaces everything.
        vm.LibraryRoot = root;
        AssertEqual(string.Empty, vm.ErrorText, "Editing an input clears the old validation message.");
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertTrue(vm.OutcomeIsClean && vm.Rows.Count == 1 && vm.ErrorText.Length == 0, "A fresh run shows fresh results.");

        vm.VerifyContent = false;
        AssertNoResults("After the verify option changed");
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.DatPaths.Clear();
        AssertNoResults("After the DAT list changed");

        // Cancelled and partial states are real states and stay until the user changes something.
        LibraryAnalysisViewModel partial = NewLibraryVmFor(root, dat, output, (request, _, _) =>
        {
            DatMatchResult one = new("a.bin", DatMatchStatus.CandidateMatch, "d", "Sony - PlayStation", "A", "a.bin", null, null, [DatEvidence.NameExact], ["content-not-verified"], []);
            return Task.FromResult(new LibraryAnalysisResult(LibraryAnalysisOutcome.Cancelled, LibraryAnalysisPhase.Matching, null, [],
                new DatMatchReport(DatMatchOutcome.Cancelled, [one]), null, "partial", [], request.VerifyContent));
        });
        partial.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(LibraryAnalysisOutcome.Cancelled, partial.LastOutcome, "Cancellation is reported.");
        AssertTrue(partial.HasOutcome && !partial.OutcomeIsClean && partial.Rows.Count == 1, "Cancelled partial results stay visible, never as success.");
        partial.SelectedFilter = partial.Filters[0];
        AssertEqual(1, partial.Rows.Count, "Using the filter does not discard partial results.");
        partial.OutputRoot = Path.Combine(workDirectory, "ui-stale-out-2");
        AssertEqual(0, partial.Rows.Count, "Partial results are cleared once an input changes.");
    }

    private static void TestLibraryUiIgnoresLateCallbacksFromEarlierRuns(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        string root = AnalysisLibrary(workDirectory, "ui-late", ("Alpha.bin", alpha));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-late-dats"), "t.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))));
        string output = Path.Combine(workDirectory, "ui-late-out");

        LibraryAnalysisRunner real = RealRunner();
        IProgress<LibraryAnalysisProgress>? first = null;
        IProgress<LibraryAnalysisProgress>? second = null;
        using ManualResetEventSlim secondRunning = new();
        int call = 0;

        LibraryAnalysisRunner runner = async (request, progress, token) =>
        {
            if (Interlocked.Increment(ref call) == 1)
            {
                first = progress;
                return await real(request, progress, token);
            }

            second = progress;
            secondRunning.Set();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };

        LibraryAnalysisViewModel vm = NewLibraryVmFor(root, dat, output, runner);
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertTrue(first is not null && vm.OutcomeIsClean, "Precondition: the first run completed.");

        // Change an input so a new run is allowed to start, then start it and keep it running.
        vm.VerifyContent = true;
        Task secondRun = vm.StartCommand.ExecuteAsync(null);
        AssertTrue(secondRunning.Wait(TimeSpan.FromSeconds(30)) && vm.IsRunning, "The second run is active.");
        string indexing = "Indexing library files...";
        AssertEqual(indexing, vm.PhaseText, "Precondition: the second run shows its own phase.");

        first!.Report(new LibraryAnalysisProgress(LibraryAnalysisPhase.Matching, 9, 10));
        AssertEqual(indexing, vm.PhaseText, "A late callback from the first run does not change the phase.");
        AssertEqual(string.Empty, vm.ProgressText, "A late callback from the first run does not change the progress text.");
        AssertTrue(vm.ProgressValue == 0, "A late callback from the first run does not change the progress value.");

        second!.Report(new LibraryAnalysisProgress(LibraryAnalysisPhase.Matching, 3, 12));
        AssertEqual("3 / 12", vm.ProgressText, "The current run still reports progress.");

        vm.Cancel();
        AssertTrue(secondRun.Wait(TimeSpan.FromSeconds(30)), "Cancelling ends the second run.");
        AssertEqual(LibraryAnalysisOutcome.Cancelled, vm.LastOutcome, "The state is the second run's cancellation.");
        string idle = vm.PhaseText;
        string progressText = vm.ProgressText;

        second.Report(new LibraryAnalysisProgress(LibraryAnalysisPhase.Reporting, 1, 1));
        first.Report(new LibraryAnalysisProgress(LibraryAnalysisPhase.Reporting, 1, 1));
        AssertEqual(idle, vm.PhaseText, "A callback after the run ended does not change the final phase.");
        AssertEqual(progressText, vm.ProgressText, "A callback after the run ended does not change the final progress.");
        AssertFalse(vm.OutcomeIsClean, "A cancelled run is never turned into a success by a late callback.");
    }

    private static void TestPlannerRejectsOutputEqualToOrInsideLibrary(string workDirectory)
    {
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-out-library", ("g.bin", [1]));
        DatMatchReport report = RunMatch(new DatMatcher([]), lib.Root, lib.Entries, verify: false);
        Dictionary<string, string> before = SnapshotTree(lib.Root);
        string parent = Path.GetDirectoryName(lib.Root)!;

        string[] rejected =
        [
            lib.Root,
            lib.Root + Path.DirectorySeparatorChar,
            lib.Root.ToUpperInvariant(),
            Path.Combine(lib.Root, "out"),
            Path.Combine(lib.Root, "a", "b", "c"),
            lib.Root + Path.AltDirectorySeparatorChar + "out"
        ];

        foreach (string output in rejected)
        {
            OrganizationPlan plan = PlanFor(lib.Root, output, lib.Entries, report);
            AssertEqual(PlanStatus.Rejected, plan.Status, "Output equal to or inside the library must be rejected: '" + output + "'.");
            AssertEqual(0, plan.Items.Count, "A rejected plan has no items.");
            AssertTrue(plan.Issues.Any(i => i.Contains("library root", StringComparison.Ordinal)), "The rejection names the library root.");
        }

        // The library root may be written with a trailing separator or other casing too.
        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root.ToUpperInvariant() + Path.DirectorySeparatorChar, Path.Combine(lib.Root, "x"), lib.Entries, report).Status, "Library path spelling does not matter.");

        // Whole path components are compared, never a string prefix.
        string[] accepted =
        [
            lib.Root + "-sibling",
            lib.Root + "2",
            Path.Combine(parent, "plan-out-library-out"),
            Path.Combine(parent, "elsewhere", "plan-out-library")
        ];

        foreach (string output in accepted)
        {
            AssertEqual(PlanStatus.Ready, PlanFor(lib.Root, output, lib.Entries, report).Status, "A sibling that merely shares a name prefix is allowed: '" + output + "'.");
        }

        // Existing rules are unchanged: a volume root and a relative path are still refused.
        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, Path.GetPathRoot(workDirectory)!, lib.Entries, report).Status, "A volume root is still rejected.");
        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, "relative\\out", lib.Entries, report).Status, "A relative path is still rejected.");

        // An 8.3 alias of the library cannot hide the overlap (only checked when the volume has short names).
        StringBuilder shortName = new(1024);
        uint length = GetShortPathNameW(lib.Root, shortName, (uint)shortName.Capacity);

        if (length > 0 && length < shortName.Capacity && !string.Equals(shortName.ToString(), lib.Root, StringComparison.OrdinalIgnoreCase))
        {
            AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, Path.Combine(shortName.ToString(), "out"), lib.Entries, report).Status, "An 8.3 alias of the library is still the library.");
            AssertEqual(PlanStatus.Rejected, PlanFor(shortName.ToString(), Path.Combine(lib.Root, "out"), lib.Entries, report).Status, "An 8.3 alias of the library root is still the library.");
        }

        AssertFalse(Directory.Exists(Path.Combine(lib.Root, "out")) || Directory.Exists(lib.Root + "-sibling"), "The planner creates no folder.");
        AssertTreeUnchanged(before, lib.Root, "Planning leaves the library untouched.");
    }

    private static void TestPlannerRejectsOutputReachableThroughReparsePoints(string workDirectory)
    {
        (string Root, IReadOnlyList<LibraryIndexEntry> Entries) lib = BuildMatchLibrary(workDirectory, "plan-reparse-library", (Path.Combine("sub", "g.bin"), [1]));
        DatMatchReport report = RunMatch(new DatMatcher([]), lib.Root, lib.Entries, verify: false);
        Dictionary<string, string> before = SnapshotTree(lib.Root);

        // A junction elsewhere that points into the library: the real destination would be inside it.
        string intoLibrary = Path.Combine(workDirectory, "plan-reparse-into-library");
        CreateJunction(intoLibrary, Path.Combine(lib.Root, "sub"));
        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, intoLibrary, lib.Entries, report).Status, "A junction that points into the library is rejected as an output.");
        AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, Path.Combine(intoLibrary, "out"), lib.Entries, report).Status, "A path below a junction into the library is rejected.");

        // The library reached through a junction cannot be compared reliably, so the plan is refused.
        string viaJunction = Path.Combine(workDirectory, "plan-reparse-library-link");
        CreateJunction(viaJunction, lib.Root);
        OrganizationPlan viaLink = PlanFor(viaJunction, Path.Combine(lib.Root, "out"), lib.Entries, report);
        AssertEqual(PlanStatus.Rejected, viaLink.Status, "A library root behind a junction is refused.");
        AssertEqual(PlanStatus.Rejected, PlanFor(viaJunction, Path.Combine(workDirectory, "plan-reparse-fine-out"), lib.Entries, report).Status, "A library root behind a junction is refused even with a distant output.");

        // A symbolic link needs a privilege; without it the junction cases above are the evidence.
        string symlink = Path.Combine(workDirectory, "plan-reparse-symlink");

        try
        {
            Directory.CreateSymbolicLink(symlink, lib.Root);
            AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, symlink, lib.Entries, report).Status, "A directory symlink that points at the library is rejected as an output.");
            AssertEqual(PlanStatus.Rejected, PlanFor(lib.Root, Path.Combine(symlink, "out"), lib.Entries, report).Status, "A path below a directory symlink into the library is rejected.");
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070522) || ex.Message.Contains("required privilege", StringComparison.OrdinalIgnoreCase))
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        AssertTreeUnchanged(before, lib.Root, "Planning leaves the library untouched.");
    }
}
