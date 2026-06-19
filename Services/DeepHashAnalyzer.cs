using HakamiqChdTool.App.Models;
using Serilog;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Services;

public sealed record DeepHashAnalysisResult(
    IntegrityValidationState State,
    string StatusMessageKey,
    string DetailTooltipKey,
    IReadOnlyList<object?> DetailArgs,
    IReadOnlyList<DeepHashFileDigest> HashedFiles,
    IReadOnlyList<DeepHashMatch> Matches,
    IReadOnlyList<string> UnmatchedFileNames,
    string SuggestedStandardName = "",
    string MatchedSystemName = "",
    string MatchedGameName = "",
    int MatchedFileCount = 0,
    int HashedFileCount = 0,
    string FailureCode = "")
{
    public bool IsFatalInputReadFailure =>
        string.Equals(
            FailureCode,
            DeepHashAnalyzer.InputReadCrcOrIoFailureCode,
            StringComparison.Ordinal);
}

public sealed record DeepHashFileDigest(
    string Path,
    long SizeBytes,
    string Md5,
    string Sha1);

public sealed record DeepHashMatch(
    string FilePath,
    long SizeBytes,
    string Md5,
    string Sha1,
    string SystemName,
    string GameName,
    string RomName,
    string MatchSource,
    string Crc);

public static class DeepHashAnalyzer
{
    private const int BufferSize = 1024 * 1024;

    public const string InputReadCrcOrIoFailureCode = "InputReadCrcOrIoFailure";

    private const string StatusErrorKey = "LocDeepHash_StatusError";
    private const string StatusInputReadFailureKey = "LocDeepHash_StatusInputReadFailure";
    private const string StatusRequiresRawImageKey = "LocDeepHash_StatusRequiresRawImage";
    private const string StatusUnsupportedDirectKey = "LocDeepHash_StatusUnsupportedDirect";
    private const string StatusUnsupportedKey = "LocDeepHash_StatusUnsupported";
    private const string StatusNoDatabaseKey = "LocDeepHash_StatusNoDatabase";
    private const string StatusConflictingMatchKey = "LocDeepHash_StatusConflictingMatch";
    private const string StatusVerifiedKey = "LocDeepHash_StatusVerified";
    private const string StatusVerifiedCompleteKey = "LocDeepHash_StatusVerifiedComplete";
    private const string StatusIncompleteKey = "LocDeepHash_StatusIncomplete";
    private const string StatusModifiedKey = "LocDeepHash_StatusModified";

    private const string TipNoPathKey = "LocDeepHash_TipNoPath";
    private const string TipInvalidPathKey = "LocDeepHash_TipInvalidPath";
    private const string TipFileNotFoundKey = "LocDeepHash_TipFileNotFound";
    private const string TipChdNeedsExtractionKey = "LocDeepHash_TipChdNeedsExtraction";
    private const string TipArchiveNeedsExtractionKey = "LocDeepHash_TipArchiveNeedsExtraction";
    private const string TipUnsupportedExtensionKey = "LocDeepHash_TipUnsupportedExtension";
    private const string TipNoTrackFilesKey = "LocDeepHash_TipNoTrackFiles";
    private const string TipResolveFailedKey = "LocDeepHash_TipResolveFailed";
    private const string TipHashFailedKey = "LocDeepHash_TipHashFailed";
    private const string TipInputReadCrcOrIoFailureKey = "LocDeepHash_TipInputReadCrcOrIoFailure";
    private const string TipNoDatabaseKey = "LocDeepHash_TipNoDatabase";
    private const string TipConflictingMatchesKey = "LocDeepHash_TipConflictingMatches";
    private const string TipVerifiedHeaderKey = "LocDeepHash_TipVerifiedHeader";
    private const string TipPartialMatchKey = "LocDeepHash_TipPartialMatch";
    private const string TipNoRedumpMatchKey = "LocDeepHash_TipNoRedumpMatch";

