using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Workflow.Paths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Core.Library;

[Flags]
public enum PlanFlags
{
    None = 0,
    UnverifiedIdentity = 1,
    UnknownPlatform = 2,
    ContentUnverifiable = 4,
    DuplicateCandidate = 8,
    DestinationCollision = 16,
    InvalidDestinationName = 32,
    PathTooLong = 64,
    DestinationConflictsWithSource = 128,
    DestinationExists = 256,
    InconsistentDiscSet = 512,

    /// <summary>A CUE/GDI reference is not a plain file name beside the sheet, so moving would need the sheet rewritten.</summary>
    SheetNeedsReferenceRewrite = 1024,

    /// <summary>A sheet is unreadable, lists no files, or references a file the index does not contain.</summary>
    IncompleteDiscSet = 2048,

    /// <summary>Some member of the disc set is not content-verified, so the set is not applied as a unit.</summary>
    PartiallyVerifiedSet = 4096
}

public enum PlanBucket
{
    Platform,
    Review
}

/// <summary>A proposed move. The planner never performs it.</summary>
public sealed record PlanItem(
    string SourceRelativePath,
    string DestinationRelativePath,
    PlanBucket Bucket,
    DatMatchStatus MatchStatus,
    string? Platform,
    string? GameName,
    string? DiscSetId,
    PlanFlags Flags,
    IReadOnlyList<string> Reasons,
    string? DatName = null,
    string? Region = null,
    string? Version = null,
    IReadOnlyList<DatEvidence>? Evidence = null,
    string? ReviewReason = null)
{
    /// <summary>True only for a content-verified identity with no flags, in a platform folder.</summary>
    public bool IsSafeToApply =>
        Bucket == PlanBucket.Platform
        && MatchStatus == DatMatchStatus.VerifiedMatch
        && Flags == PlanFlags.None;
}

public enum PlanStatus
{
    Ready,
    Rejected
}

