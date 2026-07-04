using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Models;

internal sealed class OrphanedWorkItemScanResult(
    IReadOnlyList<OrphanedWorkItem>? items,
    IReadOnlyList<string>? rootPaths = null)
{
    public IReadOnlyList<OrphanedWorkItem> Items { get; } = NormalizeItems(items);

    public IReadOnlyList<string> RootPaths { get; } = NormalizeRootPaths(rootPaths);

    public bool HasItems => Items.Count > 0;

    public long TotalBytes => Items.Aggregate(
        0L,
        static (total, item) => AddSaturating(total, item.SizeBytes));

    public int TotalFiles => Items.Aggregate(
        0,
        static (total, item) => AddSaturating(total, item.FileCount));

    public static OrphanedWorkItemScanResult Empty { get; } = new([], []);

    private static OrphanedWorkItem[] NormalizeItems(
        IEnumerable<OrphanedWorkItem>? items)
    {
        if (items is null)
        {
            return [];
        }

        OrphanedWorkItem[] normalized =
        [
            .. items.Where(static item => item is not null)
        ];

        return normalized.Length == 0
            ? []
            : normalized;
    }

    private static string[] NormalizeRootPaths(
        IEnumerable<string>? rootPaths)
    {
        if (rootPaths is null)
        {
            return [];
        }

        string[] normalized =
        [
            .. rootPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        return normalized.Length == 0
            ? []
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