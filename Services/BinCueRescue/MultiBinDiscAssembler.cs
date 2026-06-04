using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal static class MultiBinDiscAssembler
{
    private const long MaximumCueProbeBytes = 1024 * 1024;
    private const int RegexTimeoutMilliseconds = 250;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds);
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly Regex CueFileLineRegex = new(
        @"^\s*FILE\s+(?:""(?<quoted>[^""]+)""|(?<plain>\S+))\s+\S+\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline,
        RegexTimeout);

    private static readonly Regex CueFileKeywordRegex = new(
        @"^\s*FILE\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline,
        RegexTimeout);

    private static readonly Regex DiscPartNumberRegex = new(
        @"(?:^|[\s_\-\.\(\[])(?:disc|disk|cd|dvd|side|part)\s*0*\d{1,3}(?:$|[\s_\-\.\)\]])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    public static BinCueRescuePlan AssembleForBin(
        string binPath,
        string? leaderCueWriteTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binPath);

        string fullBinPath;
        try
        {
            fullBinPath = NormalizeFullPath(binPath);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return Refuse(
                leaderCueWriteTarget,
                [BinCueRescueRefusalReason.InsufficientSectorEvidence]);
        }

        FileInfo selectedBin = new(fullBinPath);
        if (!IsSafeExistingReferencedFile(selectedBin.FullName))
        {
            return Refuse(
                leaderCueWriteTarget,
                [BinCueRescueRefusalReason.InsufficientSectorEvidence]);
        }

        string? adjacentCue = FindAdjacentCueForBin(selectedBin.FullName);
        if (!string.IsNullOrWhiteSpace(adjacentCue))
        {
            return new BinCueRescuePlan(
                BinCueRescueDecision.UseAdjacentCue,
                adjacentCue,
                null,
                Array.Empty<BinCueRescueTrackPlan>(),
                null,
                false,
                Array.Empty<BinCueRescueRefusalReason>(),
                Array.Empty<BinCueRescueWarningCode>());
        }

        IReadOnlyList<FileInfo> orderedBins = FindOrderedCandidateBins(selectedBin, out bool ambiguousOrder);
        if (ambiguousOrder)
        {
            return Refuse(
                leaderCueWriteTarget,
                [BinCueRescueRefusalReason.AmbiguousOrder]);
        }

        if (orderedBins.Count == 0)
        {
            return Refuse(
                leaderCueWriteTarget,
                [BinCueRescueRefusalReason.InsufficientSectorEvidence]);
        }

        List<BinSectorProbeResult> probes = [];
        foreach (FileInfo bin in orderedBins)
        {
            probes.Add(BinSectorProbe.Probe(bin.FullName));
        }

        List<BinCueRescueRefusalReason> refusals = [];
        List<BinCueRescueWarningCode> warnings = [];

        if (probes.Any(probe => probe.Kind == BinTrackKind.Cooked2048Data))
        {
            refusals.Add(BinCueRescueRefusalReason.Cooked2048ShouldBeIso);
        }

        if (probes.Any(probe => probe.Kind == BinTrackKind.NonStandard))
        {
            refusals.Add(BinCueRescueRefusalReason.NonStandardSectorLayout);
        }

        if (probes.Any(probe => probe.Kind == BinTrackKind.Unknown))
        {
            refusals.Add(BinCueRescueRefusalReason.InsufficientSectorEvidence);
        }

        int dataTrackCount = probes.Count(probe => probe.Kind is BinTrackKind.Raw2352Mode1 or BinTrackKind.Raw2352Mode2);
        if (dataTrackCount == 0)
        {
            refusals.Add(BinCueRescueRefusalReason.NoSyncProof);
        }

        if (dataTrackCount > 1)
        {
            refusals.Add(BinCueRescueRefusalReason.MultipleDataTracksConflict);
        }

        int firstDataIndex = probes.FindIndex(probe => probe.Kind is BinTrackKind.Raw2352Mode1 or BinTrackKind.Raw2352Mode2);
        if (firstDataIndex > 0)
        {
            refusals.Add(BinCueRescueRefusalReason.AmbiguousOrder);
        }

        if (orderedBins.Count > 1 && probes.Skip(1).Any(probe => probe.Kind is not BinTrackKind.Raw2352AudioCandidate))
        {
            refusals.Add(BinCueRescueRefusalReason.MixedSectorSizes);
        }

        List<BinCueRescueTrackPlan> trackPlans = [];
        for (int i = 0; i < orderedBins.Count; i++)
        {
            BinSectorProbeResult probe = probes[i];
            string cueTrackMode = ToCueTrackMode(probe.Kind);

            trackPlans.Add(
                new BinCueRescueTrackPlan(
                    i + 1,
                    orderedBins[i].FullName,
                    probe.Kind,
                    cueTrackMode,
                    probe.Kind is BinTrackKind.Raw2352Mode1 or BinTrackKind.Raw2352Mode2,
                    probe.Kind == BinTrackKind.Raw2352AudioCandidate));
        }

        if (trackPlans.Count > 1)
        {
            warnings.Add(BinCueRescueWarningCode.MultipleOrderedBinTracksAssumed);
        }

        if (refusals.Count > 0)
        {
            return new BinCueRescuePlan(
                BinCueRescueDecision.Refuse,
                null,
                leaderCueWriteTarget,
                trackPlans,
                InferPlatformHint(trackPlans),
                true,
                refusals.Distinct().ToArray(),
                warnings.Distinct().ToArray());
        }

        if (string.IsNullOrWhiteSpace(leaderCueWriteTarget))
        {
            warnings.Add(BinCueRescueWarningCode.LeaderCueWriteTargetMissing);

            return new BinCueRescuePlan(
                BinCueRescueDecision.Refuse,
                null,
                null,
                trackPlans,
                InferPlatformHint(trackPlans),
                false,
                [BinCueRescueRefusalReason.InsufficientSectorEvidence],
                warnings.Distinct().ToArray());
        }

        return new BinCueRescuePlan(
            BinCueRescueDecision.GenerateTempCue,
            null,
            leaderCueWriteTarget,
            trackPlans,
            InferPlatformHint(trackPlans),
            false,
            Array.Empty<BinCueRescueRefusalReason>(),
            warnings.Distinct().ToArray());
    }

    private static IReadOnlyList<FileInfo> FindOrderedCandidateBins(FileInfo selectedBin, out bool ambiguousOrder)
    {
        ambiguousOrder = false;

        DirectoryInfo? directory = selectedBin.Directory;
        if (directory is null || !directory.Exists)
        {
            return [selectedBin];
        }

        FileInfo[] allBins = directory
            .EnumerateFiles("*.bin", SearchOption.TopDirectoryOnly)
            .Where(file => file.Exists)
            .ToArray();

        if (allBins.Length <= 1)
        {
            return [selectedBin];
        }

        string selectedPrefix = BuildDiscPrefix(selectedBin);
        List<FileInfo> grouped = allBins
            .Where(file => PathComparer.Equals(BuildDiscPrefix(file), selectedPrefix))
            .GroupBy(file => file.FullName, PathComparer)
            .Select(group => group.First())
            .ToList();

        if (grouped.Count <= 1)
        {
            return [selectedBin];
        }

        List<(FileInfo File, int? TrackNumber)> numbered = grouped
            .Select(file => (File: file, TrackNumber: TryExtractTrackNumber(file)))
            .ToList();

        if (numbered.Any(item => item.TrackNumber is null))
        {
            ambiguousOrder = true;
            return grouped.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        bool hasDuplicates = numbered
            .GroupBy(item => item.TrackNumber!.Value)
            .Any(group => group.Count() > 1);

        if (hasDuplicates)
        {
            ambiguousOrder = true;
            return grouped.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        int[] sortedNumbers = numbered
            .Select(item => item.TrackNumber!.Value)
            .Order()
            .ToArray();

        for (int i = 0; i < sortedNumbers.Length; i++)
        {
            if (sortedNumbers[i] != i + 1)
            {
                ambiguousOrder = true;
                break;
            }
        }

        return numbered
            .OrderBy(item => item.TrackNumber!.Value)
            .Select(item => item.File)
            .ToArray();
    }

    private static string BuildDiscPrefix(FileInfo file)
    {
        string name = Path.GetFileNameWithoutExtension(file.Name);

        string normalized = SafeRegexReplace(
            name,
            @"(?:[\s_\-\.\(\[]*)(?:track|trk|tk)\s*0*\d{1,3}(?:[\s_\-\.\)\]]*)",
            " ");

        normalized = CollapseWhitespace(normalized);

        if (!LooksLikeDiscPartNumber(normalized))
        {
            normalized = SafeRegexReplace(
                normalized,
                @"(?:[\s_\-\.\(\[]*)0*\d{1,3}(?:[\s_\-\.\)\]]*)$",
                " ");

            normalized = CollapseWhitespace(normalized);
        }

        return normalized.Length == 0
            ? Path.GetFileNameWithoutExtension(file.Name)
            : normalized;
    }

    private static int? TryExtractTrackNumber(FileInfo file)
    {
        string name = Path.GetFileNameWithoutExtension(file.Name);

        try
        {
            Match explicitTrack = Regex.Match(
                name,
                @"(?:track|trk|tk)\s*0*(?<number>\d{1,3})",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);

            if (explicitTrack.Success && int.TryParse(explicitTrack.Groups["number"].Value, out int explicitNumber))
            {
                return explicitNumber;
            }

            if (LooksLikeDiscPartNumber(name))
            {
                return null;
            }

            Match trailingNumber = Regex.Match(
                name,
                @"(?<!\d)0*(?<number>\d{1,3})(?!\d)\s*$",
                RegexOptions.CultureInvariant,
                RegexTimeout);

            if (trailingNumber.Success && int.TryParse(trailingNumber.Groups["number"].Value, out int trailing))
            {
                return trailing;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return null;
    }

    private static string? FindAdjacentCueForBin(string binPath)
    {
        FileInfo bin = new(binPath);
        DirectoryInfo? directory = bin.Directory;
        if (directory is null || !directory.Exists)
        {
            return null;
        }

        string sameBaseCue = Path.Combine(directory.FullName, Path.GetFileNameWithoutExtension(bin.Name) + ".cue");
        if (File.Exists(sameBaseCue) && CueReferencesBin(sameBaseCue, bin.FullName))
        {
            return sameBaseCue;
        }

        foreach (FileInfo cue in directory.EnumerateFiles("*.cue", SearchOption.TopDirectoryOnly))
        {
            if (CueReferencesBin(cue.FullName, bin.FullName))
            {
                return cue.FullName;
            }
        }

        return null;
    }

    private static bool CueReferencesBin(string cuePath, string binPath)
    {
        FileInfo cueFile = new(cuePath);
        if (!cueFile.Exists || cueFile.Length <= 0 || cueFile.Length > MaximumCueProbeBytes)
        {
            return false;
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(cueFile.FullName))
        {
            return false;
        }

        string cueText;
        try
        {
            cueText = File.ReadAllText(cueFile.FullName);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return false;
        }

        DirectoryInfo? cueDirectory = cueFile.Directory;
        if (cueDirectory is null)
        {
            return false;
        }

        string cueDirectoryPath;
        string fullBinPath;
        try
        {
            cueDirectoryPath = NormalizeFullPath(cueDirectory.FullName);
            fullBinPath = NormalizeFullPath(binPath);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(cueDirectoryPath))
        {
            return false;
        }

        Match[] fileLineMatches;
        int fileKeywordCount;

        try
        {
            fileLineMatches = CueFileLineRegex.Matches(cueText).Cast<Match>().ToArray();
            fileKeywordCount = CueFileKeywordRegex.Matches(cueText).Count;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        if (fileLineMatches.Length == 0 || fileKeywordCount != fileLineMatches.Length)
        {
            return false;
        }

        bool referencesSelectedBin = false;

        foreach (Match match in fileLineMatches)
        {
            string referenced = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["plain"].Value;

            if (!TryResolveSafeCueReference(referenced, cueDirectoryPath, out string? resolved))
            {
                return false;
            }

            if (!IsSafeExistingReferencedFile(resolved))
            {
                return false;
            }

            if (PathComparer.Equals(resolved, fullBinPath))
            {
                referencesSelectedBin = true;
            }
        }

        return referencesSelectedBin;
    }

    private static bool TryResolveSafeCueReference(
        string referenced,
        string cueDirectoryPath,
        out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(referenced))
        {
            return false;
        }

        try
        {
            if (Path.IsPathRooted(referenced))
            {
                return false;
            }

            string candidate = NormalizeFullPath(Path.Combine(cueDirectoryPath, referenced));
            if (!IsSameOrChildPath(candidate, cueDirectoryPath))
            {
                return false;
            }

            resolved = candidate;
            return true;
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }
    }

    private static bool IsSafeExistingReferencedFile(string resolvedPath)
    {
        try
        {
            FileInfo file = new(resolvedPath);

            if (!file.Exists || file.Length <= 0)
            {
                return false;
            }

            return !HasReparsePointInExistingPathFromVolumeRoot(file.FullName);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return false;
        }
    }

    private static string ToCueTrackMode(BinTrackKind kind)
    {
        return kind switch
        {
            BinTrackKind.Raw2352Mode1 => "MODE1/2352",
            BinTrackKind.Raw2352Mode2 => "MODE2/2352",
            BinTrackKind.Raw2352AudioCandidate => "AUDIO",
            _ => string.Empty
        };
    }

    private static BinCueRescuePlatformHint? InferPlatformHint(IReadOnlyList<BinCueRescueTrackPlan> tracks)
    {
        BinCueRescueTrackPlan? dataTrack = tracks.FirstOrDefault(track => track.IsDataTrack);
        if (dataTrack is null)
        {
            return null;
        }

        return dataTrack.Kind switch
        {
            BinTrackKind.Raw2352Mode2 => BinCueRescuePlatformHint.Raw2352Mode2Data,
            BinTrackKind.Raw2352Mode1 => BinCueRescuePlatformHint.Raw2352Mode1Data,
            _ => null
        };
    }

    private static BinCueRescuePlan Refuse(
        string? leaderCueWriteTarget,
        IReadOnlyList<BinCueRescueRefusalReason> reasons)
    {
        return new BinCueRescuePlan(
            BinCueRescueDecision.Refuse,
            null,
            leaderCueWriteTarget,
            Array.Empty<BinCueRescueTrackPlan>(),
            null,
            true,
            reasons,
            Array.Empty<BinCueRescueWarningCode>());
    }

    private static string SafeRegexReplace(
        string input,
        string pattern,
        string replacement)
    {
        try
        {
            return Regex.Replace(
                input,
                pattern,
                replacement,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return input;
        }
    }

    private static string CollapseWhitespace(string value)
    {
        try
        {
            return Regex.Replace(
                value,
                @"\s+",
                " ",
                RegexOptions.CultureInvariant,
                RegexTimeout).Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return value.Trim();
        }
    }

    private static bool LooksLikeDiscPartNumber(string name)
    {
        try
        {
            return DiscPartNumberRegex.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string root = NormalizeFullPath(rootPath);

            if (!IsSameOrChildPath(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current))
                    && IsExistingPathReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsIoOrPathFailure(ex))
        {
            return true;
        }
    }

    private static string NormalizeFullPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static bool IsSameOrChildPath(string path, string parent)
    {
        string fullPath = NormalizeFullPath(path);
        string fullParent = NormalizeFullPath(parent);

        if (fullPath.Equals(fullParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fullParentWithSeparator = EnsureDirectorySeparatorSuffix(fullParent);

        return fullPath.StartsWith(fullParentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsIoOrPathFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsPathFailure(Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}