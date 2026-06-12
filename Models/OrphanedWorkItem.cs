using System;

namespace HakamiqChdTool.App.Models;

internal sealed class OrphanedWorkItem(
    string path,
    OrphanedWorkItemKind kind,
    long sizeBytes,
    int fileCount,
    DateTimeOffset lastWriteUtc)
{
    public string Path { get; } = ValidatePath(path);

    public OrphanedWorkItemKind Kind { get; } = kind;

    public long SizeBytes { get; } = Math.Max(0, sizeBytes);

    public int FileCount { get; } = Math.Max(0, fileCount);

    public DateTimeOffset LastWriteUtc { get; } = lastWriteUtc;

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return path;
    }
}

internal enum OrphanedWorkItemKind
{
    File,
    Directory,
    PendingDirectory
}