    private static readonly HashSet<string> HashableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".gdi", ".iso", ".bin", ".img", ".raw", ".cso"
    };

    private static readonly HashSet<string> ArchiveNoDirectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z"
    };

    public static async Task<DeepHashAnalysisResult> DeepHashAnalyzeAsync(
        string probePath,
        RedumpSqliteManager? redumpDatabase,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(probePath))
        {
            return Error(TipNoPathKey);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(probePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: invalid probe path. Path={Path}", probePath);
            return Error(TipInvalidPathKey);
        }

        if (!File.Exists(fullPath))
        {
            return Error(TipFileNotFoundKey);
        }

        string extension = Path.GetExtension(fullPath);

        if (string.Equals(extension, ".chd", StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                IntegrityValidationState.NoDirectRedump,
                StatusRequiresRawImageKey,
                TipChdNeedsExtractionKey,
                [fullPath]);
        }

        if (ArchiveNoDirectExtensions.Contains(extension))
        {
            return Result(
                IntegrityValidationState.NoDirectRedump,
                StatusUnsupportedDirectKey,
                TipArchiveNeedsExtractionKey,
                [fullPath]);
        }

        if (!HashableExtensions.Contains(extension))
        {
            Log.Debug("DeepHashAnalyzer: skipped unsupported Redump extension {Extension} for {Path}", extension, fullPath);
            return Result(
                IntegrityValidationState.Unsupported,
                StatusUnsupportedKey,
                TipUnsupportedExtensionKey,
                [extension, fullPath]);
        }

        IReadOnlyList<string> filesToHash;
        try
        {
            filesToHash = ResolveFilesToHash(fullPath);
        }
        catch (Exception ex) when (IsInputReadFailureException(ex))
        {
            Log.Warning(ex, "DeepHashAnalyzer: input read failed while resolving hash files. Path={Path}; FailureCode={FailureCode}", fullPath, InputReadCrcOrIoFailureCode);
            return InputReadFailure();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: failed to resolve files to hash. Path={Path}", fullPath);
            return Error(TipResolveFailedKey);
        }

        if (filesToHash.Count == 0)
        {
            return Error(TipNoTrackFilesKey);
        }

        List<DeepHashFileDigest> hashed;
        try
        {
            hashed = await Task.Run(
                () => HashAllFiles(filesToHash, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsInputReadFailureException(ex))
        {
            Log.Warning(ex, "DeepHashAnalyzer: input read failed while hashing. Path={Path}; FailureCode={FailureCode}", fullPath, InputReadCrcOrIoFailureCode);
            return InputReadFailure();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: hashing failed. Path={Path}", fullPath);
            return Error(TipHashFailedKey);
        }

        if (redumpDatabase is null || !redumpDatabase.HasAnyRows())
        {
            return Result(
                IntegrityValidationState.NoDat,
                StatusNoDatabaseKey,
                TipNoDatabaseKey,
                hashedFiles: hashed);
        }

        var matches = new List<DeepHashMatch>();
        var misses = new List<string>();

        foreach (DeepHashFileDigest file in hashed)
        {
            if (redumpDatabase.TryMatchHash(file.Md5, file.Sha1, file.SizeBytes, out RedumpRomHit hit))
            {
                matches.Add(ToMatch(file, hit));
            }
            else
            {
                misses.Add(Path.GetFileName(file.Path));
            }
        }

        if (matches.Count == hashed.Count)
        {
            return BuildFullMatchResult(fullPath, hashed, matches);
        }

        if (matches.Count > 0)
        {
            return Result(
                IntegrityValidationState.Failed,
                StatusIncompleteKey,
                TipPartialMatchKey,
                [matches.Count, hashed.Count],
                hashed,
                matches,
                misses);
        }

        return Result(
            IntegrityValidationState.NoRedumpMatch,
            StatusModifiedKey,
            TipNoRedumpMatchKey,
            hashedFiles: hashed);
    }

    private static DeepHashAnalysisResult BuildFullMatchResult(
        string fullPath,
        IReadOnlyList<DeepHashFileDigest> hashed,
        IReadOnlyList<DeepHashMatch> matches)
    {
        if (!MatchesBelongToOneDisc(matches))
        {
            return Result(
                IntegrityValidationState.Failed,
                StatusConflictingMatchKey,
                TipConflictingMatchesKey,
                hashedFiles: hashed,
                matches: matches);
        }

        DeepHashMatch first = matches[0];
        string statusKey = matches.Count == 1
            ? StatusVerifiedKey
            : StatusVerifiedCompleteKey;

        return Result(
            IntegrityValidationState.Verified,
            statusKey,
            TipVerifiedHeaderKey,
            hashedFiles: hashed,
            matches: matches,
            suggestedStandardName: BuildSuggestedStandardFileName(first.GameName, fullPath),
            matchedSystemName: first.SystemName,
            matchedGameName: first.GameName,
            matchedFileCount: matches.Count,
            hashedFileCount: hashed.Count);
    }

    private static DeepHashAnalysisResult Error(string detailKey) =>
        Result(IntegrityValidationState.Error, StatusErrorKey, detailKey);

    private static DeepHashAnalysisResult InputReadFailure() =>
        Result(
            IntegrityValidationState.Error,
            StatusInputReadFailureKey,
            TipInputReadCrcOrIoFailureKey,
            failureCode: InputReadCrcOrIoFailureCode);

    private static DeepHashAnalysisResult Result(
        IntegrityValidationState state,
        string statusKey,
        string detailKey,
        IReadOnlyList<object?>? detailArgs = null,
        IReadOnlyList<DeepHashFileDigest>? hashedFiles = null,
        IReadOnlyList<DeepHashMatch>? matches = null,
        IReadOnlyList<string>? unmatchedFileNames = null,
        string suggestedStandardName = "",
        string matchedSystemName = "",
        string matchedGameName = "",
        int? matchedFileCount = null,
        int? hashedFileCount = null,
        string failureCode = "")
    {
        IReadOnlyList<DeepHashFileDigest> resolvedHashedFiles = hashedFiles ?? [];
        IReadOnlyList<DeepHashMatch> resolvedMatches = matches ?? [];

        return new DeepHashAnalysisResult(
            state,
            statusKey,
            detailKey,
            detailArgs ?? [],
            resolvedHashedFiles,
            resolvedMatches,
            unmatchedFileNames ?? [],
            suggestedStandardName,
            matchedSystemName,
            matchedGameName,
            matchedFileCount ?? resolvedMatches.Count,
            hashedFileCount ?? resolvedHashedFiles.Count,
            failureCode);
    }

    private static List<DeepHashFileDigest> HashAllFiles(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        var result = new List<DeepHashFileDigest>(files.Count);

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(file);
            (string md5, string sha1) = ComputeMd5Sha1Sequential(file, cancellationToken);
            result.Add(new DeepHashFileDigest(file, info.Length, md5, sha1));
        }

        return result;
    }

    private static (string Md5Lower, string Sha1Lower) ComputeMd5Sha1Sequential(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[BufferSize];

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            FileOptions.SequentialScan);

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySpan<byte> span = buffer.AsSpan(0, read);
            md5.AppendData(span);
            sha1.AppendData(span);
        }

        string md5Hex = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
        string sha1Hex = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
        return (md5Hex, sha1Hex);
    }

    private static IReadOnlyList<string> ResolveFilesToHash(string fullProbePath)
    {
        string extension = Path.GetExtension(fullProbePath);
        string normalized = Path.GetFullPath(fullProbePath);

        return extension.ToLowerInvariant() switch
        {
            ".cue" => ResolveCueBinFiles(normalized),
            ".gdi" => ResolveGdiTrackFiles(normalized),
            ".iso" or ".bin" or ".img" or ".raw" or ".cso" => [normalized],
            _ => []
        };
    }

    private static IReadOnlyList<string> ResolveCueBinFiles(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        string baseDirectory = Path.GetFullPath(directory);
        var names = new List<string>();

        foreach (string line in File.ReadLines(cuePath, Encoding.UTF8))
        {
            string text = line.Trim();
            if (!text.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name;
            int firstQuote = text.IndexOf('"');
            int secondQuote = firstQuote >= 0 ? text.IndexOf('"', firstQuote + 1) : -1;

            if (firstQuote >= 0 && secondQuote > firstQuote)
            {
                name = text.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }
            else
            {
                ReadOnlySpan<char> span = text.AsSpan(5).TrimStart();
                int separator = IndexOfWhiteSpace(span);
                name = separator > 0 ? span[..separator].ToString() : span.ToString();
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return ResolveDescriptorFileNames(baseDirectory, names);
    }

    private static IReadOnlyList<string> ResolveGdiTrackFiles(string gdiPath)
    {
        string? directory = Path.GetDirectoryName(gdiPath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        string baseDirectory = Path.GetFullPath(directory);
        var names = new List<string>();
        bool skippedHeader = false;

        foreach (string line in File.ReadLines(gdiPath, Encoding.UTF8))
        {
            if (!skippedHeader)
            {
                skippedHeader = true;
                continue;
            }

            if (TryExtractGdiTrackFileName(line, out string name))
            {
                names.Add(name);
            }
        }

        return ResolveDescriptorFileNames(baseDirectory, names);
    }

    private static bool TryExtractGdiTrackFileName(string line, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = line.AsSpan().Trim();
        if (span.Length == 0 || span[0] == '#')
        {
            return false;
        }

        for (int field = 0; field < 4; field++)
        {
            if (!TryConsumeToken(ref span, out _))
            {
                return false;
            }
        }

        span = span.TrimStart();
        if (span.Length == 0)
        {
            return false;
        }

        if (span[0] == '"')
        {
            int closingQuote = span[1..].IndexOf('"');
            if (closingQuote < 0)
            {
                return false;
            }

            fileName = span.Slice(1, closingQuote).ToString().Trim();
            return fileName.Length > 0;
        }

        if (!TryConsumeToken(ref span, out ReadOnlySpan<char> token))
        {
            return false;
        }

        fileName = token.Trim('"').ToString().Trim();
        return fileName.Length > 0;
    }

    private static bool TryConsumeToken(ref ReadOnlySpan<char> span, out ReadOnlySpan<char> token)
    {
        span = span.TrimStart();
        token = default;

        if (span.Length == 0)
        {
            return false;
        }

        int separator = IndexOfWhiteSpace(span);
        if (separator < 0)
        {
            token = span;
            span = [];
            return token.Length > 0;
        }

        token = span[..separator];
        span = span[(separator + 1)..];
        return token.Length > 0;
    }

    private static IReadOnlyList<string> ResolveDescriptorFileNames(string baseDirectory, IReadOnlyList<string> names)
    {
        var resolved = new List<string>();
        var missing = new List<string>();

        foreach (string relativePath in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string combined = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            if (!IsUnderDirectory(baseDirectory, combined))
            {
                missing.Add(relativePath);
                continue;
            }

            if (File.Exists(combined))
            {
                resolved.Add(combined);
            }
            else
            {
                missing.Add(relativePath);
            }
        }

        if (missing.Count > 0)
        {
            throw new FileNotFoundException("Disc descriptor references missing files.");
        }

        return resolved;
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = Path.GetFullPath(baseDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        string path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static DeepHashMatch ToMatch(DeepHashFileDigest file, RedumpRomHit hit) => new(
        file.Path,
        file.SizeBytes,
        file.Md5,
        file.Sha1,
        hit.SystemName,
        hit.GameName,
        hit.RomName,
        hit.MatchSource,
        hit.Crc ?? string.Empty);

    private static string BuildSuggestedStandardFileName(string redumpGameName, string originalPath)
    {
        string extension = Path.GetExtension(originalPath);
        string safeBaseName = SanitizeFileName(redumpGameName);
        return string.IsNullOrWhiteSpace(safeBaseName) ? string.Empty : safeBaseName + extension;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (char character in value.Trim())
        {
            builder.Append(invalid.Contains(character) ? ' ' : character);
        }

        string collapsed = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        return collapsed.TrimEnd('.', ' ');
    }

    private static bool MatchesBelongToOneDisc(IEnumerable<DeepHashMatch> matches)
    {
        string? system = null;
        string? game = null;

        foreach (DeepHashMatch match in matches)
        {
            if (system is null)
            {
                system = match.SystemName;
                game = match.GameName;
                continue;
            }

            if (!string.Equals(system, match.SystemName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(game, match.GameName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsInputReadFailureException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;
}
