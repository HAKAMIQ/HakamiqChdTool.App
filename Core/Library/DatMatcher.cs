using HakamiqChdTool.App.Core.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Library;

public enum DatMatchStatus
{
    /// <summary>A streamed content hash equalled a catalog record (and size did not conflict).</summary>
    VerifiedMatch,

    /// <summary>Name and optional size evidence only. Identity is not verified.</summary>
    CandidateMatch,

    /// <summary>More than one catalog game plausibly fits.</summary>
    Ambiguous,

    /// <summary>Compared evidence conflicts with the catalog record.</summary>
    Mismatch,

    NotFound,

    /// <summary>The media representation cannot be reliably evaluated by this method.</summary>
    Unsupported,

    Error
}

public enum DatEvidence
{
    NameExact,
    SizeEqual,
    Sha1Equal,
    Md5Equal,
    Crc32Equal
}

public enum DatHashKind
{
    Sha1,
    Md5,
    Crc32
}

/// <summary>Streams a file and returns its lowercase hex hash. Supplied by the caller so Core stays free of Services.</summary>
public delegate Task<string> DatContentHasher(string path, DatHashKind kind, CancellationToken cancellationToken);

public sealed record DatMatchOptions(bool VerifyContent = false);

public sealed record DatMatchResult(
    string RelativePath,
    DatMatchStatus Status,
    string? DatName,
    string? Platform,
    string? GameName,
    string? RomName,
    string? Region,
    string? Version,
    IReadOnlyList<DatEvidence> Evidence,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> AlternativeGames);

public enum DatMatchOutcome
{
    Completed,
    Cancelled
}

public sealed class DatMatchReport(
    DatMatchOutcome outcome,
    IReadOnlyList<DatMatchResult> results)
{
    public DatMatchOutcome Outcome { get; } = outcome;

    public IReadOnlyList<DatMatchResult> Results { get; } = results;

    public int Count(DatMatchStatus status) => Results.Count(r => r.Status == status);
}

/// <summary>
/// Matches library-index entries against user-supplied DATs. By default it only compares
/// names and sizes (CandidateMatch at best). Content hashing happens only when
/// <see cref="DatMatchOptions.VerifyContent"/> is set, and only for files whose bytes are
/// the thing a DAT hashes. CHD, CSO, PKG and archives are never hashed or compared
/// against raw-image hashes. Nothing is written, moved or deleted.
/// </summary>
public sealed class DatMatcher
{
    private sealed record Hit(int DatIndex, DatGame Game, DatRom Rom);

