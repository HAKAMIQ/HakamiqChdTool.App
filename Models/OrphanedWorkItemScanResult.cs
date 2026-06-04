using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Models;

internal sealed class OrphanedWorkItemScanResult
{
    public OrphanedWorkItemScanResult(
        IReadOnlyList<OrphanedWorkItem>? items,
        IReadOnlyList<string>? rootPaths = null)
    {
        Items = NormalizeItems(items);
        RootPaths = NormalizeRootPaths(rootPaths);
    }

    public IReadOnlyList<OrphanedWorkItem> Items { get; }

    public IReadOnlyList<string> RootPaths { get; }

    public bool HasItems => Items.Count > 0;

    public long TotalBytes => Items.Aggregate(
        0L,
        static (total, item) => AddSaturating(total, item.SizeBytes));

    public int TotalFiles => Items.Aggregate(
        0,
        static (total, item) => AddSaturating(total, item.FileCount));

    public static OrphanedWorkItemScanResult Empty { get; } =
        new(Array.Empty<OrphanedWorkItem>(), Array.Empty<string>());

    private static IReadOnlyList<OrphanedWorkItem> NormalizeItems(
        IEnumerable<OrphanedWorkItem>? items)
    {
        if (items is null)
        {
            return Array.Empty<OrphanedWorkItem>();
        }

        OrphanedWorkItem[] normalized = items
            .Where(static item => item is not null)
            .ToArray();

        return normalized.Length == 0
            ? Array.Empty<OrphanedWorkItem>()
            : normalized;
    }

    private static IReadOnlyList<string> NormalizeRootPaths(
        IEnumerable<string>? rootPaths)
    {
        if (rootPaths is null)
        {
            return Array.Empty<string>();
        }

        string[] normalized = rootPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0
            ? Array.Empty<string>()
            : normalized;
    }

    private static long AddSaturating(long left, long right)
    {
        if (right <= 0)
        {
            return left;
        }

        return long.MaxValue - left < right
            ? long.MaxValue
            : left + right;
    }

    private static int AddSaturating(int left, int right)
    {
        if (right <= 0)
        {
            return left;
        }

        return int.MaxValue - left < right
            ? int.MaxValue
            : left + right;
    }
}