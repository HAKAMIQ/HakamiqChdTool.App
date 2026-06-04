using System;

namespace HakamiqChdTool.App.Models;

internal sealed class OrphanedWorkItem
{
    public OrphanedWorkItem(
        string path,
        OrphanedWorkItemKind kind,
        long sizeBytes,
        int fileCount,
        DateTimeOffset lastWriteUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;
        Kind = kind;
        SizeBytes = Math.Max(0, sizeBytes);
        FileCount = Math.Max(0, fileCount);
        LastWriteUtc = lastWriteUtc;
    }

    public string Path { get; }

    public OrphanedWorkItemKind Kind { get; }

    public long SizeBytes { get; }

    public int FileCount { get; }

    public DateTimeOffset LastWriteUtc { get; }
}

internal enum OrphanedWorkItemKind
{
    File,
    Directory,
    PendingDirectory
}