    private static readonly HashSet<string> ContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".gz", ".tar", ".rvz", ".wia", ".gcz", ".wbfs", ".nkit", ".ciso", ".zso"
    };

    private readonly IReadOnlyList<DatFile> dats;
    private readonly DatContentHasher? hasher;
    private readonly Dictionary<string, List<Hit>> byRomName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Hit>> byGameName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Hit>> bySha1 = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Hit>> byMd5 = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Hit>> byCrc = new(StringComparer.Ordinal);

    public DatMatcher(IReadOnlyList<DatFile> dats, DatContentHasher? hasher = null)
    {
        this.dats = dats;
        this.hasher = hasher;

        for (int d = 0; d < dats.Count; d++)
        {
            foreach (DatGame game in dats[d].Games)
            {
                foreach (DatRom rom in game.Roms)
                {
                    Hit hit = new(d, game, rom);
                    if (rom.Name.Length > 0)
                    {
                        Add(byRomName, rom.Name, hit);
                    }

                    Add(byGameName, game.Name, hit);

                    if (rom.Crc32 is not null)
                    {
                        Add(byCrc, rom.Crc32, hit);
                    }

                    if (rom.Sha1 is not null)
                    {
                        Add(bySha1, rom.Sha1, hit);
                    }

                    if (rom.Md5 is not null)
                    {
                        Add(byMd5, rom.Md5, hit);
                    }
                }
            }
        }
    }

    public async Task<DatMatchReport> MatchAsync(
        string libraryRoot,
        IReadOnlyList<LibraryIndexEntry> entries,
        DatMatchOptions options,
        CancellationToken cancellationToken)
    {
        List<DatMatchResult> results = [];
        DatHashKind? hashKind = bySha1.Count > 0
            ? DatHashKind.Sha1
            : byMd5.Count > 0
                ? DatHashKind.Md5
                : byCrc.Count > 0 ? DatHashKind.Crc32 : null;

        string root = Path.GetFullPath(libraryRoot);

        try
        {
            foreach (LibraryIndexEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await MatchEntryAsync(root, entry, options, hashKind, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException)
        {
            return new DatMatchReport(DatMatchOutcome.Cancelled, results);
        }

        return new DatMatchReport(DatMatchOutcome.Completed, results);
    }

    private async Task<DatMatchResult> MatchEntryAsync(
        string root,
        LibraryIndexEntry entry,
        DatMatchOptions options,
        DatHashKind? hashKind,
        CancellationToken cancellationToken)
    {
        string stem = Path.GetFileNameWithoutExtension(entry.FileName);
        bool container = entry.Kind is MediaInputKind.CHD or MediaInputKind.CSO or MediaInputKind.PKG
            || (entry.Extension is not null && ContainerExtensions.Contains(entry.Extension));

        // A container is named after its game; a raw file is only named like one of the game's own ROM records.
        List<Hit> nameHits = Distinct(
            container
                ? Lookup(byRomName, entry.FileName).Concat(Lookup(byGameName, stem))
                : Lookup(byRomName, entry.FileName));

        if (container)
        {
            return MatchContainer(entry, nameHits);
        }

        // Only plain image and ROM files are size-comparable; text sheets vary by line ending.
        bool sizeComparable = entry.Kind is MediaInputKind.BIN or MediaInputKind.ISO or MediaInputKind.Other
            && entry.SizeBytes is not null;
        List<string> reasons = [];

        if (options.VerifyContent)
        {
            if (hasher is null || hashKind is null)
            {
                reasons.Add(hasher is null ? "verification-unavailable-no-hasher" : "verification-unavailable-no-hashes-in-catalog");
            }
            else
            {
                return await VerifyAsync(root, entry, nameHits, sizeComparable, hashKind.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        return MatchByName(entry, nameHits, sizeComparable, reasons);
    }

    private DatMatchResult MatchContainer(LibraryIndexEntry entry, List<Hit> nameHits)
    {
        if (nameHits.Count == 0)
        {
            return Result(entry, DatMatchStatus.Unsupported, null, [], ["container-content-not-comparable"]);
        }

        List<string> reasons = ["container-name-only", "content-verification-unsupported"];
        return Decide(entry, nameHits, [DatEvidence.NameExact], reasons, DatMatchStatus.CandidateMatch);
    }

    private DatMatchResult MatchByName(
        LibraryIndexEntry entry,
        List<Hit> nameHits,
        bool sizeComparable,
        List<string> reasons)
    {
        if (nameHits.Count == 0)
        {
            reasons.Add("no-name-candidate");
            return Result(entry, DatMatchStatus.NotFound, null, [], reasons);
        }

        List<DatEvidence> evidence = [DatEvidence.NameExact];
        List<Hit> usable = nameHits;

        if (sizeComparable)
        {
            long size = entry.SizeBytes!.Value;
            List<Hit> sized = nameHits.Where(h => h.Rom.Size == size).ToList();
            List<Hit> unsized = nameHits.Where(h => h.Rom.Size is null).ToList();

            if (sized.Count > 0)
            {
                usable = sized;
                evidence.Add(DatEvidence.SizeEqual);
            }
            else if (unsized.Count > 0)
            {
                usable = unsized;
                reasons.Add("catalog-size-missing");
            }
            else
            {
                reasons.Add("size-conflict");
                return Result(entry, DatMatchStatus.Mismatch, nameHits[0], evidence, reasons);
            }
        }

        reasons.Add("content-not-verified");
        return Decide(entry, usable, evidence, reasons, DatMatchStatus.CandidateMatch);
    }

    private async Task<DatMatchResult> VerifyAsync(
        string root,
        LibraryIndexEntry entry,
        List<Hit> nameHits,
        bool sizeComparable,
        DatHashKind kind,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(Path.Combine(root, entry.RelativePath));

        if (!fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Result(entry, DatMatchStatus.Error, null, [], ["path-outside-library-root"]);
        }

        string hash;

        try
        {
            hash = (await hasher!(fullPath, kind, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return Result(entry, DatMatchStatus.Error, null, [], ["hash-read-failed"]);
        }

        Dictionary<string, List<Hit>> index = kind switch
        {
            DatHashKind.Sha1 => bySha1,
            DatHashKind.Md5 => byMd5,
            _ => byCrc
        };
        List<Hit> hashHits = Distinct(Lookup(index, hash));
        DatEvidence hashEvidence = kind switch
        {
            DatHashKind.Sha1 => DatEvidence.Sha1Equal,
            DatHashKind.Md5 => DatEvidence.Md5Equal,
            _ => DatEvidence.Crc32Equal
        };

        // CRC32 is only 32 bits: it identifies a file when the catalog size agrees too, and
        // a CRC-only or size-less match stays a candidate.
        bool weakHash = kind == DatHashKind.Crc32;

        if (weakHash && sizeComparable)
        {
            long entrySize = entry.SizeBytes!.Value;
            List<Hit> sameSize = hashHits.Where(h => h.Rom.Size == entrySize).ToList();
            List<Hit> noSize = hashHits.Where(h => h.Rom.Size is null).ToList();
            hashHits = sameSize.Count > 0 ? sameSize : noSize;
        }

        if (hashHits.Count == 0)
        {
            // Name-only candidates that carry this hash kind contradict the content.
            bool conflict = nameHits.Any(h => (kind switch
            {
                DatHashKind.Sha1 => h.Rom.Sha1,
                DatHashKind.Md5 => h.Rom.Md5,
                _ => h.Rom.Crc32
            }) is not null);
            if (conflict)
            {
                return Result(entry, DatMatchStatus.Mismatch, nameHits[0], [DatEvidence.NameExact], ["name-matches-hash-differs"]);
            }

            // Name records that carry no hash cannot contradict or confirm the content.
            return nameHits.Count > 0
                ? MatchByName(entry, nameHits, sizeComparable, ["catalog-record-lacks-hash"])
                : Result(entry, DatMatchStatus.NotFound, null, [], ["hash-not-in-catalog"]);
        }

        List<DatEvidence> evidence = [hashEvidence];
        List<string> reasons = [weakHash ? "crc32-identification" : "content-hash-verified"];
        bool sizeAgrees = false;

        if (sizeComparable)
        {
            long size = entry.SizeBytes!.Value;

            if (hashHits.All(h => h.Rom.Size is not null && h.Rom.Size != size))
            {
                return Result(entry, DatMatchStatus.Mismatch, hashHits[0], [hashEvidence], ["hash-equal-size-conflict"]);
            }

            if (hashHits.Any(h => h.Rom.Size == size))
            {
                evidence.Add(DatEvidence.SizeEqual);
                sizeAgrees = true;
            }
        }

        DatMatchStatus single = DatMatchStatus.VerifiedMatch;

        if (weakHash && !sizeAgrees)
        {
            single = DatMatchStatus.CandidateMatch;
            reasons.Add(sizeComparable ? "crc32-without-catalog-size" : "crc32-size-not-comparable");
        }

        if (nameHits.Any(n => hashHits.Any(h => h.Game.Name == n.Game.Name && h.DatIndex == n.DatIndex)))
        {
            evidence.Add(DatEvidence.NameExact);
        }
        else
        {
            reasons.Add("filename-differs-from-catalog");
        }

        return Decide(entry, hashHits, evidence, reasons, single);
    }

    private DatMatchResult Decide(
        LibraryIndexEntry entry,
        List<Hit> hits,
        List<DatEvidence> evidence,
        List<string> reasons,
        DatMatchStatus singleStatus)
    {
        var games = hits
            .GroupBy(h => (h.DatIndex, h.Game.Name))
            .Select(g => g.First())
            .OrderBy(h => h.DatIndex)
            .ThenBy(h => h.Game.Name, StringComparer.Ordinal)
            .ToList();

        if (games.Count > 1)
        {
            reasons.Add("multiple-catalog-games");
            return Result(
                entry,
                DatMatchStatus.Ambiguous,
                null,
                evidence,
                reasons,
                games.Select(h => h.Game.Name).ToList());
        }

        return Result(entry, singleStatus, hits.OrderBy(h => h.Rom.Name, StringComparer.Ordinal).First(), evidence, reasons);
    }

    private DatMatchResult Result(
        LibraryIndexEntry entry,
        DatMatchStatus status,
        Hit? hit,
        IReadOnlyList<DatEvidence> evidence,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string>? alternatives = null)
    {
        DatFile? dat = hit is null ? null : dats[hit.DatIndex];

        return new DatMatchResult(
            entry.RelativePath,
            status,
            dat is null ? null : (dat.Header.Name ?? Path.GetFileName(dat.SourcePath)),
            dat?.Platform,
            hit?.Game.Name,
            hit?.Rom.Name,
            hit?.Game.Region,
            hit?.Game.Version,
            evidence,
            reasons,
            alternatives ?? []);
    }

    private static void Add(Dictionary<string, List<Hit>> index, string key, Hit hit)
    {
        if (!index.TryGetValue(key, out List<Hit>? list))
        {
            index[key] = list = [];
        }

        list.Add(hit);
    }

    private static List<Hit> Lookup(Dictionary<string, List<Hit>> index, string key) =>
        index.TryGetValue(key, out List<Hit>? list) ? list : [];

    private static List<Hit> Distinct(IEnumerable<Hit> hits) =>
        hits.GroupBy(h => (h.DatIndex, h.Game.Name, h.Rom.Name)).Select(g => g.First()).ToList();
}
