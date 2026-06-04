using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public readonly record struct ArchiveInspectionCacheKey(
    string FullPathKey,
    long LastWriteTimeUtcTicks,
    long Length)
{
    public static bool TryCreate(string path, out ArchiveInspectionCacheKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(Path.GetFullPath(path));
            if (!info.Exists)
            {
                return false;
            }

            key = new ArchiveInspectionCacheKey(
                info.FullName.ToUpperInvariant(),
                info.LastWriteTimeUtc.Ticks,
                info.Length);
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException;
    }
}
