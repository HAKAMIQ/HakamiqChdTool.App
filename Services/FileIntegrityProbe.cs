using System.Globalization;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

public static class FileIntegrityProbe
{
    private const int SampleSize = 64 * 1024;

    private const string SummaryNoPathKey = "LocFileIntegrity_SummaryNoPath";
    private const string SummaryPathIsDirectoryKey = "LocFileIntegrity_SummaryPathIsDirectory";
    private const string SummaryFileNotFoundKey = "LocFileIntegrity_SummaryFileNotFound";
    private const string SummaryEmptyFileKey = "LocFileIntegrity_SummaryEmptyFile";
    private const string SummaryReadFailedKey = "LocFileIntegrity_SummaryReadFailed";
    private const string SummaryUnexpectedChdSignatureKey = "LocFileIntegrity_SummaryUnexpectedChdSignature";
    private const string SummaryBasicReadOkKey = "LocFileIntegrity_SummaryBasicReadOk";
    private const string SummaryCueInvalidPathKey = "LocFileIntegrity_SummaryCueInvalidPath";
    private const string SummaryCueParseFailedKey = "LocFileIntegrity_SummaryCueParseFailed";
    private const string SummaryCueNoTracksKey = "LocFileIntegrity_SummaryCueNoTracks";
    private const string SummaryCueReferenceProblemKey = "LocFileIntegrity_SummaryCueReferenceProblem";
    private const string SummaryCueLinkedOkKey = "LocFileIntegrity_SummaryCueLinkedOk";

    private const string DetailNoPathKey = "LocFileIntegrity_DetailNoPath";
    private const string DetailPathIsDirectoryKey = "LocFileIntegrity_DetailPathIsDirectory";
    private const string DetailFileNotFoundKey = "LocFileIntegrity_DetailFileNotFound";
    private const string DetailEmptyFileKey = "LocFileIntegrity_DetailEmptyFile";
    private const string DetailReadFailedKey = "LocFileIntegrity_DetailReadFailed";
    private const string DetailReadStartFailedKey = "LocFileIntegrity_DetailReadStartFailed";
    private const string DetailReadEndFailedKey = "LocFileIntegrity_DetailReadEndFailed";
    private const string DetailUnexpectedChdSignatureKey = "LocFileIntegrity_DetailUnexpectedChdSignature";
    private const string DetailBasicReadOkKey = "LocFileIntegrity_DetailBasicReadOk";
    private const string DetailBasicReadOkWithHashAdviceKey = "LocFileIntegrity_DetailBasicReadOkWithHashAdvice";
    private const string DetailCueInvalidPathKey = "LocFileIntegrity_DetailCueInvalidPath";
    private const string DetailCueParseFailedKey = "LocFileIntegrity_DetailCueParseFailed";
    private const string DetailCueNoTracksKey = "LocFileIntegrity_DetailCueNoTracks";
    private const string DetailCueReferenceProblemKey = "LocFileIntegrity_DetailCueReferenceProblem";
    private const string DetailCueLinkedOkKey = "LocFileIntegrity_DetailCueLinkedOk";
    private const string DetailCueMissingReferenceKey = "LocFileIntegrity_DetailCueMissingReference";
    private const string DetailCueZeroLengthReferenceKey = "LocFileIntegrity_DetailCueZeroLengthReference";
    private const string DetailCueUnsafeReferenceKey = "LocFileIntegrity_DetailCueUnsafeReference";

    public sealed record ProbeResult(
        bool LooksOk,
        string SummaryKey,
        string DetailKey,
        IReadOnlyList<object?> DetailArgs);

    public static ProbeResult Analyze(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result(false, SummaryNoPathKey, DetailNoPathKey);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return Result(false, SummaryFileNotFoundKey, DetailFileNotFoundKey);
        }

        if (Directory.Exists(fullPath))
        {
            return Result(false, SummaryPathIsDirectoryKey, DetailPathIsDirectoryKey);
        }

