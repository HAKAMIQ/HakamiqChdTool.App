using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Services.M3u;

public sealed class M3uPlaylistGenerator : IM3uPlaylistGenerator
{
    private const string M3uArtifactKind = "M3U";
    private const string M3uGenerationFailedMessageCode = "LocPostProcessing_M3uGenerationFailed";

    private readonly ILogger _logger;

    public M3uPlaylistGenerator()
        : this(Log.ForContext<M3uPlaylistGenerator>())
    {
    }

    internal M3uPlaylistGenerator(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public M3uGenerationResult Generate(IEnumerable<MultiDiscSet> sets, bool overwriteExisting)
    {
        ArgumentNullException.ThrowIfNull(sets);

        int candidateSetCount = 0;
        int generatedCount = 0;
        int skippedExistingCount = 0;
        List<string> generatedPaths = [];
        List<PostConversionArtifactFailure> failures = [];

        foreach (MultiDiscSet set in sets)
        {
            candidateSetCount++;

            try
            {
                string? generatedPath = TryGenerateOne(set, overwriteExisting, out bool skippedExisting);
                if (skippedExisting)
                {
                    skippedExistingCount++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(generatedPath))
                {
                    generatedCount++;
                    generatedPaths.Add(generatedPath);
                }
            }
            catch (Exception ex) when (IsPathOrIoFailure(ex))
            {
                failures.Add(new PostConversionArtifactFailure
                {
                    ArtifactKind = M3uArtifactKind,
                    MessageCode = M3uGenerationFailedMessageCode,
                    TargetPath = ResolveFailureTarget(set)
                });

                _logger.Warning(
                    ex,
                    "M3U playlist generation failed. SetTitle={SetTitle}; Directory={Directory}",
                    set.Title,
                    set.DirectoryPath);
            }
        }

        return new M3uGenerationResult(
            candidateSetCount,
            generatedCount,
            skippedExistingCount,
            generatedPaths,
            failures);
    }

    private static string? TryGenerateOne(MultiDiscSet set, bool overwriteExisting, out bool skippedExisting)
    {
        skippedExisting = false;

        if (set.Discs.Count < 2 || string.IsNullOrWhiteSpace(set.DirectoryPath))
        {
            return null;
        }

        if (!TryNormalizeSafeDirectory(set.DirectoryPath, out string? directory))
        {
            throw new IOException();
        }

        string safeTitle = SanitizeFileName(set.Title);
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            return null;
        }

        string playlistPath = Path.GetFullPath(Path.Combine(directory, safeTitle + ".m3u"));
        if (!IsPathInsideDirectory(playlistPath, directory))
        {
            throw new IOException();
        }

        if (File.Exists(playlistPath))
        {
            if (HasReparsePointInExistingPath(playlistPath, directory))
            {
                throw new IOException();
            }

            if (!overwriteExisting)
            {
                skippedExisting = true;
                return null;
            }
        }

        List<string> entries = new(set.Discs.Count);

        foreach (MultiDiscItem disc in set.Discs
                     .OrderBy(static disc => disc.DiscNumber)
                     .ThenBy(static disc => disc.FileName, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryBuildRelativeEntry(directory, disc.FilePath, out string? entry))
            {
                return null;
            }

            entries.Add(entry);
        }

        if (entries.Count < 2
            || entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
        {
            return null;
        }

        string content = string.Join(Environment.NewLine, entries) + Environment.NewLine;
        WriteTextAtomically(playlistPath, content, overwriteExisting);

        return playlistPath;
    }

    private static string? ResolveFailureTarget(MultiDiscSet set)
    {
        if (string.IsNullOrWhiteSpace(set.DirectoryPath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(set.DirectoryPath);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return null;
        }
    }

    private static bool TryBuildRelativeEntry(
        string directory,
        string filePath,
        [NotNullWhen(true)] out string? entry)
    {
        entry = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            string fullDirectory = Path.GetFullPath(directory);
            string fullPath = Path.GetFullPath(filePath);

            if (!IsPathInsideDirectory(fullPath, fullDirectory)
                || !File.Exists(fullPath)
                || !string.Equals(Path.GetExtension(fullPath), ".chd", StringComparison.OrdinalIgnoreCase)
                || HasReparsePointInExistingPath(fullPath, fullDirectory))
            {
                return false;
            }

            string relative = Path.GetRelativePath(fullDirectory, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            if (!IsSafeRelativePlaylistEntry(relative))
            {
                return false;
            }

            entry = relative;
            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    private static void WriteTextAtomically(string targetPath, string content, bool overwriteExisting)
    {
        string? directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException();
        }

        string fullDirectory = Path.GetFullPath(directory);
        string fullTargetPath = Path.GetFullPath(targetPath);

        if (!IsPathInsideDirectory(fullTargetPath, fullDirectory)
            || HasReparsePointInExistingPathFromVolumeRoot(fullDirectory)
            || File.Exists(fullTargetPath) && HasReparsePointInExistingPath(fullTargetPath, fullDirectory))
        {
            throw new IOException();
        }

        string targetFileName = Path.GetFileName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(targetFileName))
        {
            throw new IOException();
        }

        string tempPath = Path.Combine(
            fullDirectory,
            $"{targetFileName}.{Guid.NewGuid():N}.tmp");

        if (!IsPathInsideDirectory(tempPath, fullDirectory))
        {
            throw new IOException();
        }

        try
        {
            using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    .GetBytes(content);

                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (HasReparsePointInExistingPath(tempPath, fullDirectory)
                || File.Exists(fullTargetPath) && HasReparsePointInExistingPath(fullTargetPath, fullDirectory))
            {
                throw new IOException();
            }

            File.Move(tempPath, fullTargetPath, overwrite: overwriteExisting);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static bool TryNormalizeSafeDirectory(
        string directoryPath,
        [NotNullWhen(true)] out string? directory)
    {
        directory = null;

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            string fullDirectory = Path.GetFullPath(directoryPath.Trim());

            if (IsUnsafeRoot(fullDirectory)
                || HasReparsePointInExistingPathFromVolumeRoot(fullDirectory))
            {
                return false;
            }

            Directory.CreateDirectory(fullDirectory);

            if (!Directory.Exists(fullDirectory)
                || HasReparsePointInExistingPathFromVolumeRoot(fullDirectory))
            {
                return false;
            }

            directory = fullDirectory;
            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    private static string SanitizeFileName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(title.Length);

        foreach (char ch in title.Trim())
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        string value = builder.ToString().Trim(' ', '.');
        while (value.Contains("__", StringComparison.Ordinal))
        {
            value = value.Replace("__", "_", StringComparison.Ordinal);
        }

        if (value.Length > 120)
        {
            value = value[..120].Trim(' ', '.');
        }

        return string.IsNullOrWhiteSpace(value) || IsReservedWindowsDeviceName(value)
            ? string.Empty
            : value;
    }

    private static bool IsReservedWindowsDeviceName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);

        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || IsReservedNumberedDeviceName(stem, "COM")
            || IsReservedNumberedDeviceName(stem, "LPT");
    }

    private static bool IsReservedNumberedDeviceName(string stem, string prefix)
    {
        return stem.Length == 4
            && stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && stem[3] is >= '1' and <= '9';
    }

    private static bool IsSafeRelativePlaylistEntry(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)
            || relative.Contains('\0', StringComparison.Ordinal)
            || relative.Contains("//", StringComparison.Ordinal)
            || relative.StartsWith('/')
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (string segment in segments)
        {
            if (segment.Length == 0
                || string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPathInsideDirectory(string fullPath, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string normalizedPath = Path.GetFullPath(fullPath);

        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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

    private static bool IsUnsafeRoot(string path)
    {
        try
        {
            string fullPath = TrimDirectorySeparators(Path.GetFullPath(path));
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath) && !HasReparsePointInExistingPathFromVolumeRoot(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
        }
    }

    private static bool IsPathOrIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }
}