public sealed class OrganizationPlan(
    PlanStatus status,
    string outputRoot,
    IReadOnlyList<PlanItem> items,
    IReadOnlyList<string> issues)
{
    private static readonly (PlanFlags Flag, string Meaning)[] FlagMeanings =
    [
        (PlanFlags.UnverifiedIdentity, "identity rests on names/size or a weak hash, not verified content"),
        (PlanFlags.UnknownPlatform, "the catalog does not name a platform"),
        (PlanFlags.ContentUnverifiable, "this representation cannot be verified by content"),
        (PlanFlags.DuplicateCandidate, "another file has the same catalog identity"),
        (PlanFlags.DestinationCollision, "another item proposes the same destination (compared case-insensitively)"),
        (PlanFlags.InvalidDestinationName, "a destination folder name is not valid on Windows"),
        (PlanFlags.PathTooLong, "the destination path exceeds 259 characters"),
        (PlanFlags.DestinationConflictsWithSource, "the destination is a current source file"),
        (PlanFlags.DestinationExists, "something already exists at the destination"),
        (PlanFlags.InconsistentDiscSet, "files of one disc set disagree on identity, or a track is shared by two sets"),
        (PlanFlags.SheetNeedsReferenceRewrite, "the sheet references files by a path, so moving would require rewriting it"),
        (PlanFlags.IncompleteDiscSet, "the sheet is unreadable, lists no files, or references a missing file"),
        (PlanFlags.PartiallyVerifiedSet, "not every file of the disc set is content-verified")
    ];

    public PlanStatus Status { get; } = status;

    public string OutputRoot { get; } = outputRoot;

    public IReadOnlyList<PlanItem> Items { get; } = items;

    /// <summary>Plan-level problems; a rejected plan has no items.</summary>
    public IReadOnlyList<string> Issues { get; } = issues;

    public string ToReport()
    {
        StringBuilder text = new();
        text.AppendLine("Organization preview (no files were changed)");
        text.AppendLine("Output root: " + OutputRoot);
        text.AppendLine("Status: " + Status);

        foreach (string issue in Issues)
        {
            text.AppendLine("Issue: " + issue);
        }

        text.AppendLine($"Items: {Items.Count}; safe to apply: {Items.Count(i => i.IsSafeToApply)}; platform folders: {Items.Count(i => i.Bucket == PlanBucket.Platform)}; review: {Items.Count(i => i.Bucket == PlanBucket.Review)}");

        foreach (IGrouping<DatMatchStatus, PlanItem> group in Items.GroupBy(i => i.MatchStatus).OrderBy(g => g.Key))
        {
            text.AppendLine($"  {group.Key}: {group.Count()}");
        }

        foreach (PlanItem item in Items)
        {
            text.AppendLine();
            text.AppendLine($"[{item.MatchStatus}] {item.SourceRelativePath}");
            text.AppendLine($"  proposed: {item.DestinationRelativePath} ({(item.IsSafeToApply ? "safe to apply" : item.Bucket == PlanBucket.Review ? "needs review" : "not safe to apply")})");

            if (item.ReviewReason is not null)
            {
                text.AppendLine("  review reason: " + item.ReviewReason);
            }

            if (item.GameName is not null)
            {
                string detail = string.Join(" | ", new[]
                {
                    item.DatName is null ? null : "DAT '" + item.DatName + "'",
                    "game '" + item.GameName + "'",
                    item.Platform is null ? "platform unknown" : "platform '" + item.Platform + "'",
                    item.Region is null ? null : "region " + item.Region,
                    item.Version is null ? null : "version " + item.Version
                }.Where(part => part is not null));
                text.AppendLine("  catalog: " + detail);
            }

            if (item.Evidence is { Count: > 0 })
            {
                text.AppendLine("  evidence: " + string.Join(", ", item.Evidence));
            }

            if (item.Reasons.Count > 0)
            {
                text.AppendLine("  reasons: " + string.Join(", ", item.Reasons));
            }

            if (item.DiscSetId is not null)
            {
                text.AppendLine("  disc set: " + item.DiscSetId);
            }

            foreach ((PlanFlags flag, string meaning) in FlagMeanings)
            {
                if (item.Flags.HasFlag(flag))
                {
                    text.AppendLine($"  flag {flag}: {meaning}");
                }
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// Builds a deterministic, read-only organization proposal from the library index and
/// DAT match results. It reads only small CUE/GDI sheet files (bounded) to keep disc
/// sets together, checks destinations with existence tests only, and never writes,
/// moves, renames or deletes anything. Files keep their names and a sheet's references
/// stay valid only when every referenced file sits beside it and moves with it; any other
/// layout is flagged for review rather than proposed as executable. Only VerifiedMatch
/// items with no flags are <see cref="PlanItem.IsSafeToApply"/>.
/// </summary>
public static class OrganizationPlanner
{
    public const string ReviewFolder = "_Review";
    private const int MaxSheetBytes = 1024 * 1024;
    private const int MaxSheetLines = 8192;
    private const int MaxPathLength = 259;

    // Most severe first. A disc set shares the most severe reason of any of its members.
    private static readonly string[] ReviewPriority =
    [
        "NeedsReferenceRewrite", "IncompleteDiscSet", "InconsistentDiscSet", "Mismatch", "Ambiguous",
        "Error", "Unsupported", "InvalidName", "UnknownPlatform", "Duplicates", "Unknown"
    ];

    public static OrganizationPlan Plan(
        string libraryRoot,
        string outputRoot,
        IReadOnlyList<LibraryIndexEntry> entries,
        IReadOnlyList<DatMatchResult> matches)
    {
        string? rejection = ValidateOutputRoot(outputRoot, out string fullOutput);

        if (rejection is not null)
        {
            return new OrganizationPlan(PlanStatus.Rejected, outputRoot, [], [rejection]);
        }

        string root = Path.GetFullPath(libraryRoot);
        string? overlap = ValidateOutputOutsideLibrary(root, fullOutput);

        if (overlap is not null)
        {
            return new OrganizationPlan(PlanStatus.Rejected, outputRoot, [], [overlap]);
        }

        var matchByPath = matches
            .GroupBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        List<Draft> drafts = entries
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.RelativePath, StringComparer.Ordinal)
            .Select(e => new Draft(
                e,
                matchByPath.TryGetValue(e.RelativePath, out DatMatchResult? m)
                    ? m
                    : new DatMatchResult(e.RelativePath, DatMatchStatus.NotFound, null, null, null, null, null, null, [], ["no-match-result"], [])))
            .ToList();

        Dictionary<string, List<Draft>> sets = ApplyDiscSets(root, drafts);
        MarkDuplicates(drafts);

        foreach (Draft draft in drafts)
        {
            Route(draft);
        }

        KeepSetsTogether(sets);
        FlagCollisionsAndConflicts(root, fullOutput, drafts);

        List<PlanItem> items = drafts
            .Select(d => new PlanItem(
                d.Entry.RelativePath,
                d.Destination,
                d.Bucket,
                d.Match.Status,
                d.Match.Platform,
                d.Match.GameName,
                d.SetId,
                d.Flags,
                d.Reasons,
                d.Match.DatName,
                d.Match.Region,
                d.Match.Version,
                d.Match.Evidence,
                d.ReviewReason))
            .ToList();

        return new OrganizationPlan(PlanStatus.Ready, fullOutput, items, []);
    }

    private sealed class Draft(LibraryIndexEntry entry, DatMatchResult match)
    {
        public LibraryIndexEntry Entry { get; } = entry;

        public DatMatchResult Match { get; set; } = match;

        public string? SetId { get; set; }

        public PlanFlags Flags { get; set; }

        public List<string> Reasons { get; } = [.. match.Reasons];

        public string? ReviewReason { get; set; }

        public bool IsDuplicateLoser { get; set; }

        public PlanBucket Bucket { get; set; }

        public string Destination { get; set; } = string.Empty;
    }

    // The output must be a separate tree: organizing into the library itself would mix
    // destinations with the sources being organized. Compared by path components, not prefix.
    private static string? ValidateOutputOutsideLibrary(string libraryRoot, string fullOutput)
    {
        try
        {
            if (Directory.Exists(libraryRoot) && !InputResolver.IsSafeExistingPath(libraryRoot))
            {
                return "Library root is or sits under a reparse point, so its location cannot be compared safely.";
            }

            if (LibPathRelation.IsSameOrInside(libraryRoot, fullOutput))
            {
                return "Output root must not be, or sit inside, the library root.";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return "Library or output path could not be compared: " + ex.Message;
        }

        return null;
    }

    private static string? ValidateOutputRoot(string outputRoot, out string fullOutput)
    {
        fullOutput = outputRoot;

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return "Output root is empty.";
        }

        if (!Path.IsPathFullyQualified(outputRoot)
            || outputRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
        {
            return "Output root must be an absolute path without '..' segments.";
        }

        try
        {
            fullOutput = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Output root is not a valid path: " + ex.Message;
        }

        string? pathRoot = Path.GetPathRoot(fullOutput);

        if (pathRoot is null
            || string.Equals(fullOutput + Path.DirectorySeparatorChar, pathRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullOutput, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "Output root must not be a volume root.";
        }

        if (fullOutput.Split(Path.DirectorySeparatorChar).Any(PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName))
        {
            return "Output root must not be, or sit inside, an app workspace folder.";
        }

        // The nearest existing ancestor must not be, or sit under, a reparse point.
        string? existing = fullOutput;

        while (existing is not null && !Directory.Exists(existing) && !File.Exists(existing))
        {
            existing = Path.GetDirectoryName(existing);
        }

        if (existing is null || File.Exists(existing) && !Directory.Exists(existing) || !InputResolver.IsSafeExistingPath(existing))
        {
            return "Output root is missing a safe existing ancestor, is a file, or sits under a reparse point.";
        }

        return null;
    }

    // Keeps each CUE/GDI sheet together with the files it references. Moving a set to one
    // folder under unchanged names keeps every reference valid; anything else is flagged.
    private static Dictionary<string, List<Draft>> ApplyDiscSets(string root, List<Draft> drafts)
    {
        Dictionary<string, List<Draft>> sets = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Draft> byFullPath = IndexByFullPath(root, drafts, out HashSet<string> ambiguous);

        foreach (Draft sheet in drafts.Where(d => d.Entry.Kind is MediaInputKind.CUE or MediaInputKind.GDI).ToList())
        {
            string sheetPath = Path.GetFullPath(Path.Combine(root, sheet.Entry.RelativePath));
            string directory = Path.GetDirectoryName(sheetPath)!;
            string setId = Path.GetRelativePath(root, sheetPath);
            List<Draft> members = [sheet];
            bool needsRewrite = false;
            bool incomplete = false;

            IReadOnlyList<string>? references = ReadSheetReferences(sheetPath, sheet.Entry.Kind);

            if (references is null || references.Count == 0)
            {
                incomplete = true;
            }
            else
            {
                foreach (string reference in references)
                {
                    if (DatNames.IsUnsafeFileName(reference))
                    {
                        // A directory component, traversal or invalid name: the sheet would need rewriting.
                        // The file is still a member of the set, so it is never moved on its own.
                        needsRewrite = true;

                        if (TryResolveInsideRoot(root, directory, reference, out string resolved)
                            && ambiguous.Contains(resolved))
                        {
                            incomplete = true;
                            sheet.Reasons.Add("case-ambiguous-reference");
                        }
                        else if (TryResolveInsideRoot(root, directory, reference, out resolved)
                            && byFullPath.TryGetValue(resolved, out Draft? nested)
                            && !members.Contains(nested))
                        {
                            members.Add(nested);
                        }
                    }
                    else if (ambiguous.Contains(Path.Combine(directory, reference)))
                    {
                        incomplete = true;
                        sheet.Reasons.Add("case-ambiguous-reference");
                    }
                    else if (byFullPath.TryGetValue(Path.Combine(directory, reference), out Draft? track))
                    {
                        if (!members.Contains(track))
                        {
                            members.Add(track);
                        }
                    }
                    else
                    {
                        incomplete = true;
                    }
                }
            }

            List<Draft> claimed = [];

            foreach (Draft member in members)
            {
                if (member.SetId is not null && !string.Equals(member.SetId, setId, StringComparison.OrdinalIgnoreCase))
                {
                    // A file claimed by two sheets: both sets are inconsistent.
                    member.Flags |= PlanFlags.InconsistentDiscSet;
                    claimed.AddRange(sets[member.SetId]);
                    continue;
                }

                member.SetId = setId;
            }

            foreach (Draft other in claimed)
            {
                other.Flags |= PlanFlags.InconsistentDiscSet;
            }

            if (claimed.Count > 0)
            {
                foreach (Draft member in members)
                {
                    member.Flags |= PlanFlags.InconsistentDiscSet;
                }
            }

            sets[setId] = members;

            if (needsRewrite)
            {
                foreach (Draft member in members)
                {
                    member.Flags |= PlanFlags.SheetNeedsReferenceRewrite;
                }
            }

            if (incomplete)
            {
                foreach (Draft member in members)
                {
                    member.Flags |= PlanFlags.IncompleteDiscSet;
                }
            }

            InheritSetIdentity(members);
        }

        return sets;
    }

    // Entries that differ only by case (case-sensitive folders) must not abort planning, but a
    // sheet reference that could mean either of them must not silently pick one: such keys
    // are reported in `ambiguous` and the caller fails closed.
    private static Dictionary<string, Draft> IndexByFullPath(string root, List<Draft> drafts) =>
        IndexByFullPath(root, drafts, out _);

    private static Dictionary<string, Draft> IndexByFullPath(string root, List<Draft> drafts, out HashSet<string> ambiguous)
    {
        Dictionary<string, Draft> index = new(StringComparer.OrdinalIgnoreCase);
        ambiguous = new(StringComparer.OrdinalIgnoreCase);

        foreach (Draft draft in drafts)
        {
            if (!index.TryAdd(Path.GetFullPath(Path.Combine(root, draft.Entry.RelativePath)), draft))
            {
                ambiguous.Add(Path.GetFullPath(Path.Combine(root, draft.Entry.RelativePath)));
            }
        }

        return index;
    }

    private static bool TryResolveInsideRoot(string root, string directory, string reference, out string resolved)
    {
        resolved = string.Empty;

        try
        {
            string full = Path.GetFullPath(Path.Combine(directory, reference.Replace('/', Path.DirectorySeparatorChar)));

            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            resolved = full;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void InheritSetIdentity(List<Draft> members)
    {
        bool Identified(Draft m) => m.Match.Status is DatMatchStatus.VerifiedMatch or DatMatchStatus.CandidateMatch;

        var identities = members.Where(Identified).Select(m => (m.Match.Platform, m.Match.GameName)).Distinct().ToList();

        if (identities.Count > 1)
        {
            foreach (Draft member in members)
            {
                member.Flags |= PlanFlags.InconsistentDiscSet;
            }
        }
        else if (identities.Count == 1)
        {
            Draft donor = members.First(Identified);

            foreach (Draft member in members.Where(m => m.Match.Status is DatMatchStatus.NotFound or DatMatchStatus.Unsupported))
            {
                // A file without its own record follows its sheet's game but stays unverified.
                member.Match = member.Match with
                {
                    Status = DatMatchStatus.CandidateMatch,
                    Platform = donor.Match.Platform,
                    DatName = donor.Match.DatName,
                    GameName = donor.Match.GameName,
                    Region = donor.Match.Region,
                    Version = donor.Match.Version
                };
                member.Reasons.Add("inherited-from-disc-set");
            }
        }

        if (members.Any(m => m.Match.Status != DatMatchStatus.VerifiedMatch))
        {
            foreach (Draft member in members)
            {
                member.Flags |= PlanFlags.PartiallyVerifiedSet;
            }
        }
    }

    // Returns null when the sheet cannot be read within bounds. CUE parsing reuses the app's
    // CueSheetFileStatementReader; GDI uses the same rule as the archive dependency
    // validator (quoted name, else the fifth column), which lives in Services and so is
    // mirrored here rather than referenced from Core.
    private static IReadOnlyList<string>? ReadSheetReferences(string sheetPath, MediaInputKind kind)
    {
        List<string> names = [];

        try
        {
            // Size is checked on the open handle and the read is capped, so a file that grows
            // or is swapped after the check cannot be pulled into memory unbounded.
            byte[] buffer = new byte[MaxSheetBytes + 1];
            int total = 0;

            using (FileStream stream = new(sheetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length == 0 || stream.Length > MaxSheetBytes)
                {
                    return null;
                }

                int read;

                while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
                {
                    total += read;
                }
            }

            if (total == 0 || total > MaxSheetBytes)
            {
                return null;
            }

            List<string> lines = [];

            using (StreamReader reader = new(new MemoryStream(buffer, 0, total), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                while (reader.ReadLine() is { } line)
                {
                    if (lines.Count >= MaxSheetLines)
                    {
                        return null;
                    }

                    lines.Add(line);
                }
            }

            foreach (string raw in lines)
            {
                if (kind == MediaInputKind.CUE)
                {
                    if (CueSheetFileStatementReader.TryRead(raw, requireFileType: true, out string reference, out bool hasFile))
                    {
                        names.Add(reference);
                    }
                    else if (hasFile)
                    {
                        return null;
                    }

                    continue;
                }

                string line = raw.Trim();

                if (line.Length == 0 || !char.IsDigit(line[0]))
                {
                    continue;
                }

                int open = line.IndexOf('"');
                int close = open >= 0 ? line.IndexOf('"', open + 1) : -1;

                if (open >= 0 && close > open)
                {
                    names.Add(line[(open + 1)..close]);
                    continue;
                }

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 5)
                {
                    names.Add(parts[4]);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        return names;
    }

    private static void MarkDuplicates(List<Draft> drafts)
    {
        var groups = drafts
            .Where(d => d.Match.Status is DatMatchStatus.VerifiedMatch or DatMatchStatus.CandidateMatch && d.Match.GameName is not null)
            .GroupBy(d => (d.Match.DatName, d.Match.GameName, FileName: d.Entry.FileName.ToUpperInvariant()));

        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            // Drafts are already path-ordered, so the first copy stays put; the rest go to review.
            bool first = true;

            foreach (Draft draft in group)
            {
                draft.Flags |= PlanFlags.DuplicateCandidate;
                draft.IsDuplicateLoser = !first;
                first = false;
            }
        }
    }

    private static void Route(Draft draft)
    {
        DatMatchResult match = draft.Match;

        if (match.Status == DatMatchStatus.CandidateMatch)
        {
            draft.Flags |= PlanFlags.UnverifiedIdentity;
        }

        if (match.Status == DatMatchStatus.Unsupported)
        {
            draft.Flags |= PlanFlags.ContentUnverifiable;
        }

        string? reviewReason = match.Status switch
        {
            DatMatchStatus.VerifiedMatch or DatMatchStatus.CandidateMatch => null,
            DatMatchStatus.NotFound => "Unknown",
            DatMatchStatus.Ambiguous => "Ambiguous",
            DatMatchStatus.Mismatch => "Mismatch",
            DatMatchStatus.Unsupported => "Unsupported",
            _ => "Error"
        };

        if (reviewReason is null && string.IsNullOrWhiteSpace(match.Platform))
        {
            draft.Flags |= PlanFlags.UnknownPlatform;
            reviewReason = "UnknownPlatform";
        }

        if (reviewReason is null && draft.IsDuplicateLoser)
        {
            reviewReason = "Duplicates";
        }

        reviewReason = draft.Flags switch
        {
            var f when f.HasFlag(PlanFlags.SheetNeedsReferenceRewrite) => "NeedsReferenceRewrite",
            var f when f.HasFlag(PlanFlags.IncompleteDiscSet) => "IncompleteDiscSet",
            var f when f.HasFlag(PlanFlags.InconsistentDiscSet) => "InconsistentDiscSet",
            _ => reviewReason
        };

        string? platformFolder = null;
        string? gameFolder = null;

        if (reviewReason is null)
        {
            platformFolder = match.Platform!.Trim();
            gameFolder = draft.SetId is not null ? match.GameName : null;

            if (DatNames.IsUnsafeFileName(platformFolder)
                || gameFolder is not null && DatNames.IsUnsafeFileName(gameFolder))
            {
                draft.Flags |= PlanFlags.InvalidDestinationName;
                reviewReason = "InvalidName";
            }
        }

        if (reviewReason is not null)
        {
            Place(draft, reviewReason);
        }
        else
        {
            draft.Bucket = PlanBucket.Platform;
            draft.Destination = gameFolder is null
                ? Path.Combine(platformFolder!, draft.Entry.FileName)
                : Path.Combine(platformFolder!, gameFolder, draft.Entry.FileName);
        }

    }

    private static void Place(Draft draft, string reviewReason)
    {
        draft.Bucket = PlanBucket.Review;
        draft.ReviewReason = reviewReason;
        draft.Destination = Path.Combine(ReviewFolder, reviewReason, draft.Entry.RelativePath);
    }

    // A disc set moves as a unit: if any member needs review, every member goes to review
    // under the same, most severe reason, keeping its original relative path.
    private static void KeepSetsTogether(Dictionary<string, List<Draft>> sets)
    {
        foreach (List<Draft> members in sets.Values)
        {
            if (members.All(m => m.Bucket == PlanBucket.Platform))
            {
                continue;
            }

            string reason = members
                .Where(m => m.ReviewReason is not null)
                .Select(m => m.ReviewReason!)
                .OrderBy(r => Array.IndexOf(ReviewPriority, r) is var i && i < 0 ? int.MaxValue : i)
                .First();

            foreach (Draft member in members)
            {
                Place(member, reason);
            }
        }
    }

    private static void FlagCollisionsAndConflicts(string libraryRoot, string outputRoot, List<Draft> drafts)
    {
        Dictionary<string, Draft> sources = IndexByFullPath(libraryRoot, drafts);

        foreach (Draft draft in drafts)
        {
            string full = Path.Combine(outputRoot, draft.Destination);

            if (full.Length > MaxPathLength)
            {
                draft.Flags |= PlanFlags.PathTooLong;
            }

            if (sources.ContainsKey(full))
            {
                draft.Flags |= PlanFlags.DestinationConflictsWithSource;
            }

            if (File.Exists(full) || Directory.Exists(full))
            {
                draft.Flags |= PlanFlags.DestinationExists;
            }
        }

        foreach (var group in drafts.GroupBy(d => Path.Combine(outputRoot, d.Destination), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (Draft draft in group)
            {
                draft.Flags |= PlanFlags.DestinationCollision;
            }
        }
    }
}