        if (!File.Exists(fullPath))
        {
            return Result(false, SummaryFileNotFoundKey, DetailFileNotFoundKey);
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length == 0)
            {
                return Result(false, SummaryEmptyFileKey, DetailEmptyFileKey);
            }

            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: SampleSize,
                FileOptions.SequentialScan);

            if (!TryReadSample(stream, info.Length, out string detailKey))
            {
                return Result(false, SummaryReadFailedKey, detailKey);
            }

            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension == ".cue")
            {
                return AnalyzeCue(fullPath);
            }

            if (extension == ".chd" && !HasLikelyChdSignature(stream))
            {
                return Result(false, SummaryUnexpectedChdSignatureKey, DetailUnexpectedChdSignatureKey);
            }

            return BuildOkResult(info.Length, extension);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return Result(false, SummaryReadFailedKey, DetailReadFailedKey);
        }
    }

    private static ProbeResult BuildOkResult(long length, string extension)
    {
        string size = length.ToString("N0", CultureInfo.InvariantCulture);
        bool hashAdviceApplies = extension is ".iso" or ".bin" or ".img" or ".raw";

        return hashAdviceApplies
            ? Result(true, SummaryBasicReadOkKey, DetailBasicReadOkWithHashAdviceKey, size)
            : Result(true, SummaryBasicReadOkKey, DetailBasicReadOkKey, size);
    }

    private static bool TryReadSample(Stream stream, long length, out string detailKey)
    {
        detailKey = string.Empty;
        byte[] buffer = new byte[SampleSize];

        int firstRead = stream.Read(buffer, 0, (int)Math.Min(SampleSize, length));
        if (firstRead <= 0 && length > 0)
        {
            detailKey = DetailReadStartFailedKey;
            return false;
        }

        if (length > SampleSize * 2L)
        {
            long seek = Math.Max(0, length - SampleSize);
            stream.Seek(seek, SeekOrigin.Begin);

            int lastRead = stream.Read(buffer, 0, SampleSize);
            if (lastRead <= 0)
            {
                detailKey = DetailReadEndFailedKey;
                return false;
            }
        }

        return true;
    }

    private static bool HasLikelyChdSignature(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> head = stackalloc byte[8];
        int read = stream.Read(head);

        if (read < 4)
        {
            return false;
        }

        ReadOnlySpan<byte> value = head[..read];
        return value.StartsWith("MComprHD"u8)
               || value.StartsWith("CHD"u8)
               || value.StartsWith("MChD"u8);
    }

    private static ProbeResult AnalyzeCue(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Result(false, SummaryCueInvalidPathKey, DetailCueInvalidPathKey);
        }

        string baseDirectory;
        try
        {
            baseDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return Result(false, SummaryCueInvalidPathKey, DetailCueInvalidPathKey);
        }

        List<string> referenced = [];

        try
        {
            foreach (string line in ReadBoundedTextLines(cuePath))
            {
                if (TryExtractCueFileName(line, out string fileName))
                {
                    referenced.Add(fileName);
                }
            }
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return Result(false, SummaryCueParseFailedKey, DetailCueParseFailedKey);
        }

        if (referenced.Count == 0)
        {
            return Result(false, SummaryCueNoTracksKey, DetailCueNoTracksKey);
        }

        List<string> problems = [];
        foreach (string relativePath in referenced.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveCueReference(baseDirectory, relativePath, out string resolved))
            {
                problems.Add(FormatDetail(DetailCueUnsafeReferenceKey, relativePath));
                continue;
            }

            if (!File.Exists(resolved))
            {
                problems.Add(FormatDetail(DetailCueMissingReferenceKey, relativePath));
                continue;
            }

            try
            {
                var info = new FileInfo(resolved);
                if (info.Length == 0)
                {
                    problems.Add(FormatDetail(DetailCueZeroLengthReferenceKey, relativePath));
                }
            }
            catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
            {
                problems.Add(FormatDetail(DetailCueMissingReferenceKey, relativePath));
            }
        }

        if (problems.Count > 0)
        {
            return Result(
                false,
                SummaryCueReferenceProblemKey,
                DetailCueReferenceProblemKey,
                string.Join(Environment.NewLine, problems));
        }

        int uniqueCount = referenced.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return Result(true, SummaryCueLinkedOkKey, DetailCueLinkedOkKey, uniqueCount);
    }

    private static IEnumerable<string> ReadBoundedTextLines(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static bool TryExtractCueFileName(string rawLine, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = rawLine.AsSpan().TrimStart();
        if (span.Length < 4 || !span[..4].Equals("FILE".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (span.Length > 4 && !char.IsWhiteSpace(span[4]))
        {
            return false;
        }

        span = span[4..].TrimStart();
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

        int separator = IndexOfWhiteSpace(span);
        fileName = separator > 0 ? span[..separator].ToString().Trim() : span.ToString().Trim();
        return fileName.Length > 0;
    }

    private static bool TryResolveCueReference(string baseDirectory, string relativePath, out string resolved)
    {
        resolved = string.Empty;

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            if (!IsUnderDirectory(baseDirectory, candidate))
            {
                return false;
            }

            resolved = candidate;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static ProbeResult Result(
        bool looksOk,
        string summaryKey,
        string detailKey,
        params object?[] detailArgs) =>
        new(looksOk, summaryKey, detailKey, detailArgs);

    private static string FormatDetail(string key, params object?[] args)
    {
        if (args.Length == 0)
        {
            return key;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{key}|{string.Join("|", args)}");
    }

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> span)
    {
        for (int index = 0; index < span.Length; index++)
        {
            if (char.IsWhiteSpace(span[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or InvalidDataException
        or PathTooLongException;
}