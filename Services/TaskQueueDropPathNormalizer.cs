using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public static class TaskQueueDropPathNormalizer
{
    public static IEnumerable<string> ExpandPathForDrop(
        string? path,
        SearchOption searchOption,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeRootPath(path, out string? fullPath))
        {
            yield break;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(fullPath))
        {
            yield return fullPath;
            yield break;
        }

        foreach (string candidatePath in EnumerateFilesSafe(fullPath, searchOption, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryNormalizeSafeFileUnderDirectory(candidatePath, fullPath, out string safePath))
            {
                yield return safePath;
            }
        }
    }

    public static bool TryNormalizeRootPath(
        string? path,
        [NotNullWhen(true)] out string? normalizedPath)
    {
        normalizedPath = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (File.Exists(fullPath))
            {
                if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
                {
                    return false;
                }

                normalizedPath = fullPath;
                return true;
            }

            if (!Directory.Exists(fullPath)
                || IsUnsafeRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            return false;
        }
    }

    public static bool IsSupportedExtension(string? extension) =>
        QueueInputClassifier.IsSupportedExtension(extension);

    private static IEnumerable<string> EnumerateFilesSafe(
        string rootPath,
        SearchOption searchOption,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDirectory = pendingDirectories.Pop();
            if (ShouldSkipDirectory(currentDirectory)
                || HasReparsePointInExistingPath(currentDirectory, rootPath))
            {
                continue;
            }

            foreach (string file in EnumerateDirectoryFilesSafe(currentDirectory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            if (searchOption != SearchOption.AllDirectories)
            {
                continue;
            }

            foreach (string directory in EnumerateChildDirectoriesSafe(currentDirectory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!ShouldSkipDirectory(directory)
                    && !HasReparsePointInExistingPath(directory, rootPath))
                {
                    pendingDirectories.Push(directory);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoryFilesSafe(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        IEnumerator<string>? enumerator;

        try
        {
            enumerator = Directory
                .EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .GetEnumerator();
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string file;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    file = enumerator.Current;
                }
                catch (Exception ex) when (IsExpectedPathExpansionException(ex))
                {
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateChildDirectoriesSafe(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        IEnumerator<string>? enumerator;

        try
        {
            enumerator = Directory
                .EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .GetEnumerator();
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    directory = enumerator.Current;
                }
                catch (Exception ex) when (IsExpectedPathExpansionException(ex))
                {
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    yield return directory;
                }
            }
        }
    }

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return true;
        }

        try
        {
            return PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(Path.GetFileName(Path.GetFullPath(directoryPath)));
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            return true;
        }
    }

    private static bool TryNormalizeSafeFileUnderDirectory(
        string candidatePath,
        string rootDirectory,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootDirectory))
        {
            return false;
        }

        try
        {
            string fullCandidatePath = Path.GetFullPath(candidatePath);
            string fullRootDirectory = Path.GetFullPath(rootDirectory);

            if (!File.Exists(fullCandidatePath)
                || !IsSamePathOrChild(fullCandidatePath, fullRootDirectory)
                || HasReparsePointInExistingPath(fullCandidatePath, fullRootDirectory))
            {
                return false;
            }

            normalizedPath = fullCandidatePath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            return false;
        }
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
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
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
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
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
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
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
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsExpectedPathExpansionException(ex))
        {
            return true;
        }
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsExpectedPathExpansionException(Exception ex)
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
