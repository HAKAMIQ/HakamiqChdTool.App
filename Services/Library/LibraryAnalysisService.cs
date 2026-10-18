using HakamiqChdTool.App.Core.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Library;

public enum LibraryAnalysisPhase
{
    NotStarted,
    Indexing,
    ParsingDats,
    Matching,
    Planning,
    Reporting,
    Finished
}

public enum LibraryAnalysisOutcome
{
    /// <summary>Every phase ran to the end without a recorded problem.</summary>
    Completed,

    /// <summary>A plan and report exist, but at least one phase recorded a problem.</summary>
    CompletedWithIssues,

    /// <summary>Cancelled; whatever finished before cancellation is kept, no plan is built.</summary>
    Cancelled,

    /// <summary>A required input was rejected or a phase failed; nothing usable was produced.</summary>
    Failed
}

public sealed record LibraryAnalysisRequest(
    string LibraryRoot,
    IReadOnlyList<string> DatPaths,
    string OutputRoot,
    bool VerifyContent = false);

/// <summary>Completed/Total count the unit of the phase (entries while matching, DAT files while parsing). Total is 0 when unknown.</summary>
public sealed record LibraryAnalysisProgress(
    LibraryAnalysisPhase Phase,
    int Completed,
    int Total,
    string? Detail = null);

public sealed record LibraryAnalysisIssue(
    LibraryAnalysisPhase Phase,
    string Code,
    string Message);

public sealed record LibraryAnalysisDat(
    string Path,
    DatParseResult Result);

public sealed class LibraryAnalysisResult(
    LibraryAnalysisOutcome outcome,
    LibraryAnalysisPhase lastPhase,
    LibraryIndexResult? index,
    IReadOnlyList<LibraryAnalysisDat> dats,
    DatMatchReport? match,
    OrganizationPlan? plan,
    string? report,
    IReadOnlyList<LibraryAnalysisIssue> issues,
    bool verifyContent)
{
    public LibraryAnalysisOutcome Outcome { get; } = outcome;

    /// <summary>The last phase that started (the interrupted one when cancelled or failed).</summary>
    public LibraryAnalysisPhase LastPhase { get; } = lastPhase;

    public LibraryIndexResult? Index { get; } = index;

    public IReadOnlyList<LibraryAnalysisDat> Dats { get; } = dats;

    /// <summary>Set once matching started; on cancellation it holds only the entries matched so far.</summary>
    public DatMatchReport? Match { get; } = match;

    /// <summary>Null unless every earlier phase finished: a plan built from partial input would misreport unmatched files.</summary>
    public OrganizationPlan? Plan { get; } = plan;

    public string? Report { get; } = report;

    public IReadOnlyList<LibraryAnalysisIssue> Issues { get; } = issues;

    public bool VerifyContent { get; } = verifyContent;

    public bool IsPartial => Outcome is LibraryAnalysisOutcome.Cancelled or LibraryAnalysisOutcome.Failed;

    /// <summary>
    /// True only when the analysis is complete and clean: full index with no issue, every DAT
    /// parsed without rejected records, every file matched without an Error, and a Ready plan.
    /// It does not mean any single item may be applied; that is <see cref="PlanItem.IsSafeToApply"/>.
    /// </summary>
    public bool IsExecutable =>
        Outcome == LibraryAnalysisOutcome.Completed
        && Plan is { Status: PlanStatus.Ready };

    /// <summary>The items a later execution step may consider; empty unless <see cref="IsExecutable"/>.</summary>
    public IReadOnlyList<PlanItem> ExecutableItems =>
        IsExecutable ? Plan!.Items.Where(i => i.IsSafeToApply).ToList() : [];
}

/// <summary>
/// Read-only orchestration of the library analysis pipeline: index, parse DATs, match,
/// plan, preview report. It composes the existing components and adds no matching or
/// planning rules of its own. It never writes, moves, renames, decompresses or deletes
/// anything. Content hashing happens only when <see cref="LibraryAnalysisRequest.VerifyContent"/> is set.
/// </summary>
public sealed class LibraryAnalysisService(LibraryIndexer indexer, DatContentHasher hasher)
{
    private const int MatchChunkSize = 64;

    public LibraryAnalysisService()
        : this(new LibraryIndexer(), DatHashing.Hasher)
    {
    }

