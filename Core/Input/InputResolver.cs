using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Core.Input;

public sealed class InputResolver : IInputResolver
{
    public IEnumerable<string> Resolve(string path)
    {
        return Resolve(path, SearchOption.AllDirectories);
    }

    public IEnumerable<string> Resolve(string path, SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath;
        try
        {
            fullPath = NormalizeFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            yield break;
        }

        if (File.Exists(fullPath))
        {
            if (!HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                yield return fullPath;
            }

            yield break;
        }

        if (!Directory.Exists(fullPath)
            || IsUnsafeRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            yield break;
        }

        foreach (string file in EnumerateFilesSafe(fullPath, searchOption))
        {
            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string rootPath, SearchOption searchOption)
    {
        Stack<string> pendingDirectories = [];
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            string currentDirectory = pendingDirectories.Pop();

            if (ShouldSkipDirectory(currentDirectory)
                || HasReparsePointInExistingPathFromVolumeRoot(currentDirectory))
            {
                continue;
            }

            foreach (string file in EnumerateDirectoryFilesSafe(currentDirectory))
            {
                if (!HasReparsePointInExistingPathFromVolumeRoot(file))
                {
                    yield return file;
                }
            }

            if (searchOption != SearchOption.AllDirectories)
            {
                continue;
            }

            foreach (string directory in EnumerateChildDirectoriesSafe(currentDirectory))
            {
                if (!ShouldSkipDirectory(directory)
                    && !HasReparsePointInExistingPathFromVolumeRoot(directory))
                {
                    pendingDirectories.Push(directory);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoryFilesSafe(string directoryPath)
    {
        IEnumerator<string>? enumerator;

        try
        {
            enumerator = Directory
                .EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .GetEnumerator();
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                string file;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    file = enumerator.Current;
                }
                catch (Exception ex) when (IsExpectedPathException(ex))
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

    private static IEnumerable<string> EnumerateChildDirectoriesSafe(string directoryPath)
    {
        IEnumerator<string>? enumerator;

        try
        {
            enumerator = Directory
                .EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .GetEnumerator();
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                string directory;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    directory = enumerator.Current;
                }
                catch (Exception ex) when (IsExpectedPathException(ex))
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
            return PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(Path.GetFileName(NormalizeFullPath(directoryPath)));
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsExistingPathReparsePoint(current))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = NormalizeFullPath(candidatePath);
        string root = NormalizeFullPath(rootPath);

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeRoot(string path)
    {
        try
        {
            string fullPath = NormalizeFullPath(path);
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
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

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsExpectedPathException(Exception ex)
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
