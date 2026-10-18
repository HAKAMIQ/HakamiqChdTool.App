using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Library;
using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed class FakePicker : IOptionsPickerService
    {
        public string? Folder { get; set; }

        public string? File { get; set; }

        public string? PickFolder(string titleKey, string? selectedPath) => Folder;

        public string? PickFile(string titleKey, string filterKey, string? currentPath, string fallbackDirectory) => File;
    }

    private static string FindRepoFile(string relative)
    {
        string? dir = AppContext.BaseDirectory;

        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir, "HakamiqChdTool.App.sln")))
            {
                return Path.Combine(dir, relative);
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new TestSkippedException("Repository root not found from the test output folder.");
    }

    private static Dictionary<string, string> LoadStrings(string fileName) =>
        XDocument.Load(FindRepoFile(Path.Combine("Resources", fileName)))
            .Descendants()
            .Where(e => e.Name.LocalName == "String")
            .ToDictionary(
                e => (string)e.Attributes().First(a => a.Name.LocalName == "Key"),
                e => e.Value);

    private static LibraryAnalysisViewModel NewLibraryVm(LibraryAnalysisRunner runner, FakePicker? picker = null,
        LibraryOperationJournal? journal = null, Func<string, bool>? confirm = null)
    {
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        return new LibraryAnalysisViewModel(
            runner,
            picker ?? new FakePicker(),
            key => english.TryGetValue(key, out string? value) ? value : "MISSING:" + key,
            action => action(), journal, confirm);
    }

    private static LibraryAnalysisViewModel NewLibraryVmFor(string root, string dat, string output, LibraryAnalysisRunner runner)
    {
        LibraryAnalysisViewModel vm = NewLibraryVm(runner);
        vm.LibraryRoot = root;
        vm.OutputRoot = output;
        vm.AddDatPath(dat);
        return vm;
    }

    private static LibraryAnalysisRunner RealRunner(DatContentHasher? hasher = null)
    {
        LibraryAnalysisService service = new(new LibraryIndexer(), hasher ?? DatHashing.Hasher);
        return service.AnalyzeAsync;
    }

    private static void TestLibraryWindowFitsWorkArea()
    {
        // The window must not pre-clamp against the primary screen: that would overwrite the
        // designed limits WindowBackdrop restores on a larger monitor.
        string code = System.IO.File.ReadAllText(FindRepoFile(Path.Combine("Views", "LibraryAnalysisWindow.xaml.cs")));
        AssertFalse(code.Contains("SystemParameters.WorkArea", StringComparison.Ordinal), "The window does not clamp to the primary work area.");
        AssertTrue(code.Contains("WindowBackdrop.ApplyDialog", StringComparison.Ordinal), "The window uses the per-monitor clamp.");

        // A work area smaller than the content must scroll, not clip buttons.
        string xaml = System.IO.File.ReadAllText(FindRepoFile(Path.Combine("Views", "LibraryAnalysisWindow.xaml")));
        AssertTrue(xaml.Contains("x:Name=\"BodyScroll\"", StringComparison.Ordinal), "The body is scrollable.");
        AssertTrue(xaml.Contains("Library.Body.MinHeight", StringComparison.Ordinal), "The body keeps a minimum height under the scroller.");

        // A Margin on the grid sits outside its Height (bound to ViewportHeight), so the content would
        // always be taller than the viewport and the scrollbar permanently visible. The inset must be
        // the scroller's Padding.
        int scrollStart = xaml.IndexOf("x:Name=\"BodyScroll\"", StringComparison.Ordinal);
        int gridStart = xaml.IndexOf("<Grid ", scrollStart, StringComparison.Ordinal);
        int gridEnd = xaml.IndexOf('>', gridStart);
        string scrollTag = xaml[scrollStart..gridStart];
        string gridTag = xaml[gridStart..gridEnd];
        AssertTrue(scrollTag.Contains("Padding=", StringComparison.Ordinal), "The body inset is the scroller's Padding.");
        AssertFalse(gridTag.Contains("Margin=", StringComparison.Ordinal), "The viewport-height grid has no Margin that would overflow the viewport.");

        // Real window with the designed limits: shrinks on a small work area, recovers on a large one.
        Exception? failure = null;
        double smallMinHeight = 0, smallMinWidth = 0, largeMinHeight = 0, largeMinWidth = 0;
        Thread thread = new(() =>
        {
            try
            {
                var window = new System.Windows.Window
                {
                    MinWidth = 900,
                    MinHeight = 700,
                    Width = 1040,
                    Height = 780,
                    ShowInTaskbar = false,
                    WindowStyle = System.Windows.WindowStyle.None,
                    Left = -32000,
                    Top = -32000
                };
                HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(window);
                window.Show();
                HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ConstrainToWorkArea(window, new System.Windows.Rect(0, 0, 800, 600));
                smallMinHeight = window.MinHeight;
                smallMinWidth = window.MinWidth;
                HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ConstrainToWorkArea(window, new System.Windows.Rect(0, 0, 1920, 1040));
                largeMinHeight = window.MinHeight;
                largeMinWidth = window.MinWidth;
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(30)), "The window test finished.");
        AssertTrue(failure is null, "The window test threw: " + failure);
        AssertEqual(600d, smallMinHeight, "MinHeight shrinks to a small work area.");
        AssertEqual(800d, smallMinWidth, "MinWidth shrinks to a small work area.");
        AssertEqual(700d, largeMinHeight, "MinHeight recovers on a larger monitor.");
        AssertEqual(900d, largeMinWidth, "MinWidth recovers on a larger monitor.");
    }

    private static void TestLibraryUiBuildsRequestAndRejectsInvalidInput(string workDirectory)
    {
        string root = AnalysisLibrary(workDirectory, "ui-request", ("a.bin", [1]));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-request-dats"), "a.dat", "Sony - PlayStation",
            new DatGameSpec("A", null, new DatRomSpec("a.bin", 1)));
        string output = Path.Combine(workDirectory, "ui-request-out");
        LibraryAnalysisViewModel vm = NewLibraryVm(RealRunner());

        AssertTrue(!vm.TryBuildRequest(out _, out string key) && key == "LocLib_Err_NoLibrary", "Empty library is rejected.");
        vm.LibraryRoot = Path.Combine(workDirectory, "ui-request-missing");
        AssertTrue(!vm.TryBuildRequest(out _, out key) && key == "LocLib_Err_LibraryMissing", "Missing library is rejected.");
        vm.LibraryRoot = "  " + root + "  ";
        AssertTrue(!vm.TryBuildRequest(out _, out key) && key == "LocLib_Err_NoDat", "A DAT is required.");
        vm.AddDatPath(Path.Combine(workDirectory, "ui-request-dats", "missing.dat"));
        AssertTrue(!vm.TryBuildRequest(out _, out key) && key == "LocLib_Err_DatMissing", "A missing DAT is rejected.");
        vm.DatPaths.Clear();
        vm.AddDatPath(dat);
        vm.AddDatPath(dat.ToUpperInvariant());
        AssertEqual(1, vm.DatPaths.Count, "The same DAT is not listed twice.");
        AssertTrue(!vm.TryBuildRequest(out _, out key) && key == "LocLib_Err_NoOutput", "An output folder is required.");
        vm.OutputRoot = "relative\\out";
        AssertTrue(!vm.TryBuildRequest(out _, out key) && key == "LocLib_Err_OutputNotAbsolute", "A relative output folder is rejected.");

        vm.OutputRoot = " " + output + " ";
        AssertTrue(vm.TryBuildRequest(out LibraryAnalysisRequest? request, out _) && request is not null, "Valid input builds a request.");
        AssertEqual(root, request!.LibraryRoot, "Library path is trimmed.");
        AssertEqual(output, request.OutputRoot, "Output path is trimmed.");
        AssertEqual(1, request.DatPaths.Count, "DAT list is passed through.");
        AssertFalse(request.VerifyContent, "Content hashing is off by default.");
        vm.VerifyContent = true;
        vm.TryBuildRequest(out request, out _);
        AssertTrue(request!.VerifyContent, "Hashing is on only after the user opts in.");

        int calls = 0;
        LibraryAnalysisViewModel invalid = NewLibraryVm((_, _, _) => { calls++; throw new InvalidOperationException(); });
        invalid.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(0, calls, "Invalid input never reaches the service.");
        AssertEqual("Select the game library folder.", invalid.ErrorText, "The validation message is localized text.");
        AssertFalse(invalid.HasOutcome, "A validation error is not an analysis outcome.");

        FakePicker picker = new() { Folder = root, File = dat };
        LibraryAnalysisViewModel picked = NewLibraryVm(RealRunner(), picker);
        picked.BrowseLibraryCommand.Execute(null);
        picked.AddDatCommand.Execute(null);
        picker.Folder = output;
        picked.BrowseOutputCommand.Execute(null);
        AssertTrue(picked.LibraryRoot == root && picked.OutputRoot == output && picked.DatPaths.Single() == dat, "Pickers fill the inputs.");
    }

    private static void TestLibraryUiRunsAnalysisAndInterpretsStatuses(string workDirectory)
    {
        byte[] alpha = [1, 2, 3, 4];
        byte[] twin = [6, 6];
        string root = AnalysisLibrary(workDirectory, "ui-run",
            ("Alpha.bin", alpha), ("Guess.bin", [7, 7, 7]), ("Shared.bin", twin), ("misc\\a.zip", [80, 75, 3, 4]), ("unknown.rom", [9]));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-run-dats"), "r.dat", "Sony - PlayStation",
            new DatGameSpec("Alpha", null, new DatRomSpec("Alpha.bin", 4, Sha1: Sha1Of(alpha))),
            new DatGameSpec("Guess", null, new DatRomSpec("Guess.bin", 3, Sha1: new string('a', 40))),
            new DatGameSpec("Twin A", null, new DatRomSpec("Shared.bin", 2, Sha1: Sha1Of(twin))),
            new DatGameSpec("Twin B", null, new DatRomSpec("Shared.bin", 2, Sha1: Sha1Of(twin))));
        string output = Path.Combine(workDirectory, "ui-run-out");
        Dictionary<string, string> before = SnapshotTree(root);

        // Without opt-in nothing may be hashed, so nothing may be Verified.
        int hashed = 0;
        DatContentHasher counting = (p, k, t) => { Interlocked.Increment(ref hashed); return DatHashing.Hasher(p, k, t); };
        LibraryAnalysisViewModel light = NewLibraryVmFor(root, dat, output, RealRunner(counting));
        light.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(0, hashed, "The UI default never hashes file contents.");
        AssertFalse(light.Rows.Any(r => r.Status == DatMatchStatus.VerifiedMatch), "Nothing is Verified without hashing.");
        AssertEqual("Candidate (not verified)", light.Rows.Single(r => r.Source == "Alpha.bin").StatusText, "Name and size only is shown as a candidate.");

        LibraryAnalysisViewModel vm = NewLibraryVmFor(root, dat, output, RealRunner());
        vm.VerifyContent = true;
        List<string> phases = [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryAnalysisViewModel.PhaseText) && vm.IsRunning)
            {
                phases.Add(vm.PhaseText);
            }
        };
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        AssertFalse(vm.IsRunning, "The run has ended.");
        AssertEqual(LibraryAnalysisOutcome.Completed, vm.LastOutcome, "Clean run completes.");
        AssertTrue(vm.OutcomeIsClean && vm.HasPreview, "Only a completed run is shown as clean, with a preview.");
        AssertEqual(5, vm.Rows.Count, "Every indexed file has a row.");
        AssertEqual("Verified", vm.Rows.Single(r => r.Source == "Alpha.bin").StatusText, "A SHA1 match is Verified.");
        AssertEqual("Mismatch", vm.Rows.Single(r => r.Source == "Guess.bin").StatusText, "Same name but a different hash is a Mismatch, never Verified.");
        AssertEqual(DatMatchStatus.Ambiguous, vm.Rows.Single(r => r.Source == "Shared.bin").Status, "Two games sharing a record are Ambiguous.");
        AssertEqual(DatMatchStatus.Unsupported, vm.Rows.Single(r => r.Source == Path.Combine("misc", "a.zip")).Status, "An unmatched archive is Unsupported.");
        AssertEqual(DatMatchStatus.NotFound, vm.Rows.Single(r => r.Source == "unknown.rom").Status, "Unknown stays Not found.");
        LibraryResultRow alphaRow = vm.Rows.Single(r => r.Source == "Alpha.bin");
        AssertEqual(Path.Combine("Sony - PlayStation", "Alpha.bin"), alphaRow.Destination, "The proposed destination is shown.");

        AssertTrue(vm.Filters.Count == 8 && vm.Filters[0].Label == "All results (5)", "Filter list has an All entry with counts.");
        vm.SelectedFilter = vm.Filters.Single(f => f.Status == DatMatchStatus.Ambiguous);
        AssertEqual(1, vm.Rows.Count, "Filtering shows one status.");
        vm.SelectedFilter = vm.Filters[0];
        AssertEqual(5, vm.Rows.Count, "Clearing the filter restores all rows.");

        AssertTrue(phases.Contains("Indexing library files...") && phases.Contains("Matching files against DATs..."), "Phase text is reported while running.");
        AssertTrue(vm.ReportText.Contains("no files were changed"), "The preview report is available.");

        AssertFalse(Directory.Exists(output), "Preview creates no output folder.");
        AssertTreeUnchanged(before, root, "The UI run leaves every library file and timestamp untouched.");
    }

    private static void TestLibraryUiOrganizerExecuteJournalUndo(string workDirectory)
    {
        string root = AnalysisLibrary(workDirectory, "ui-organizer-e2e", ("Game.iso", [3, 1, 4, 1, 5]));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-organizer-dats"), "game.dat", "Sony - PlayStation",
            new DatGameSpec("Synthetic Game", "World", new DatRomSpec("Game.iso", 5, Sha1: Sha1Of([3, 1, 4, 1, 5]))));
        string output = Path.Combine(workDirectory, "ui-organizer-output");
        string journalPath = Path.Combine(workDirectory, "ui-organizer-journal", "operations.jsonl");
        LibraryOperationJournal journal = new(journalPath);
        LibraryAnalysisViewModel vm = NewLibraryVm(RealRunner(), journal: journal, confirm: _ => true);
        vm.LibraryRoot = root;
        vm.OutputRoot = output;
        vm.AddDatPath(dat);
        vm.VerifyContent = true;

        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertTrue(vm.CanExecutePlan, "A completed content-verified preview enables Execute.");
        vm.ExecuteCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        string destination = Path.Combine(output, "Sony - PlayStation", "Game.iso");
        AssertTrue(File.Exists(destination) && !File.Exists(Path.Combine(root, "Game.iso")), "The Library Execute command moves the eligible item.");
        AssertTrue(File.Exists(journalPath), "The Execute command records its operation journal.");

        vm.JournalCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertTrue(vm.ReportText.Contains("Completed", StringComparison.Ordinal), "The Journal command displays recorded operation states.");
        vm.UndoCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        string source = Path.Combine(root, "Game.iso");
        AssertTrue(File.Exists(source) && !File.Exists(destination), "The Library Undo command restores the source tree.");
        AssertEqual(Convert.ToHexString(SHA256.HashData([3, 1, 4, 1, 5])),
            Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(source))), "The restored source bytes match the test fixture.");
    }

    private static void TestLibraryUiShowsProgressAndPartialResults(string workDirectory)
    {
        string root = AnalysisLibrary(workDirectory, "ui-partial", ("a.bin", [1]));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-partial-dats"), "p.dat", "Sony - PlayStation",
            new DatGameSpec("A", null, new DatRomSpec("a.bin", 1)));
        string output = Path.Combine(workDirectory, "ui-partial-out");
        List<(string Phase, double Value, bool Indeterminate, string Text)> seen = [];
        LibraryAnalysisViewModel? vmRef = null;

        LibraryAnalysisRunner runner = (request, progress, token) =>
        {
            progress.Report(new LibraryAnalysisProgress(LibraryAnalysisPhase.Indexing, 0, 0));
            progress.Report(new LibraryAnalysisProgress(LibraryAnalysisPhase.Matching, 3, 12));
            seen.Add((vmRef!.PhaseText, vmRef.ProgressValue, vmRef.ProgressIsIndeterminate, vmRef.ProgressText));
            DatMatchResult one = new("a.bin", DatMatchStatus.CandidateMatch, "d", "Sony - PlayStation", "A", "a.bin", null, null, [DatEvidence.NameExact], ["content-not-verified"], []);
            return Task.FromResult(new LibraryAnalysisResult(
                LibraryAnalysisOutcome.Failed,
                LibraryAnalysisPhase.Matching,
                null,
                [],
                new DatMatchReport(DatMatchOutcome.Cancelled, [one]),
                null,
                "partial",
                [new LibraryAnalysisIssue(LibraryAnalysisPhase.Matching, "phase-failed", "disk error")],
                request.VerifyContent));
        };
        LibraryAnalysisViewModel vm = NewLibraryVmFor(root, dat, output, runner);
        vmRef = vm;
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        AssertEqual("Matching files against DATs...", seen[0].Phase, "Progress phase is shown.");
        AssertTrue(Math.Abs(seen[0].Value - 25.0) < 0.001 && !seen[0].Indeterminate && seen[0].Text == "3 / 12", "Progress shows completed work, not a guess.");

        AssertFalse(vm.OutcomeIsClean, "A failed run is never shown as successful.");
        AssertEqual("Analysis failed. Any results shown are partial.", vm.OutcomeText, "Failure is announced.");
        AssertFalse(vm.HasPreview, "No preview exists without a plan.");
        AssertEqual(1, vm.Rows.Count, "Partial match results are still listed.");
        AssertEqual("", vm.Rows[0].Destination, "A partial row has no proposed destination.");
        AssertTrue(vm.HasIssues && vm.Issues[0].Contains("disk error"), "Service issues are listed.");

        // A runner that throws must not leave the window stuck in the running state.
        LibraryAnalysisViewModel crashing = NewLibraryVmFor(root, dat, output, (_, _, _) => throw new IOException("boom"));
        crashing.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertFalse(crashing.IsRunning, "An unexpected exception ends the run.");
        AssertTrue(!crashing.OutcomeIsClean && crashing.OutcomeText.Contains("stopped unexpectedly"), "An unexpected exception is shown as a failure.");
        AssertTrue(crashing.StartCommand.CanExecute(null), "The user can start again after a failure.");
    }

    private static void TestLibraryUiCancellationAndSingleSession(string workDirectory)
    {
        byte[] data = [1, 2, 3];
        string root = AnalysisLibrary(workDirectory, "ui-cancel", ("a.bin", data), ("b.bin", [4, 5]));
        string dat = WriteDat(Path.Combine(workDirectory, "ui-cancel-dats"), "c.dat", "Sony - PlayStation",
            new DatGameSpec("A", null, new DatRomSpec("a.bin", 3, Sha1: Sha1Of(data))));
        string output = Path.Combine(workDirectory, "ui-cancel-out");
        Dictionary<string, string> before = SnapshotTree(root);

        using ManualResetEventSlim hashing = new();
        int starts = 0;
        DatContentHasher blocking = async (path, kind, token) =>
        {
            hashing.Set();
            await Task.Delay(Timeout.Infinite, token);
            return string.Empty;
        };
        LibraryAnalysisRunner real = RealRunner(blocking);
        LibraryAnalysisViewModel vm = NewLibraryVmFor(root, dat, output, (r, p, t) => { Interlocked.Increment(ref starts); return real(r, p, t); });
        vm.VerifyContent = true;

        Task run = vm.StartCommand.ExecuteAsync(null);
        AssertTrue(hashing.Wait(TimeSpan.FromSeconds(30)), "Analysis reached the hashing phase.");
        AssertTrue(vm.IsRunning && !vm.StartCommand.CanExecute(null), "Start is disabled while running.");
        AssertTrue(vm.CancelCommand.CanExecute(null), "Cancel is available while running.");
        AssertFalse(vm.BrowseLibraryCommand.CanExecute(null) || vm.AddDatCommand.CanExecute(null), "Inputs are locked while running.");
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(1, starts, "A second start while running is ignored.");

        vm.CancelCommand.Execute(null);
        AssertTrue(vm.IsCancelling && !vm.CancelCommand.CanExecute(null), "Cancel is acknowledged once.");
        AssertTrue(run.Wait(TimeSpan.FromSeconds(30)), "Cancellation ends the run promptly.");

        AssertFalse(vm.IsRunning || vm.IsCancelling, "The run state is reset.");
        AssertEqual(LibraryAnalysisOutcome.Cancelled, vm.LastOutcome, "Cancellation is reported as such.");
        AssertTrue(!vm.OutcomeIsClean && vm.OutcomeText.StartsWith("Analysis was cancelled"), "A cancelled run is never shown as completed.");
        AssertFalse(vm.HasPreview, "A cancelled run has no organization preview.");
        AssertTrue(vm.StartCommand.CanExecute(null), "A new session can start after cancelling.");
        AssertFalse(Directory.Exists(output), "Cancellation creates nothing.");
        AssertTreeUnchanged(before, root, "Cancellation leaves the library untouched.");

        // Cancel with nothing running is a harmless no-op.
        vm.Cancel();
        AssertFalse(vm.IsCancelling, "Cancel when idle does nothing.");
    }

    private static void TestLibraryUiLocalizationResources(string workDirectory)
    {
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");

        List<string> keys = [.. english.Keys.Where(k => k.StartsWith("LocLib_", StringComparison.Ordinal)), "LocUi_Header_Library"];
        AssertTrue(keys.Count > 60, "Library strings exist.");

        foreach (string key in keys)
        {
            AssertTrue(arabic.TryGetValue(key, out string? ar) && !string.IsNullOrWhiteSpace(ar), "Arabic string missing: " + key);
            AssertFalse(string.IsNullOrWhiteSpace(english[key]), "English string empty: " + key);
        }

        AssertTrue(arabic.Keys.Where(k => k.StartsWith("LocLib_", StringComparison.Ordinal)).All(english.ContainsKey), "Arabic has no key English lacks.");

        // Every key the view model can ask for is declared in both languages.
        List<string> used =
        [
            .. Enum.GetValues<DatMatchStatus>().Select(LibraryAnalysisViewModel.StatusKey),
            .. Enum.GetValues<DatMatchStatus>().Select(LibraryAnalysisViewModel.StatusHintKey),
            .. Enum.GetValues<LibraryAnalysisPhase>().Select(LibraryAnalysisViewModel.PhaseKey),
            .. Enum.GetValues<LibraryAnalysisOutcome>().Select(LibraryAnalysisViewModel.OutcomeKey),
            .. Enum.GetValues<PlanFlags>().Where(f => f != PlanFlags.None).Select(LibraryAnalysisViewModel.FlagKey),
            "LocFilter_RedumpDatXmlFiles", "LocCommon_Browse", "LocCommon_Cancel", "LocCommon_Close"
        ];

        foreach (string key in used)
        {
            AssertTrue(key.Length > 0 && english.ContainsKey(key) && arabic.ContainsKey(key), "Resource key not declared in both languages: " + key);
        }

        // Every flag has its own wording; statuses are never merged into one label.
        AssertEqual(13, Enum.GetValues<PlanFlags>().Count(f => f != PlanFlags.None), "A new plan flag needs a library string.");
        AssertEqual(7, Enum.GetValues<DatMatchStatus>().Select(LibraryAnalysisViewModel.StatusKey).Distinct().Count(), "Every match status has its own label.");
        AssertTrue(LibraryAnalysisViewModel.StatusKey(DatMatchStatus.CandidateMatch) != LibraryAnalysisViewModel.StatusKey(DatMatchStatus.VerifiedMatch), "Candidate is never labelled Verified.");
        string[] arabicStatusLabels = Enum.GetValues<DatMatchStatus>()
            .Select(status => arabic[LibraryAnalysisViewModel.StatusKey(status)]).ToArray();
        AssertEqual(arabicStatusLabels.Length, arabicStatusLabels.Distinct(StringComparer.Ordinal).Count(), "Arabic status labels are not repeated in the filter.");
        AssertEqual("بلا تطابق", arabic[LibraryAnalysisViewModel.StatusKey(DatMatchStatus.NotFound)], "NotFound means no DAT entry matched.");
        AssertEqual("تعارض", arabic[LibraryAnalysisViewModel.StatusKey(DatMatchStatus.Mismatch)], "Mismatch means evidence conflicts with a DAT entry.");
        AssertEqual("تعذر الفحص", arabic[LibraryAnalysisViewModel.StatusKey(DatMatchStatus.Error)], "Error means the file could not be evaluated.");

        // The window references only declared keys.
        string xaml = System.IO.File.ReadAllText(FindRepoFile(Path.Combine("Views", "LibraryAnalysisWindow.xaml")));
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(xaml, @"\{DynamicResource (Loc[A-Za-z0-9_]+)\}"))
        {
            AssertTrue(english.ContainsKey(match.Groups[1].Value) && arabic.ContainsKey(match.Groups[1].Value), "Window uses an undeclared key: " + match.Groups[1].Value);
        }

        AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(xaml, @"(Text|Content|Header)=""[A-Za-z]"), "The window has no hard-coded display text.");
    }
}
