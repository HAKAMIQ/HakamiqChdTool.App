using HakamiqChdTool.App.Core.Disc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

public static partial class ArchiveCandidateDiscovery
{
    public const string MultipleConvertibleImageSetsMessageResourceKey = "LocArchive_MultipleConvertibleDiscImages";
    public const string DescriptorMissingDependenciesMessageResourceKey = "LocArchive_DescriptorMissingDependencies";
    public const string DescriptorUnsafeReferenceMessageResourceKey = "LocArchive_DescriptorUnsafeReference";
    public const string DescriptorHasNoTrackReferencesMessageResourceKey = "LocArchive_DescriptorHasNoTrackReferences";
    public const string DescriptorUnreadableMessageResourceKey = "LocArchive_DescriptorUnreadable";
    public const string UnsupportedDiscImageMessageResourceKey = "LocArchive_UnsupportedDiscImage";
    public const string EmptyArchiveMessageResourceKey = "LocArchive_EmptyArchive";
    public const string DescriptorMissingDependenciesMessage = DescriptorMissingDependenciesMessageResourceKey;

    private const long MaxDescriptorTextBytes = 4L * 1024L * 1024L;
    private const int RegexTimeoutMilliseconds = 250;

    public static readonly string[] ConvertibleLeaderPriority = [".gdi", ".cue", ".toc", ".nrg", ".iso", ".cso"];
    private static readonly string[] DescriptorLeaderPriority = [".gdi", ".cue", ".toc"];

