using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

internal readonly record struct DiscRawSerialProbeResult(
    bool Success,
    string Platform,
    string Region,
    string Serial,
    string Reason);

internal static class DiscRawSerialProbe
{
    private const int MaxProbeBytesPerFile = 64 * 1024 * 1024;
    private const int ReadBufferSize = 1024 * 1024;
    private const int SerialScanOverlapChars = 128;
    private const int MaxDescriptorBytes = 1024 * 1024;

    private const string NoResultReasonKey = "LocDiscRawProbe_NoResult";
    private const string RawSerialDetectedReasonKey = "LocDiscRawProbe_RawSerialDetected";

    public static bool TryDetectRegion(string? path, out string region)
    {
        region = string.Empty;

        if (!TryProbe(path, out DiscRawSerialProbeResult result))
        {
            return false;
        }

        region = result.Region;
        return !string.IsNullOrWhiteSpace(region);
    }

    public static bool TryDetectPlatform(string? path, out PlatformDetectionResult detection)
    {
        detection = PlatformDetectionResult.Create(string.Empty, string.Empty, 10, NoResultReasonKey);

        if (!TryProbe(path, out DiscRawSerialProbeResult result))
        {
            return false;
        }

        detection = PlatformDetectionResult.Create(
            result.Platform,
            result.Serial,
            94,
            result.Reason);

        return true;
    }

    public static bool TryProbe(string? path, out DiscRawSerialProbeResult result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
            ValidateReadableFilePath(fullPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || IsExpectedReadException(ex))
        {
            return false;
        }

        foreach (string candidate in EnumerateProbeFiles(fullPath))
        {
            if (!IsReadableFilePath(candidate))
            {
                continue;
            }

            if (TryScanFile(candidate, out result))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateProbeFiles(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension == ".cue")
        {
            foreach (string referenced in EnumerateCueReferences(path))
            {
                yield return referenced;
            }

            yield return path;
            yield break;
        }

        if (extension == ".gdi")
        {
            foreach (string referenced in EnumerateGdiReferences(path))
            {
                yield return referenced;
            }

            yield return path;
            yield break;
        }

        yield return path;
    }

    private static IEnumerable<string> EnumerateCueReferences(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        string baseDirectory;
        try
        {
            ValidateReadableFilePath(cuePath);
            baseDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || IsExpectedReadException(ex))
        {
            yield break;
        }

        foreach (string line in ReadBoundedDescriptorLines(cuePath))
        {
            if (!TryExtractCueFileName(line, out string value))
            {
                continue;
            }

            if (TryResolveDescriptorReference(baseDirectory, value, out string resolved))
            {
                yield return resolved;
            }
        }
    }

    private static IEnumerable<string> EnumerateGdiReferences(string gdiPath)
    {
        string? directory = Path.GetDirectoryName(gdiPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        string baseDirectory;
        try
        {
            ValidateReadableFilePath(gdiPath);
            baseDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || IsExpectedReadException(ex))
        {
            yield break;
        }

        bool skippedHeader = false;

        foreach (string line in ReadBoundedDescriptorLines(gdiPath))
        {
            if (!skippedHeader)
            {
                skippedHeader = true;
                continue;
            }

            if (!TryExtractGdiFileName(line, out string value))
            {
                continue;
            }

            if (TryResolveDescriptorReference(baseDirectory, value, out string resolved))
            {
                yield return resolved;
            }
        }
    }

    private static bool TryExtractCueFileName(string rawLine, out string fileName) =>
        CueSheetFileStatementReader.TryRead(rawLine, out fileName, out _);

    private static bool TryExtractGdiFileName(string line, out string fileName)
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

    private static IReadOnlyList<string> ReadBoundedDescriptorLines(string path)
    {
        try
        {
            ValidateReadableFilePath(path);

            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxDescriptorBytes)
            {
                return [];
            }

            var lines = new List<string>();

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
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return [];
        }
    }

    private static bool TryResolveDescriptorReference(string baseDirectory, string relativePath, out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(baseDirectory)
            || string.IsNullOrWhiteSpace(relativePath)
            || relativePath.IndexOf('\0') >= 0
            || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(baseDirectory);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!IsUnderDirectory(root, candidate) || !IsReadableFilePath(candidate))
            {
                return false;
            }

            resolved = candidate;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || IsExpectedReadException(ex))
        {
            return false;
        }
    }

    private static bool TryScanFile(string path, out DiscRawSerialProbeResult result)
    {
        result = default;

        try
        {
            ValidateReadableFilePath(path);

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: ReadBufferSize,
                options: FileOptions.SequentialScan);

            long remaining = Math.Min(stream.Length, MaxProbeBytesPerFile);
            byte[] buffer = new byte[ReadBufferSize];
            string carry = string.Empty;

            while (remaining > 0)
            {
                int targetRead = (int)Math.Min(buffer.Length, remaining);
                int read = stream.Read(buffer, 0, targetRead);
                if (read <= 0)
                {
                    break;
                }

                remaining -= read;
                string chunk = carry + BytesToSearchableAscii(buffer.AsSpan(0, read));

                if (TryBuildFromText(chunk, out result))
                {
                    return true;
                }

                carry = chunk.Length <= SerialScanOverlapChars
                    ? chunk
                    : chunk[^SerialScanOverlapChars..];
            }
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return false;
        }

        return false;
    }

    private static bool TryBuildFromText(string text, out DiscRawSerialProbeResult result)
    {
        result = default;

        string fallbackPlatform = text.IndexOf("BOOT2", StringComparison.OrdinalIgnoreCase) >= 0
            ? "PlayStation 2"
            : "PlayStation 1";

        if (!DiscSerialCatalog.TryExtract(
            text,
            DiscSerialScanProfile.Raw,
            fallbackPlatform,
            includeOptionalTail: false,
            serialSeparator: "-",
            out DiscSerialCatalogResult serial))
        {
            return false;
        }

        result = new DiscRawSerialProbeResult(
            true,
            serial.PlatformName,
            serial.Region,
            serial.Serial,
            RawSerialDetectedReasonKey);

        return true;
    }

    private static string BytesToSearchableAscii(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);

        foreach (byte value in bytes)
        {
            builder.Append(value is >= 32 and <= 126 ? (char)value : ' ');
        }

        return builder.ToString();
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

    private static bool IsReadableFilePath(string path)
    {
        try
        {
            ValidateReadableFilePath(path);
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex) || IsExpectedReadException(ex))
        {
            return false;
        }
    }

    private static void ValidateReadableFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(NoResultReasonKey, fullPath);
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = TrimDirectorySeparators(Path.GetFullPath(baseDirectory));
        string path = TrimDirectorySeparators(Path.GetFullPath(candidate));

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);

        if (!string.IsNullOrWhiteSpace(root)
            && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or InvalidDataException
        or PathTooLongException
        or System.Security.SecurityException;
}