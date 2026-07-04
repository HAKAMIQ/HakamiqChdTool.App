using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services.GameCatalog;

public sealed class GameCatalogDiscSetResolver
{
    public IReadOnlyList<GameCatalogDiscSet> Resolve(IEnumerable<GameCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        GameCatalogDiscSet[] sets =
        [
            .. entries
                .Where(static entry => entry is not null
                    && entry.IsValid
                    && entry.IsDisc
                    && entry.DiscNumber is > 0
                    && !string.IsNullOrWhiteSpace(entry.Path)
                    && !string.IsNullOrWhiteSpace(entry.CollectionTitle))
                .GroupBy(BuildKey, StringComparer.OrdinalIgnoreCase)
                .Select(CreateSet)
                .Where(static set => set is not null)
                .Cast<GameCatalogDiscSet>()
                .OrderBy(static set => set.DirectoryPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static set => set.CollectionTitle, StringComparer.OrdinalIgnoreCase)
        ];

        return sets;
    }

    private static string BuildKey(GameCatalogEntry entry)
    {
        string directory = NormalizeDirectory(GetSafeDirectoryName(entry.Path));
        string titleSource = string.IsNullOrWhiteSpace(entry.SortTitle)
            ? entry.CollectionTitle
            : entry.SortTitle;
        string title = NormalizeKeyPart(titleSource);

        return string.Join("|", directory, title);
    }

    private static GameCatalogDiscSet? CreateSet(IGrouping<string, GameCatalogEntry> group)
    {
        GameCatalogEntry[] normalizedEntries =
        [
            .. group
                .GroupBy(static entry => NormalizePath(entry.Path), StringComparer.OrdinalIgnoreCase)
                .Select(static pathGroup => pathGroup.First())
        ];

        IGrouping<int, GameCatalogEntry>[] discGroups =
        [
            .. normalizedEntries
                .GroupBy(static entry => entry.DiscNumber.GetValueOrDefault())
        ];

        if (discGroups.Length < 2)
        {
            return null;
        }

        List<GameCatalogEntry> selectedEntries = new(discGroups.Length);

        foreach (IGrouping<int, GameCatalogEntry> discGroup in discGroups)
        {
            GameCatalogEntry[] candidates =
            [
                .. discGroup
                    .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            ];

            if (candidates.Length != 1)
            {
                return null;
            }

            selectedEntries.Add(candidates[0]);
        }

        GameCatalogEntry[] ordered =
        [
            .. selectedEntries
                .OrderBy(static entry => entry.DiscNumber.GetValueOrDefault())
        ];

        if (!HasSequentialDiscNumbers(ordered) || !HasCompatibleDeclaredTotal(ordered))
        {
            return null;
        }

        string directory = GetSafeDirectoryName(ordered[0].Path);
        string normalizedDirectory = NormalizeDirectory(directory);

        if (string.IsNullOrWhiteSpace(normalizedDirectory)
            || ordered.Any(entry => !string.Equals(
                NormalizeDirectory(GetSafeDirectoryName(entry.Path)),
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new GameCatalogDiscSet(
            group.Key,
            ResolveRepresentativeValue(ordered, static entry => entry.Platform),
            ResolveRepresentativeValue(ordered, static entry => entry.Region),
            ordered[0].CollectionTitle,
            directory,
            ordered);
    }

    private static bool HasSequentialDiscNumbers(IReadOnlyList<GameCatalogEntry> ordered)
    {
        if (ordered.Count < 2 || ordered[0].DiscNumber != 1)
        {
            return false;
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].DiscNumber != index + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCompatibleDeclaredTotal(IReadOnlyList<GameCatalogEntry> ordered)
    {
        int highestDiscNumber = ordered
            .Select(static entry => entry.DiscNumber.GetValueOrDefault())
            .DefaultIfEmpty(0)
            .Max();

        if (highestDiscNumber != ordered.Count)
        {
            return false;
        }

        int[] declaredTotals =
        [
            .. ordered
                .Select(static entry => entry.TotalDiscs)
                .Where(static total => total.HasValue)
                .Select(static total => total.GetValueOrDefault())
                .Where(static total => total > 0)
                .Distinct()
        ];

        if (declaredTotals.Length == 0)
        {
            return true;
        }

        return declaredTotals.Length == 1 && declaredTotals[0] == highestDiscNumber;
    }

    private static string ResolveRepresentativeValue(
        IReadOnlyList<GameCatalogEntry> ordered,
        Func<GameCatalogEntry, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return ordered
            .Select(selector)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => NormalizeKeyPart(value), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First().Trim())
            .FirstOrDefault() ?? string.Empty;
    }

    private static string GetSafeDirectoryName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetDirectoryName(path.Trim()) ?? string.Empty;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return string.Empty;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
    }

    private static string NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(directory.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return directory.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
    }

    private static string NormalizeKeyPart(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToUpperInvariant();

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