using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Core.Library;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Library;
using HakamiqChdTool.App.Services.ExternalTools.Igir;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.ViewModels;

/// <summary>Runs one analysis; the production binding is <see cref="LibraryAnalysisService"/>.</summary>
public delegate Task<LibraryAnalysisResult> LibraryAnalysisRunner(
    LibraryAnalysisRequest request,
    IProgress<LibraryAnalysisProgress> progress,
    CancellationToken cancellationToken);

/// <summary>One line of the result grid. Built only from analysis output; never edited by the user.</summary>
public sealed record LibraryResultRow(
    string Source,
    DatMatchStatus Status,
    string StatusText,
    string StatusHint,
    string Destination,
    string Bucket,
    string Conflicts,
    string Game,
    string Platform,
    string Region,
    string Evidence,
    string Reasons,
    string DiscSet,
    string ReviewReason);

public sealed record LibraryStatusFilter(DatMatchStatus? Status, string Label);

/// <summary>
/// Drives library analysis and the explicit, journaled organizer actions in the Library window.
/// </summary>
public sealed partial class LibraryAnalysisViewModel : ObservableObject
{
    private readonly LibraryAnalysisRunner _runner;
    private readonly IOptionsPickerService _picker;
    private readonly Func<string, string> _text;
    private readonly Action<Action> _post;
    private readonly LibraryOperationJournal _journal;
    private readonly Func<string, bool> _confirm;
    private LibraryMovePlan? _movePlan;
    private CancellationTokenSource? _cts;
    private List<LibraryResultRow> _allRows = [];

    // Language-independent state. Every displayed string is rendered from these, so a language
    // switch (RefreshLanguage) can re-render without re-running the analysis.
    private IReadOnlyList<PlanItem>? _planItems;
    private IReadOnlyList<DatMatchResult>? _matchResults;
    private (int Files, int Dats, int Safe)? _summary;
    private string _phaseKey = "LocLib_Phase_Idle";
    private string _errorKey = string.Empty;
    private string _outcomeKey = string.Empty;
    private LibraryAnalysisProgress? _lastProgress;

    // Identifies the current run. A callback from an earlier run carries an older id and is dropped.
    private int _runId;

