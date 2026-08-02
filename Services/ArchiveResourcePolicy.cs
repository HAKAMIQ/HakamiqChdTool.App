using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

internal static class ArchiveResourcePolicy
{
    internal const int MaxArchiveEntries = 8_192;
    internal const long MaxExpandedBytes = 256L * 1024L * 1024L * 1024L;
    internal const long MinimumFreeSpaceReserveBytes = 2L * 1024L * 1024L * 1024L;

    internal const int MaxRedumpEntries = 50_000;
    internal const long MaxRedumpDownloadBytes = 2L * 1024L * 1024L * 1024L;
    internal const long MaxRedumpExpandedBytes = 8L * 1024L * 1024L * 1024L;
    internal const long MaxRedumpSingleEntryBytes = 1L * 1024L * 1024L * 1024L;

    internal const string ResourceLimitMessageKey = "LocArchive_ResourceLimitExceeded";

    internal static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }

    internal static void ThrowIfEntryCountExceeded(int entryCount, int maximum = MaxArchiveEntries)
    {
        if (entryCount > maximum)
        {
            throw new ArchiveResourceLimitException("entry-count");
        }
    }

    internal static void ThrowIfExpandedBytesExceeded(long expandedBytes, long maximum = MaxExpandedBytes)
    {
        if (expandedBytes < 0 || expandedBytes > maximum)
        {
            throw new ArchiveResourceLimitException("expanded-bytes");
        }
    }

    internal static void EnsureInitialFreeSpace(string destinationPath, long plannedBytes)
    {
        ThrowIfExpandedBytesExceeded(plannedBytes);

        long available = GetAvailableFreeSpace(destinationPath);
        long required = SaturatingAdd(plannedBytes, MinimumFreeSpaceReserveBytes);
        if (available < required)
        {
            throw new ArchiveResourceLimitException("free-space");
        }
    }

    internal static long GetAvailableFreeSpace(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArchiveResourceLimitException("volume-root");
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (ArchiveResourceLimitException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            throw new ArchiveResourceLimitException("free-space-unavailable", ex);
        }
    }
}

internal sealed class ArchiveExtractionBudget
{
    private const long FreeSpaceRecheckIntervalBytes = 64L * 1024L * 1024L;

    private readonly string destinationPath;
    private readonly long maximumBytes;
    private long nextFreeSpaceCheckBytes;

    internal ArchiveExtractionBudget(string destinationPath, long plannedBytes, long maximumBytes = ArchiveResourcePolicy.MaxExpandedBytes)
    {
        this.destinationPath = Path.GetFullPath(destinationPath);
        this.maximumBytes = maximumBytes;
        ArchiveResourcePolicy.ThrowIfExpandedBytesExceeded(plannedBytes, maximumBytes);
        ArchiveResourcePolicy.EnsureInitialFreeSpace(this.destinationPath, plannedBytes);
        nextFreeSpaceCheckBytes = Math.Min(FreeSpaceRecheckIntervalBytes, Math.Max(1, plannedBytes));
    }

    internal long WrittenBytes { get; private set; }

    internal void AddWrittenBytes(int byteCount)
    {
        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        WrittenBytes = ArchiveResourcePolicy.SaturatingAdd(WrittenBytes, byteCount);
        ArchiveResourcePolicy.ThrowIfExpandedBytesExceeded(WrittenBytes, maximumBytes);

        if (WrittenBytes < nextFreeSpaceCheckBytes)
        {
            return;
        }

        if (ArchiveResourcePolicy.GetAvailableFreeSpace(destinationPath)
            < ArchiveResourcePolicy.MinimumFreeSpaceReserveBytes)
        {
            throw new ArchiveResourceLimitException("free-space-reserve");
        }

        nextFreeSpaceCheckBytes = ArchiveResourcePolicy.SaturatingAdd(
            WrittenBytes,
            FreeSpaceRecheckIntervalBytes);
    }
}

internal sealed class ArchiveResourceLimitException : IOException
{
    internal ArchiveResourceLimitException(string reason)
        : base(ArchiveResourcePolicy.ResourceLimitMessageKey + ":" + reason)
    {
    }

    internal ArchiveResourceLimitException(string reason, Exception innerException)
        : base(ArchiveResourcePolicy.ResourceLimitMessageKey + ":" + reason, innerException)
    {
    }
}