    public static bool IsConvertibleLeaderExtension(string extension)
    {
        return ConvertibleLeaderPriority.Contains(
            NormalizeExtension(extension),
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsUnsupportedDiscImageExtension(string extension)
    {
        string normalized = NormalizeExtension(extension);
        return normalized is ".cdi";
    }

    public static bool HasUnsupportedDiscImagePath(IEnumerable<string> pathsOrKeys)
    {
        ArgumentNullException.ThrowIfNull(pathsOrKeys);

        return pathsOrKeys
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Any(path => IsUnsupportedDiscImageExtension(Path.GetExtension(path)));
    }

    public static bool IsDescriptorLeaderPath(string? pathOrKey)
    {
        return IsDescriptorLeaderExtension(Path.GetExtension(pathOrKey ?? string.Empty));
    }

    public static bool IsDependentTrackExtension(string extension)
    {
        string normalized = NormalizeExtension(extension);
        return normalized is ".bin" or ".raw" or ".wav";
    }

    public static bool IsChdExtension(string extension)
    {
        return string.Equals(
            NormalizeExtension(extension),
            ".chd",
            StringComparison.OrdinalIgnoreCase);
    }

    public static string[] GetConvertibleLeaderExtensions(IEnumerable<string> pathsOrKeys)
    {
        ArgumentNullException.ThrowIfNull(pathsOrKeys);

        HashSet<string> extensions = pathsOrKeys
            .Select(path => NormalizeExtension(Path.GetExtension(path ?? string.Empty)))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ConvertibleLeaderPriority
            .Where(extensions.Contains)
            .ToArray();
    }

    public static int CountConvertibleLeaderPaths(IEnumerable<string> pathsOrKeys)
    {
        ArgumentNullException.ThrowIfNull(pathsOrKeys);

        return pathsOrKeys
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePortablePath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(path => IsConvertibleLeaderExtension(Path.GetExtension(path)));
    }

    public static bool HasMultipleConvertibleLeaderPaths(IEnumerable<string> pathsOrKeys)
    {
        return CountConvertibleLeaderPaths(pathsOrKeys) > 1;
    }

    public static string[] GetEffectiveConvertibleLeaderExtensions(IEnumerable<string> pathsOrKeys)
    {
        ArgumentNullException.ThrowIfNull(pathsOrKeys);

        HashSet<string> extensions = GetEffectiveConvertibleLeaderPaths(pathsOrKeys)
            .Select(path => NormalizeExtension(Path.GetExtension(path)))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] priority = extensions.Any(IsDescriptorLeaderExtension)
            ? DescriptorLeaderPriority
            : ConvertibleLeaderPriority;

        return priority
            .Where(extensions.Contains)
            .ToArray();
    }

    public static int CountEffectiveConvertibleLeaderPaths(IEnumerable<string> pathsOrKeys)
    {
        return GetEffectiveConvertibleLeaderPaths(pathsOrKeys).Count;
    }

    public static bool HasMultipleEffectiveConvertibleLeaderPaths(IEnumerable<string> pathsOrKeys)
    {
        return CountEffectiveConvertibleLeaderPaths(pathsOrKeys) > 1;
    }

    public static string? FindFirstEffectiveConvertibleLeaderPath(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return GetEffectiveConvertibleLeaderPaths(paths).FirstOrDefault();
    }

    public static IReadOnlyList<string> GetEffectiveConvertibleLeaderPaths(IEnumerable<string> pathsOrKeys)
    {
        ArgumentNullException.ThrowIfNull(pathsOrKeys);

        List<string> materialized = pathsOrKeys
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePortablePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasDescriptorLeader = materialized.Any(path => IsDescriptorLeaderExtension(Path.GetExtension(path)));
        string[] priority = hasDescriptorLeader
            ? DescriptorLeaderPriority
            : ConvertibleLeaderPriority;

        List<string> result = [];
        foreach (string extension in priority)
        {
            result.AddRange(materialized.Where(path => string.Equals(
                NormalizeExtension(Path.GetExtension(path)),
                extension,
                StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    public static string? FindFirstConvertibleLeaderPath(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<string> materialized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        foreach (string extension in ConvertibleLeaderPriority)
        {
            string? match = materialized.FirstOrDefault(path => string.Equals(
                Path.GetExtension(path),
                extension,
                StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsDescriptorLeaderExtension(string? extension)
    {
        return DescriptorLeaderPriority.Contains(
            NormalizeExtension(extension),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string? FindFirstChdPath(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return paths.FirstOrDefault(path => string.Equals(
            Path.GetExtension(path),
            ".chd",
            StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryValidateExtractedDescriptorDependencies(
        string leaderPath,
        IEnumerable<string> extractedFiles,
        out string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderPath);
        ArgumentNullException.ThrowIfNull(extractedFiles);

        failureMessage = string.Empty;

        string extension = NormalizeExtension(Path.GetExtension(leaderPath));
        if (extension is not (".cue" or ".gdi" or ".toc"))
        {
            return true;
        }

        string descriptorText;
        try
        {
            descriptorText = ReadSmallDescriptorText(leaderPath);
        }
        catch (Exception ex) when (IsDescriptorReadFailure(ex))
        {
            failureMessage = DescriptorUnreadableMessageResourceKey;
            return false;
        }

        return TryValidateDescriptorDependencies(
            leaderPath,
            descriptorText,
            extractedFiles,
            out failureMessage);
    }

    public static bool TryValidateDescriptorDependencies(
        string leaderPathOrKey,
        string descriptorText,
        IEnumerable<string> availablePathsOrKeys,
        out string failureMessage)
    {
        ArchiveDescriptorDependencyValidationResult result = AnalyzeDescriptorDependencies(
            leaderPathOrKey,
            descriptorText,
            availablePathsOrKeys);

        failureMessage = result.MessageResourceKey;
        return result.IsValid;
    }

    internal static ArchiveDescriptorDependencyValidationResult AnalyzeDescriptorDependencies(
        string leaderPathOrKey,
        string descriptorText,
        IEnumerable<string> availablePathsOrKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderPathOrKey);
        ArgumentNullException.ThrowIfNull(availablePathsOrKeys);

        string extension = NormalizeExtension(Path.GetExtension(leaderPathOrKey));
        if (extension is not (".cue" or ".gdi" or ".toc"))
        {
            return ArchiveDescriptorDependencyValidationResult.Success(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        string primaryDirectory = NormalizeDirectoryKey(
            Path.GetDirectoryName(NormalizePortablePath(leaderPathOrKey)));

        IReadOnlyList<string> referenceCandidates = extension switch
        {
            ".cue" => ExtractCueReferenceCandidates(descriptorText),
            ".gdi" => ExtractGdiReferenceCandidates(descriptorText),
            ".toc" => ExtractTocReferenceCandidates(descriptorText),
            _ => []
        };

        if (referenceCandidates.Count == 0)
        {
            return ArchiveDescriptorDependencyValidationResult.Failure(
                DescriptorHasNoTrackReferencesMessageResourceKey);
        }

        bool hasUnsafeReference = false;
        HashSet<string> requiredKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in referenceCandidates)
        {
            if (!TryBuildDescriptorReferenceKey(primaryDirectory, candidate, out string key))
            {
                hasUnsafeReference = true;
                continue;
            }

            requiredKeys.Add(key);
        }

        if (hasUnsafeReference)
        {
            return ArchiveDescriptorDependencyValidationResult.Failure(
                DescriptorUnsafeReferenceMessageResourceKey,
                requiredKeys);
        }

        if (requiredKeys.Count == 0)
        {
            return ArchiveDescriptorDependencyValidationResult.Failure(
                DescriptorHasNoTrackReferencesMessageResourceKey);
        }

        HashSet<string> availableKeys = availablePathsOrKeys
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeLookupKey(NormalizePortablePath(path)))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string requiredKey in requiredKeys)
        {
            if (!availableKeys.Contains(requiredKey))
            {
                return ArchiveDescriptorDependencyValidationResult.Failure(
                    DescriptorMissingDependenciesMessageResourceKey,
                    requiredKeys);
            }
        }

        return ArchiveDescriptorDependencyValidationResult.Success(requiredKeys);
    }

    public static HashSet<string> ParseCueReferencedLeaves(string cueText)
    {
        return ParseCueReferencedKeys(cueText, string.Empty)
            .Select(Path.GetFileName)
            .Where(leaf => !string.IsNullOrWhiteSpace(leaf))
            .Select(value => NormalizeLookupKey(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> ParseGdiReferencedLeaves(string gdiText)
    {
        return ParseGdiReferencedKeys(gdiText, string.Empty)
            .Select(Path.GetFileName)
            .Where(leaf => !string.IsNullOrWhiteSpace(leaf))
            .Select(value => NormalizeLookupKey(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> ParseTocReferencedLeaves(string tocText)
    {
        return ParseTocReferencedKeys(tocText, string.Empty)
            .Select(Path.GetFileName)
            .Where(leaf => !string.IsNullOrWhiteSpace(leaf))
            .Select(value => NormalizeLookupKey(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> ParseCueReferencedKeys(
        string cueText,
        string descriptorDirectoryKey)
    {
        HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cueText))
        {
            return results;
        }

        foreach (string candidate in ExtractCueReferenceCandidates(cueText))
        {
            if (TryBuildDescriptorReferenceKey(descriptorDirectoryKey, candidate, out string key))
            {
                results.Add(key);
            }
        }

        return results;
    }

    public static HashSet<string> ParseGdiReferencedKeys(
        string gdiText,
        string descriptorDirectoryKey)
    {
        HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gdiText))
        {
            return results;
        }

        foreach (string candidate in ExtractGdiReferenceCandidates(gdiText))
        {
            if (TryBuildDescriptorReferenceKey(descriptorDirectoryKey, candidate, out string key))
            {
                results.Add(key);
            }
        }

        return results;
    }

    public static HashSet<string> ParseTocReferencedKeys(
        string tocText,
        string descriptorDirectoryKey)
    {
        HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tocText))
        {
            return results;
        }

        foreach (string candidate in ExtractTocReferenceCandidates(tocText))
        {
            if (TryBuildDescriptorReferenceKey(descriptorDirectoryKey, candidate, out string key))
            {
                results.Add(key);
            }
        }

        return results;
    }

    private static IReadOnlyList<string> ExtractCueReferenceCandidates(string cueText)
    {
        if (string.IsNullOrWhiteSpace(cueText))
        {
            return [];
        }

        List<string> results = [];
        foreach (string line in cueText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (CueSheetFileStatementReader.TryRead(line, out string candidate, out _))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private static IReadOnlyList<string> ExtractGdiReferenceCandidates(string gdiText)
    {
        if (string.IsNullOrWhiteSpace(gdiText))
        {
            return [];
        }

        List<string> results = [];
        foreach (string rawLine in gdiText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || !char.IsDigit(line[0]))
            {
                continue;
            }

            Match quoted = GdiQuotedFileRegex().Match(line);

            string? candidate = quoted.Success
                ? quoted.Groups["file"].Value
                : null;

            if (string.IsNullOrWhiteSpace(candidate))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    candidate = parts[4];
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private static IReadOnlyList<string> ExtractTocReferenceCandidates(string tocText)
    {
        if (string.IsNullOrWhiteSpace(tocText))
        {
            return [];
        }

        List<string> results = [];
        foreach (string rawLine in tocText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            Match match = TocFileRegex().Match(line);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups["file"].Value))
            {
                results.Add(match.Groups["file"].Value);
            }
        }

        return results;
    }

    public static string NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = NormalizePortablePath(value)
            .Trim()
            .Replace('\\', '/')
            .Trim('/');

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('\0', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> cleanSegments = new(segments.Length);

        foreach (string segment in segments)
        {
            string cleanSegment = segment.Trim();

            if (cleanSegment.Length == 0 || string.Equals(cleanSegment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(cleanSegment, "..", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            cleanSegments.Add(cleanSegment);
        }

        return cleanSegments.Count == 0
            ? string.Empty
            : string.Join("/", cleanSegments).ToUpperInvariant();
    }

    public static string NormalizeDirectoryKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value
            .Trim()
            .Replace('\\', '/')
            .TrimEnd('/');

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('\0', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (string.Equals(segment.Trim(), "..", StringComparison.Ordinal))
            {
                return string.Empty;
            }
        }

        return normalized.ToUpperInvariant();
    }

    private static bool TryBuildDescriptorReferenceKey(
        string descriptorDirectoryKey,
        string candidate,
        out string key)
    {
        key = string.Empty;
        string normalizedCandidate = NormalizePortablePath(candidate).Trim();

        if (!IsSafeDescriptorReference(normalizedCandidate))
        {
            return false;
        }

        string directory = NormalizeDirectoryKey(descriptorDirectoryKey);
        string combined = string.IsNullOrWhiteSpace(directory)
            ? normalizedCandidate.TrimStart('/')
            : directory + "/" + normalizedCandidate.TrimStart('/');

        key = NormalizeLookupKey(combined);
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool IsSafeDescriptorReference(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        string normalized = NormalizePortablePath(candidate).Trim();

        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal)
            || normalized.Contains(':'))
        {
            return false;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            string cleanSegment = segment.Trim();

            if (cleanSegment.Length == 0 || string.Equals(cleanSegment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(cleanSegment, "..", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePortablePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/');
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        string trimmed = extension.Trim();

        return trimmed.StartsWith('.')
            ? trimmed.ToLowerInvariant()
            : "." + trimmed.ToLowerInvariant();
    }

    private static string ReadSmallDescriptorText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        FileInfo fileInfo = new(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(null, fullPath);
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new IOException();
        }

        if (fileInfo.Length <= 0 || fileInfo.Length > MaxDescriptorTextBytes)
        {
            throw new InvalidOperationException();
        }

        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        return reader.ReadToEnd();
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsDescriptorReadFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
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

                current = parent;
            }
        }
        catch (Exception ex) when (IsDescriptorReadFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsDescriptorReadFailure(ex))
        {
            return true;
        }
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsDescriptorReadFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or RegexMatchTimeoutException
            or System.Security.SecurityException;
    }


    [GeneratedRegex(
        "\"(?<file>[^\"]+)\"",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex GdiQuotedFileRegex();


    [GeneratedRegex(
        "^\\s*(?:FILE|AUDIOFILE|DATAFILE)\\s+(?:\"(?<file>[^\"]+)\"|(?<file>\\S+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex TocFileRegex();
}