    public LibraryAnalysisViewModel(
        LibraryAnalysisRunner runner,
        IOptionsPickerService picker,
        Func<string, string>? text = null,
        Action<Action>? post = null,
        LibraryOperationJournal? journal = null,
        Func<string, bool>? confirm = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(picker);

        _runner = runner;
        _picker = picker;
        _text = text ?? ArabicUi.Get;
        _post = post ?? PostToUiThread;
        _journal = journal ?? new LibraryOperationJournal();
        _confirm = confirm ?? (_ => false);

        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning && !IsCancelling);
        BrowseLibraryCommand = new RelayCommand(BrowseLibrary, () => !IsRunning);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsRunning);
        AddDatCommand = new RelayCommand(AddDat, () => !IsRunning);
        RemoveDatCommand = new RelayCommand(RemoveSelectedDat, () => !IsRunning && SelectedDat is not null);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsRunning && CanExecutePlan);
        JournalCommand = new AsyncRelayCommand(ShowJournalAsync, () => !IsRunning);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => !IsRunning);
        IgirReportCommand = new AsyncRelayCommand(RunIgirReportAsync, () => !IsRunning);

        SetPhase("LocLib_Phase_Idle");
        RebuildFilters();

        DatPaths.CollectionChanged += (_, _) => InputsChanged();
    }

    public ObservableCollection<string> DatPaths { get; } = [];

    public ObservableCollection<LibraryResultRow> Rows { get; } = [];

    public ObservableCollection<LibraryStatusFilter> Filters { get; } = [];

    public ObservableCollection<string> Issues { get; } = [];

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand BrowseLibraryCommand { get; }

    public IRelayCommand BrowseOutputCommand { get; }

    public IRelayCommand AddDatCommand { get; }

    public IRelayCommand RemoveDatCommand { get; }

    public IAsyncRelayCommand ExecuteCommand { get; }

    public IAsyncRelayCommand JournalCommand { get; }

    public IAsyncRelayCommand UndoCommand { get; }

    public IAsyncRelayCommand IgirReportCommand { get; }

    [ObservableProperty]
    private string _libraryRoot = string.Empty;

    [ObservableProperty]
    private string _outputRoot = string.Empty;

    /// <summary>Full-file hashing is off unless the user turns this on.</summary>
    [ObservableProperty]
    private bool _verifyContent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDatCommand))]
    private string? _selectedDat;

    [ObservableProperty]
    private LibraryResultRow? _selectedRow;

    [ObservableProperty]
    private LibraryStatusFilter? _selectedFilter;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private string _phaseText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressIsIndeterminate;

    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>Input validation message, shown instead of starting a run.</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>Outcome message; empty until a run has ended.</summary>
    [ObservableProperty]
    private string _outcomeText = string.Empty;

    /// <summary>True only when the last run ended Completed. Anything else is shown as a warning.</summary>
    [ObservableProperty]
    private bool _outcomeIsClean;

    [ObservableProperty]
    private bool _hasOutcome;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _reportText = string.Empty;

    [ObservableProperty]
    private bool _hasPreview;

    public LibraryAnalysisOutcome? LastOutcome { get; private set; }

    public bool HasIssues => Issues.Count > 0;
    public bool CanExecutePlan => _movePlan is not null && _movePlan.Operations.Any(operation => operation.Eligible);

    public bool IsIdle => !IsRunning;

    public bool HasSelection => SelectedRow is not null;

    public bool HasNoSelection => SelectedRow is null;

    partial void OnSelectedRowChanged(LibraryResultRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        NotifyCommands();
    }

    partial void OnIsCancellingChanged(bool value) => NotifyCommands();

    partial void OnSelectedFilterChanged(LibraryStatusFilter? value) => ApplyFilter();

    partial void OnLibraryRootChanged(string value) => InputsChanged();

    partial void OnOutputRootChanged(string value) => InputsChanged();

    partial void OnVerifyContentChanged(bool value) => InputsChanged();

    // Results describe the inputs they were produced from. Once an input changes they no longer
    // do, so they are cleared rather than left next to a stale "completed" message.
    private void InputsChanged()
    {
        if (IsRunning)
        {
            return;
        }

        SetError(string.Empty);

        if (_allRows.Count > 0 || HasOutcome || LastOutcome is not null || Issues.Count > 0)
        {
            ClearResults();
        }
    }

    /// <summary>Re-renders every displayed string in the current language. Call after a language switch.</summary>
    public void RefreshLanguage()
    {
        LibraryStatusFilter? previousFilter = SelectedFilter;
        string? selectedSource = SelectedRow?.Source;

        PhaseText = _text(_phaseKey);

        if (_errorKey.Length > 0)
        {
            ErrorText = _text(_errorKey);
        }

        if (_outcomeKey.Length > 0)
        {
            OutcomeText = _text(_outcomeKey);
        }

        if (_lastProgress is { } progress && IsRunning && !IsCancelling)
        {
            ProgressText = ProgressLabel(progress);
        }

        if (_summary is { } summary)
        {
            SummaryText = SummaryLabel(summary);
        }

        if (_planItems is not null)
        {
            _allRows = _planItems.Select(RowFromPlan).ToList();
        }
        else if (_matchResults is not null)
        {
            _allRows = _matchResults.Select(RowFromMatch).ToList();
        }

        RebuildFilters(previousFilter?.Status);

        if (selectedSource is not null)
        {
            SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Source, selectedSource, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void SetPhase(string key)
    {
        _phaseKey = key;
        PhaseText = _text(key);
    }

    private void SetError(string key)
    {
        _errorKey = key;
        ErrorText = key.Length == 0 ? string.Empty : _text(key);
    }

    private string ProgressLabel(LibraryAnalysisProgress value) =>
        value.Total > 0 ? string.Format(_text("LocLib_Fmt_Progress"), value.Completed, value.Total) : string.Empty;

    private string SummaryLabel((int Files, int Dats, int Safe) summary) =>
        string.Format(_text("LocLib_Fmt_Summary"), summary.Files, summary.Dats, summary.Safe);

    /// <summary>Maps a match status to its resource key. Candidate is never presented as Verified.</summary>
    public static string StatusKey(DatMatchStatus status) => status switch
    {
        DatMatchStatus.VerifiedMatch => "LocLib_Status_Verified",
        DatMatchStatus.CandidateMatch => "LocLib_Status_Candidate",
        DatMatchStatus.Ambiguous => "LocLib_Status_Ambiguous",
        DatMatchStatus.Mismatch => "LocLib_Status_Mismatch",
        DatMatchStatus.Unsupported => "LocLib_Status_Unsupported",
        DatMatchStatus.NotFound => "LocLib_Status_NotFound",
        _ => "LocLib_Status_Error"
    };

    public static string StatusHintKey(DatMatchStatus status) => status switch
    {
        DatMatchStatus.VerifiedMatch => "LocLib_StatusHint_Verified",
        DatMatchStatus.CandidateMatch => "LocLib_StatusHint_Candidate",
        DatMatchStatus.Ambiguous => "LocLib_StatusHint_Ambiguous",
        DatMatchStatus.Mismatch => "LocLib_StatusHint_Mismatch",
        DatMatchStatus.Unsupported => "LocLib_StatusHint_Unsupported",
        DatMatchStatus.NotFound => "LocLib_StatusHint_NotFound",
        _ => "LocLib_StatusHint_Error"
    };

    public static string PhaseKey(LibraryAnalysisPhase phase) => phase switch
    {
        LibraryAnalysisPhase.Indexing => "LocLib_Phase_Indexing",
        LibraryAnalysisPhase.ParsingDats => "LocLib_Phase_ParsingDats",
        LibraryAnalysisPhase.Matching => "LocLib_Phase_Matching",
        LibraryAnalysisPhase.Planning => "LocLib_Phase_Planning",
        LibraryAnalysisPhase.Reporting => "LocLib_Phase_Reporting",
        LibraryAnalysisPhase.Finished => "LocLib_Phase_Finished",
        _ => "LocLib_Phase_Idle"
    };

    public static string OutcomeKey(LibraryAnalysisOutcome outcome) => outcome switch
    {
        LibraryAnalysisOutcome.Completed => "LocLib_Outcome_Completed",
        LibraryAnalysisOutcome.CompletedWithIssues => "LocLib_Outcome_CompletedWithIssues",
        LibraryAnalysisOutcome.Cancelled => "LocLib_Outcome_Cancelled",
        _ => "LocLib_Outcome_Failed"
    };

    public static string FlagKey(PlanFlags flag) => flag switch
    {
        PlanFlags.UnverifiedIdentity => "LocLib_Flag_UnverifiedIdentity",
        PlanFlags.UnknownPlatform => "LocLib_Flag_UnknownPlatform",
        PlanFlags.ContentUnverifiable => "LocLib_Flag_ContentUnverifiable",
        PlanFlags.DuplicateCandidate => "LocLib_Flag_DuplicateCandidate",
        PlanFlags.DestinationCollision => "LocLib_Flag_DestinationCollision",
        PlanFlags.InvalidDestinationName => "LocLib_Flag_InvalidDestinationName",
        PlanFlags.PathTooLong => "LocLib_Flag_PathTooLong",
        PlanFlags.DestinationConflictsWithSource => "LocLib_Flag_DestinationConflictsWithSource",
        PlanFlags.DestinationExists => "LocLib_Flag_DestinationExists",
        PlanFlags.InconsistentDiscSet => "LocLib_Flag_InconsistentDiscSet",
        PlanFlags.SheetNeedsReferenceRewrite => "LocLib_Flag_SheetNeedsReferenceRewrite",
        PlanFlags.IncompleteDiscSet => "LocLib_Flag_IncompleteDiscSet",
        PlanFlags.PartiallyVerifiedSet => "LocLib_Flag_PartiallyVerifiedSet",
        _ => string.Empty
    };

    /// <summary>
    /// Validates the inputs and builds the service request. Returns false with a resource key
    /// for the message; no request is produced and nothing is read from the library.
    /// </summary>
    public bool TryBuildRequest(out LibraryAnalysisRequest? request, out string errorKey)
    {
        request = null;
        string library = LibraryRoot.Trim();
        string output = OutputRoot.Trim();

        if (library.Length == 0)
        {
            errorKey = "LocLib_Err_NoLibrary";
            return false;
        }

        if (!Directory.Exists(library))
        {
            errorKey = "LocLib_Err_LibraryMissing";
            return false;
        }

        if (DatPaths.Count == 0)
        {
            errorKey = "LocLib_Err_NoDat";
            return false;
        }

        if (DatPaths.Any(path => !File.Exists(path)))
        {
            errorKey = "LocLib_Err_DatMissing";
            return false;
        }

        if (output.Length == 0)
        {
            errorKey = "LocLib_Err_NoOutput";
            return false;
        }

        if (!Path.IsPathFullyQualified(output))
        {
            errorKey = "LocLib_Err_OutputNotAbsolute";
            return false;
        }

        errorKey = string.Empty;
        request = new LibraryAnalysisRequest(library, DatPaths.ToList(), output, VerifyContent);
        return true;
    }

    public void AddDatPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string trimmed = path.Trim();

        if (!DatPaths.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            DatPaths.Add(trimmed);
        }
    }

    /// <summary>Asks a running analysis to stop. Safe to call at any time, including repeatedly.</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts = _cts;

        if (cts is null || !IsRunning)
        {
            return;
        }

        IsCancelling = true;
        SetPhase("LocLib_Cancelling");

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (!TryBuildRequest(out LibraryAnalysisRequest? request, out string errorKey) || request is null)
        {
            // A failed validation starts no run, so nothing from an earlier run may stay on screen.
            ClearResults();
            SetError(errorKey);
            return;
        }

        SetError(string.Empty);
        ClearResults();
        int runId = ++_runId;
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        IsCancelling = false;
        IsRunning = true;
        ProgressValue = 0;
        ProgressIsIndeterminate = true;
        SetPhase(PhaseKey(LibraryAnalysisPhase.Indexing));

        LibraryAnalysisResult? result = null;
        LibraryMovePlan? movePlan = null;
        string? failureKey = null;

        try
        {
            // Indexing, parsing and hashing are synchronous or file-bound; keep them off the UI thread.
            result = await Task
                .Run(() => _runner(request, new UiProgress(this, _post, runId), token), CancellationToken.None)
                .ConfigureAwait(true);
            if (result.IsExecutable && result.Plan is not null)
            {
                movePlan = await Task.Run(() => new LibraryPlanValidator()
                    .CreateAsync(result.Plan, request.LibraryRoot, token), CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            failureKey = "LocLib_Outcome_Cancelled";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            global::Serilog.Log.Error(ex, "Library analysis stopped unexpectedly.");
            failureKey = "LocLib_Err_Unexpected";
        }
        finally
        {
            IsRunning = false;
            IsCancelling = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (runId != _runId)
        {
            // A newer run owns the display; this one must not overwrite it.
            return;
        }

        if (result is not null)
        {
            _movePlan = movePlan;
            ApplyResult(result);
        }
        else
        {
            LastOutcome = failureKey == "LocLib_Outcome_Cancelled" ? LibraryAnalysisOutcome.Cancelled : LibraryAnalysisOutcome.Failed;
            ShowOutcome(failureKey ?? "LocLib_Err_Unexpected", clean: false);
        }

        SetPhase("LocLib_Phase_Idle");
        ProgressIsIndeterminate = false;
    }

    private void ApplyProgress(int runId, LibraryAnalysisProgress value)
    {
        // A late callback after the run ended, or from an earlier run, must not overwrite the current state.
        if (runId != _runId || !IsRunning || IsCancelling)
        {
            return;
        }

        _lastProgress = value;
        SetPhase(PhaseKey(value.Phase));

        if (value.Total > 0)
        {
            ProgressIsIndeterminate = false;
            ProgressValue = Math.Clamp(100.0 * value.Completed / value.Total, 0, 100);
            ProgressText = ProgressLabel(value);
        }
        else
        {
            ProgressIsIndeterminate = true;
            ProgressText = string.Empty;
        }
    }

    private void ApplyResult(LibraryAnalysisResult result)
    {
        LastOutcome = result.Outcome;
        ReportText = result.Report ?? string.Empty;

        foreach (LibraryAnalysisIssue issue in result.Issues)
        {
            Issues.Add($"[{issue.Phase}] {issue.Code}: {issue.Message}");
        }

        OnPropertyChanged(nameof(HasIssues));

        // Partial results are shown, but only a Completed run is ever presented as clean.
        if (result.Plan is not null)
        {
            _planItems = result.Plan.Items;
            _allRows = _planItems.Select(RowFromPlan).ToList();
            HasPreview = result.Plan.Status == PlanStatus.Ready;
            _summary = (result.Index?.Entries.Count ?? 0, result.Dats.Count, result.Plan.Items.Count(i => i.IsSafeToApply));
            SummaryText = SummaryLabel(_summary.Value);
        }
        else if (result.Match is not null)
        {
            _matchResults = result.Match.Results;
            _allRows = _matchResults.Select(RowFromMatch).ToList();
            HasPreview = false;
            SummaryText = string.Empty;
        }

        RebuildFilters();
        NotifyCommands();
        ShowOutcome(OutcomeKey(result.Outcome), clean: result.Outcome == LibraryAnalysisOutcome.Completed);
    }

    private async Task ExecuteAsync()
    {
        LibraryMovePlan? plan = _movePlan;
        if (plan is null || !CanExecutePlan || IsRunning || !_confirm("LocLib_ExecuteConfirm")) return;
        HashSet<Guid> selected = plan.Operations.Where(operation => operation.Eligible)
            .Select(operation => operation.OperationId).ToHashSet();
        await RunOrganizerActionAsync(async token =>
        {
            IReadOnlyList<LibraryOperationResult> results = await new LibraryOrganizerService(_journal)
                .ExecuteAsync(plan, selected, confirmed: true,
                    new Progress<(int Completed, int Total)>(value => _post(() =>
                    {
                        ProgressIsIndeterminate = false;
                        ProgressValue = value.Total == 0 ? 0 : 100.0 * value.Completed / value.Total;
                        ProgressText = ProgressLabel(new LibraryAnalysisProgress(LibraryAnalysisPhase.Planning, value.Completed, value.Total));
                    })), token).ConfigureAwait(false);
            SetReport("LocLib_Journal_Title", results.Select(result => $"{result.State}: {result.OperationId} {result.ErrorCode}"));
            _post(() =>
            {
                _movePlan = null;
                OnPropertyChanged(nameof(CanExecutePlan));
            });
            SetError(string.Empty);
        }).ConfigureAwait(true);
    }

    private async Task ShowJournalAsync()
    {
        await RunOrganizerActionAsync(async token =>
        {
            _ = await new LibraryUndoService(_journal).RecoverAsync(cancellationToken: token).ConfigureAwait(false);
            IReadOnlyList<LibraryJournalEvent> events = await _journal.ReadAllAsync(token).ConfigureAwait(false);
            SetReport("LocLib_Journal_Title", events.Select(entry =>
                $"{entry.TimestampUtc:O}  {entry.State}  {entry.SourcePath} → {entry.DestinationPath}"));
        }).ConfigureAwait(true);
    }

    private async Task UndoAsync()
    {
        if (IsRunning || !_confirm("LocLib_UndoConfirm")) return;
        await RunOrganizerActionAsync(async token =>
        {
            IReadOnlyList<LibraryJournalEvent> events = await _journal.ReadAllAsync(token).ConfigureAwait(false);
            Guid? latestPlan = events.LastOrDefault()?.PlanId;
            if (latestPlan is null) throw new InvalidOperationException("No completed organizer plan can be undone.");
            LibraryUndoService undo = new(_journal);
            IReadOnlyList<LibraryOperationResult> preview = await undo.PreviewUndoAsync(latestPlan, token).ConfigureAwait(false);
            if (preview.Count == 0) throw new InvalidOperationException("No completed organizer operations can be undone.");
            IReadOnlyList<LibraryOperationResult> results = await undo.UndoAsync(latestPlan, token).ConfigureAwait(false);
            SetReport("LocLib_Journal_Title", results.Select(result => $"{result.State}: {result.OperationId} {result.ErrorCode}"));
            _post(() =>
            {
                _movePlan = null;
                OnPropertyChanged(nameof(CanExecutePlan));
            });
        }).ConfigureAwait(true);
    }

    private async Task RunIgirReportAsync()
    {
        if (!Directory.Exists(LibraryRoot) || DatPaths.Count == 0 || DatPaths.Any(path => !File.Exists(path)))
        {
            SetError("LocLib_Err_NoDat");
            return;
        }
        string workRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HakamiqChdTool", "Igir", "Runs", Guid.NewGuid().ToString("N"));
        IgirReportRequest request = new(LibraryRoot, DatPaths.ToArray(), workRoot);
        await RunOrganizerActionAsync(async token =>
        {
            IgirReportResult result;
            try
            {
                result = await new IgirReportService().RunAsync(AppContext.BaseDirectory, request, token).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                SetError("LocLib_IgirMissing");
                return;
            }
            catch (IOException ex)
            {
                global::Serilog.Log.Error(ex, "igir report failed.");
                SetError("LocLib_IgirFailed");
                return;
            }
            SetReport("LocLib_IgirReport_Title", new[] { result.ReportPath }.Concat(result.Rows.Select(row =>
                $"{row.Status}  {row.DatName}  {row.GameName}  {row.RomFiles}")));
        }).ConfigureAwait(true);
    }

    private async Task RunOrganizerActionAsync(Func<CancellationToken, Task> action)
    {
        SetError(string.Empty);
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressValue = 0;
        ProgressIsIndeterminate = true;
        try
        {
            await action(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetError("LocLib_Outcome_Cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            global::Serilog.Log.Error(ex, "Library organizer action failed.");
            SetError("LocLib_Err_Unexpected");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            IsCancelling = false;
            ProgressIsIndeterminate = false;
            NotifyCommands();
        }
    }

    private void SetReport(string titleKey, IEnumerable<string> lines)
    {
        string content = string.Join(Environment.NewLine, lines);
        _post(() => ReportText = _text(titleKey) + Environment.NewLine + content);
    }

    private void ShowOutcome(string key, bool clean)
    {
        _outcomeKey = key;
        OutcomeText = _text(key);
        OutcomeIsClean = clean;
        HasOutcome = true;
    }

    // Built once: a library can have hundreds of thousands of rows, one array per row is needless churn.
    private static readonly PlanFlags[] NamedFlags = Enum.GetValues<PlanFlags>().Where(flag => flag != PlanFlags.None).ToArray();

    private LibraryResultRow RowFromPlan(PlanItem item)
    {
        string flags = item.Flags == PlanFlags.None
            ? string.Empty
            : string.Join("; ", NamedFlags
                .Where(flag => item.Flags.HasFlag(flag))
                .Select(flag => _text(FlagKey(flag))));

        return new LibraryResultRow(
            item.SourceRelativePath,
            item.MatchStatus,
            _text(StatusKey(item.MatchStatus)),
            _text(StatusHintKey(item.MatchStatus)),
            item.DestinationRelativePath,
            _text(item.Bucket == PlanBucket.Review ? "LocLib_Bucket_Review" : "LocLib_Bucket_Platform"),
            flags,
            item.GameName ?? string.Empty,
            item.Platform ?? string.Empty,
            item.Region ?? string.Empty,
            string.Join(", ", item.Evidence ?? []),
            string.Join(", ", item.Reasons),
            item.DiscSetId ?? string.Empty,
            item.ReviewReason ?? string.Empty);
    }

    private LibraryResultRow RowFromMatch(DatMatchResult match) =>
        new(
            match.RelativePath,
            match.Status,
            _text(StatusKey(match.Status)),
            _text(StatusHintKey(match.Status)),
            string.Empty,
            string.Empty,
            string.Empty,
            match.GameName ?? string.Empty,
            match.Platform ?? string.Empty,
            match.Region ?? string.Empty,
            string.Join(", ", match.Evidence),
            string.Join(", ", match.Reasons),
            string.Empty,
            string.Empty);

    private void RebuildFilters(DatMatchStatus? keepStatus = null)
    {
        Filters.Clear();
        Filters.Add(new LibraryStatusFilter(null, string.Format(_text("LocLib_Fmt_FilterItem"), _text("LocLib_Filter_All"), _allRows.Count)));

        foreach (DatMatchStatus status in new[]
        {
            DatMatchStatus.VerifiedMatch, DatMatchStatus.CandidateMatch, DatMatchStatus.Ambiguous,
            DatMatchStatus.Mismatch, DatMatchStatus.Unsupported, DatMatchStatus.NotFound, DatMatchStatus.Error
        })
        {
            Filters.Add(new LibraryStatusFilter(
                status,
                string.Format(_text("LocLib_Fmt_FilterItem"), _text(StatusKey(status)), _allRows.Count(r => r.Status == status))));
        }

        LibraryStatusFilter target = keepStatus is null
            ? Filters[0]
            : Filters.FirstOrDefault(f => f.Status == keepStatus) ?? Filters[0];

        // Assigning a different filter already re-applies it through OnSelectedFilterChanged; applying
        // again would rebuild every row twice. An equal filter raises no change, so apply it here.
        bool filterChanged = !EqualityComparer<LibraryStatusFilter?>.Default.Equals(SelectedFilter, target);
        SelectedFilter = target;

        if (!filterChanged)
        {
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        DatMatchStatus? status = SelectedFilter?.Status;
        Rows.Clear();

        foreach (LibraryResultRow row in _allRows)
        {
            if (status is null || row.Status == status)
            {
                Rows.Add(row);
            }
        }

        SelectedRow = null;
    }

    private void ClearResults()
    {
        _allRows = [];
        _movePlan = null;
        OnPropertyChanged(nameof(CanExecutePlan));
        _planItems = null;
        _matchResults = null;
        _summary = null;
        _lastProgress = null;
        _outcomeKey = string.Empty;
        Issues.Clear();
        OnPropertyChanged(nameof(HasIssues));
        ReportText = string.Empty;
        SummaryText = string.Empty;
        OutcomeText = string.Empty;
        HasOutcome = false;
        OutcomeIsClean = false;
        HasPreview = false;
        LastOutcome = null;
        ProgressText = string.Empty;
        RebuildFilters();
    }

    private void BrowseLibrary()
    {
        string? picked = _picker.PickFolder("LocLib_PickLibraryTitle", LibraryRoot);

        if (picked is not null)
        {
            LibraryRoot = picked;
        }
    }

    private void BrowseOutput()
    {
        string? picked = _picker.PickFolder("LocLib_PickOutputTitle", OutputRoot);

        if (picked is not null)
        {
            OutputRoot = picked;
        }
    }

    private void AddDat()
    {
        AddDatPath(_picker.PickFile(
            "LocLib_PickDatTitle",
            "LocFilter_RedumpDatXmlFiles",
            SelectedDat,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
    }

    private void RemoveSelectedDat()
    {
        if (SelectedDat is not null)
        {
            DatPaths.Remove(SelectedDat);
            SelectedDat = null;
        }
    }

    private void NotifyCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        BrowseLibraryCommand.NotifyCanExecuteChanged();
        BrowseOutputCommand.NotifyCanExecuteChanged();
        AddDatCommand.NotifyCanExecuteChanged();
        RemoveDatCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        JournalCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        IgirReportCommand.NotifyCanExecuteChanged();
    }

    private static void PostToUiThread(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private sealed class UiProgress(LibraryAnalysisViewModel owner, Action<Action> post, int runId) : IProgress<LibraryAnalysisProgress>
    {
        public void Report(LibraryAnalysisProgress value) => post(() => owner.ApplyProgress(runId, value));
    }
}
