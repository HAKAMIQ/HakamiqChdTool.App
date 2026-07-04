using System;
using System.IO;

namespace HakamiqChdTool.App.Services.Safety;

internal sealed class SafetyPathPolicy
{
    private readonly bool _rejectReparsePoints;

    internal SafetyPathPolicy(bool rejectReparsePoints = true)
    {
        _rejectReparsePoints = rejectReparsePoints;
    }

    public static SafetyPathPolicy Shared { get; } = new(rejectReparsePoints: true);

    public bool TryGetExistingFilePath(string? path, out string fullPath)
    {
        fullPath = string.Empty;

        if (!TryGetFullPath(path, out string candidate)
            || !File.Exists(candidate)
            || _rejectReparsePoints && HasReparsePointInExistingPathFromVolumeRoot(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public bool TryGetExistingDirectoryPath(
        string? path,
        out string fullPath,
        bool rejectVolumeRoot = true)
    {
        fullPath = string.Empty;

        if (!TryGetFullPath(path, out string candidate)
            || !Directory.Exists(candidate)
            || rejectVolumeRoot && IsUnsafeRoot(candidate)
            || _rejectReparsePoints && HasReparsePointInExistingPathFromVolumeRoot(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public bool IsSafeExistingFilePath(string? path) =>
        TryGetExistingFilePath(path, out _);

    public bool IsSafeExistingDirectoryPath(string? path, bool rejectVolumeRoot = true) =>
        TryGetExistingDirectoryPath(path, out _, rejectVolumeRoot);

    public static string NormalizeForAdvisoryKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return TryGetFullPath(path, out string fullPath)
            ? TrimDirectorySeparators(fullPath)
            : path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryGetFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path.Trim());
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
