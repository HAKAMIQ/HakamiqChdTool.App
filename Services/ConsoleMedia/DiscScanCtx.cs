using System;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services.ConsoleMedia;

internal sealed class ConsoleDiscScanContext
{
    private const int MaxProbeBytes = 1024 * 1024;
    private const int ReadBufferSize = 256 * 1024;

    private readonly string _searchableText;
    private readonly string _searchablePathText;

    private ConsoleDiscScanContext(
        string path,
        string searchableText,
        string searchablePathText)
    {
        Path = path;
        _searchableText = searchableText;
        _searchablePathText = searchablePathText;
    }

    public string Path { get; }

    public static bool TryCreate(
        string path,
        out ConsoleDiscScanContext context)
    {
        context = null!;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;

        try
        {
            fullPath =
                System.IO.Path.GetFullPath(
                    path.Trim());
        }
        catch (Exception ex) when (
            IsExpectedPathException(ex))
        {
            return false;
        }

        if (!File.Exists(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(
                fullPath))
        {
            return false;
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ReadBufferSize,
                FileOptions.SequentialScan);

            long remaining =
                Math.Min(
                    stream.Length,
                    MaxProbeBytes);

            if (remaining <= 0)
            {
                return false;
            }

            StringBuilder searchableText =
                new((int)remaining);

            byte[] buffer =
                new byte[
                    Math.Min(
                        ReadBufferSize,
                        (int)remaining)];

            while (remaining > 0)
            {
                int targetRead =
                    (int)Math.Min(
                        buffer.Length,
                        remaining);

                int read =
                    stream.Read(
                        buffer,
                        0,
                        targetRead);

                if (read <= 0)
                {
                    break;
                }

                remaining -= read;

                AppendSearchableAscii(
                    searchableText,
                    buffer.AsSpan(0, read));
            }

            string searchablePathText =
                BuildSearchablePathText(
                    fullPath);

            if (searchableText.Length == 0
                && searchablePathText.Length == 0)
            {
                return false;
            }

            context = new ConsoleDiscScanContext(
                fullPath,
                searchableText.ToString(),
                searchablePathText);

            return true;
        }
        catch (Exception ex) when (
            IsExpectedReadException(ex)
            || IsExpectedPathException(ex))
        {
            return false;
        }
    }

    public bool ContainsText(
        string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && _searchableText.Contains(
                value,
                StringComparison.OrdinalIgnoreCase);
    }

    public bool ContainsPathHint(
        string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && _searchablePathText.Contains(
                value,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSearchablePathText(
        string fullPath)
    {
        string directory =
            System.IO.Path.GetDirectoryName(fullPath)
            ?? string.Empty;

        string name =
            System.IO.Path.GetFileNameWithoutExtension(
                fullPath);

        return string.Concat(
                directory,
                " ",
                name)
            .Replace('_', ' ')
            .Replace('-', ' ');
    }

    private static void AppendSearchableAscii(
        StringBuilder builder,
        ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            builder.Append(
                value is >= 32 and <= 126
                    ? (char)value
                    : ' ');
        }
    }

    private static bool
        HasReparsePointInExistingPathFromVolumeRoot(
            string candidatePath)
    {
        try
        {
            string candidate =
                NormalizeFullPath(
                    candidatePath);

            string? root =
                System.IO.Path.GetPathRoot(
                    candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(
                candidate,
                root);
        }
        catch (Exception ex) when (
            IsExpectedPathException(ex)
            || IsExpectedReadException(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(
        string candidatePath,
        string rootPath)
    {
        try
        {
            string candidate =
                NormalizeFullPath(
                    candidatePath);

            string root =
                NormalizeFullPath(
                    rootPath);

            if (!IsSamePathOrChild(
                    candidate,
                    root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current)
                        || Directory.Exists(current))
                    && IsExistingPathReparsePoint(
                        current))
                {
                    return true;
                }

                if (PathsEqual(
                        current,
                        root))
                {
                    return false;
                }

                string? parent =
                    Directory.GetParent(
                        current)?.FullName;

                if (string.IsNullOrWhiteSpace(parent)
                    || PathsEqual(
                        parent,
                        current))
                {
                    return true;
                }

                current =
                    NormalizeFullPath(
                        parent);
            }
        }
        catch (Exception ex) when (
            IsExpectedPathException(ex)
            || IsExpectedReadException(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(
        string path)
    {
        try
        {
            if (!File.Exists(path)
                && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path)
                    & FileAttributes.ReparsePoint)
                == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (
            IsExpectedPathException(ex)
            || IsExpectedReadException(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(
        string candidatePath,
        string rootPath)
    {
        string candidate =
            NormalizeFullPath(
                candidatePath);

        string root =
            NormalizeFullPath(
                rootPath);

        return string.Equals(
                candidate,
                root,
                StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(
                EnsureDirectorySeparatorSuffix(
                    root),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFullPath(
        string path)
    {
        string fullPath =
            System.IO.Path.GetFullPath(path);

        string? root =
            System.IO.Path.GetPathRoot(
                fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
    }

    private static string EnsureDirectorySeparatorSuffix(
        string path)
    {
        return path.EndsWith(
                   System.IO.Path.DirectorySeparatorChar)
            || path.EndsWith(
                System.IO.Path.AltDirectorySeparatorChar)
            ? path
            : path
                + System.IO.Path.DirectorySeparatorChar;
    }

    private static bool IsExpectedPathException(
        Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsExpectedReadException(
        Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException;
    }
}