    public async Task<LibraryAnalysisResult> AnalyzeAsync(
        LibraryAnalysisRequest request,
        IProgress<LibraryAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<LibraryAnalysisIssue> issues = [];
        List<LibraryAnalysisDat> datResults = [];
        List<DatMatchResult> matched = [];
        LibraryAnalysisPhase phase = LibraryAnalysisPhase.NotStarted;
        LibraryIndexResult? index = null;
        DatMatchReport? match = null;
        bool verify = request.VerifyContent;

        LibraryAnalysisResult Stop(LibraryAnalysisOutcome outcome, OrganizationPlan? plan = null, string? report = null) =>
            new(outcome, phase, index, datResults, match, plan, report ?? BuildPartialReport(outcome, phase, issues), issues, verify);

        List<string> datPaths = [];

        try
        {
            if (string.IsNullOrWhiteSpace(request.LibraryRoot) || string.IsNullOrWhiteSpace(request.OutputRoot))
            {
                issues.Add(new(phase, "input-missing", "Library root and output root are required."));
                return Stop(LibraryAnalysisOutcome.Failed);
            }

            foreach (string path in request.DatPaths ?? [])
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string full = Path.GetFullPath(path.Trim());

                if (!datPaths.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    datPaths.Add(full);
                }
            }

            if (datPaths.Count == 0)
            {
                issues.Add(new(phase, "no-dat", "At least one DAT file is required."));
                return Stop(LibraryAnalysisOutcome.Failed);
            }

            // Phase 1: index. The indexer reports no per-file progress, so only start/end are reported.
            phase = LibraryAnalysisPhase.Indexing;
            progress?.Report(new(phase, 0, 0, request.LibraryRoot));
            index = await indexer.IndexAsync(request.LibraryRoot, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(phase, index.Entries.Count, index.Entries.Count, request.LibraryRoot));

            foreach (LibraryIndexIssue issue in index.Issues)
            {
                issues.Add(new(phase, "index-" + issue.Kind, (issue.RelativePath.Length == 0 ? string.Empty : issue.RelativePath + ": ") + issue.Message));
            }

            if (index.Outcome == LibraryIndexOutcome.Cancelled)
            {
                return Stop(LibraryAnalysisOutcome.Cancelled);
            }

            if (index.Outcome == LibraryIndexOutcome.Failed)
            {
                return Stop(LibraryAnalysisOutcome.Failed);
            }

            // Phase 2: parse DATs. A DAT that does not parse leaves the catalog incomplete, so the
            // run continues for the preview but is never marked complete.
            phase = LibraryAnalysisPhase.ParsingDats;
            List<DatFile> dats = [];

            for (int i = 0; i < datPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string datPath = datPaths[i];
                progress?.Report(new(phase, i, datPaths.Count, datPath));

                DatParseResult parsed = await Task
                    .Run(() => DatParser.Parse(datPath, cancellationToken), CancellationToken.None)
                    .ConfigureAwait(false);
                datResults.Add(new(datPath, parsed));

                if (parsed.Status == DatParseStatus.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (parsed.Status == DatParseStatus.Parsed && parsed.Dat is not null)
                {
                    dats.Add(parsed.Dat);

                    if (parsed.RejectedRecords > 0)
                    {
                        issues.Add(new(phase, "dat-rejected-records", $"{datPath}: {parsed.RejectedRecords} malformed record(s) were dropped."));
                    }
                }
                else
                {
                    issues.Add(new(phase, "dat-" + parsed.Status, $"{datPath}: " + string.Join("; ", parsed.Issues.Select(x => x.Code))));
                }
            }

            progress?.Report(new(phase, datPaths.Count, datPaths.Count));

            if (dats.Count == 0)
            {
                issues.Add(new(phase, "no-usable-dat", "No DAT file could be parsed."));
                return Stop(LibraryAnalysisOutcome.Failed);
            }

            // Phase 3: match in chunks so progress is real. Entries are matched independently,
            // so chunking cannot change any result.
            phase = LibraryAnalysisPhase.Matching;
            DatMatcher matcher = new(dats, hasher);
            DatMatchOptions options = new(request.VerifyContent);
            IReadOnlyList<LibraryIndexEntry> entries = index.Entries;
            progress?.Report(new(phase, 0, entries.Count));

            for (int offset = 0; offset < entries.Count; offset += MatchChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<LibraryIndexEntry> chunk = entries.Skip(offset).Take(MatchChunkSize).ToList();
                DatMatchReport part = await matcher
                    .MatchAsync(index.RootPath, chunk, options, cancellationToken)
                    .ConfigureAwait(false);
                matched.AddRange(part.Results);

                // The report is snapshotted only when matching ends: copying the whole list per
                // chunk would make a large library quadratic in memory churn.
                if (part.Outcome == DatMatchOutcome.Cancelled)
                {
                    match = new DatMatchReport(DatMatchOutcome.Cancelled, matched.ToList());
                    return Stop(LibraryAnalysisOutcome.Cancelled);
                }

                progress?.Report(new(phase, Math.Min(offset + chunk.Count, entries.Count), entries.Count));
            }

            match = new DatMatchReport(DatMatchOutcome.Completed, matched.ToList());

            int errors = match.Count(DatMatchStatus.Error);

            if (errors > 0)
            {
                issues.Add(new(phase, "match-errors", $"{errors} file(s) could not be evaluated."));
            }

            // Phase 4: plan.
            phase = LibraryAnalysisPhase.Planning;
            progress?.Report(new(phase, 0, 1));
            cancellationToken.ThrowIfCancellationRequested();
            OrganizationPlan plan = OrganizationPlanner.Plan(index.RootPath, request.OutputRoot, entries, match.Results);

            foreach (string planIssue in plan.Issues)
            {
                issues.Add(new(phase, "plan-" + plan.Status, planIssue));
            }

            progress?.Report(new(phase, 1, 1));

            // Phase 5: preview report.
            phase = LibraryAnalysisPhase.Reporting;
            progress?.Report(new(phase, 0, 1));
            string report = BuildReport(plan, index, datResults, issues, verify);
            progress?.Report(new(phase, 1, 1));

            phase = LibraryAnalysisPhase.Finished;
            progress?.Report(new(phase, 1, 1));

            LibraryAnalysisOutcome outcome = plan.Status == PlanStatus.Rejected
                ? LibraryAnalysisOutcome.Failed
                : issues.Count == 0 ? LibraryAnalysisOutcome.Completed : LibraryAnalysisOutcome.CompletedWithIssues;
            return Stop(outcome, plan, report);
        }
        catch (OperationCanceledException)
        {
            if (phase == LibraryAnalysisPhase.Matching && match is null && matched.Count > 0)
            {
                match = new DatMatchReport(DatMatchOutcome.Cancelled, matched.ToList());
            }

            return Stop(LibraryAnalysisOutcome.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            issues.Add(new(phase, "phase-failed", ex.Message));
            return Stop(LibraryAnalysisOutcome.Failed);
        }
    }

    private static string BuildReport(
        OrganizationPlan plan,
        LibraryIndexResult index,
        IReadOnlyList<LibraryAnalysisDat> dats,
        IReadOnlyList<LibraryAnalysisIssue> issues,
        bool verify)
    {
        StringBuilder text = new();
        text.AppendLine("Library analysis (read-only; no files were changed)");
        text.AppendLine("Library root: " + index.RootPath);
        text.AppendLine($"Indexed files: {index.Entries.Count}; directories: {index.DirectoriesVisited}");
        text.AppendLine("DATs parsed: " + dats.Count(d => d.Result.Status == DatParseStatus.Parsed) + " of " + dats.Count);
        text.AppendLine("Content verification: " + (verify ? "requested" : "off (name and size evidence only)"));
        text.AppendLine("Complete: " + (issues.Count == 0 && plan.Status == PlanStatus.Ready ? "yes" : "no"));

        foreach (LibraryAnalysisIssue issue in issues)
        {
            text.AppendLine($"Issue [{issue.Phase}/{issue.Code}]: {issue.Message}");
        }

        text.AppendLine();
        text.Append(plan.ToReport());
        return text.ToString();
    }

    private static string BuildPartialReport(
        LibraryAnalysisOutcome outcome,
        LibraryAnalysisPhase phase,
        IReadOnlyList<LibraryAnalysisIssue> issues)
    {
        StringBuilder text = new();
        text.AppendLine("Library analysis (read-only; no files were changed)");
        text.AppendLine($"Result: {outcome} during {phase}; no plan was built.");

        foreach (LibraryAnalysisIssue issue in issues)
        {
            text.AppendLine($"Issue [{issue.Phase}/{issue.Code}]: {issue.Message}");
        }

        return text.ToString();
    }
}
