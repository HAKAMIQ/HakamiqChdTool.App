using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.GameCatalog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services.M3u;

public sealed class MultiDiscSetDetector
{
    private readonly GameCatalogMediaScanner _mediaScanner;
    private readonly GameCatalogDiscSetResolver _discSetResolver;

    public MultiDiscSetDetector()
        : this(new GameCatalogMediaScanner(), new GameCatalogDiscSetResolver())
    {
    }

    public MultiDiscSetDetector(
        GameCatalogMediaScanner mediaScanner,
        GameCatalogDiscSetResolver discSetResolver)
    {
        ArgumentNullException.ThrowIfNull(mediaScanner);
        ArgumentNullException.ThrowIfNull(discSetResolver);

        _mediaScanner = mediaScanner;
        _discSetResolver = discSetResolver;
    }

    public IReadOnlyList<MultiDiscSet> Detect(IEnumerable<string> chdPaths)
    {
        ArgumentNullException.ThrowIfNull(chdPaths);

        GameCatalogEntry[] entries =
        [
            .. chdPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(path => _mediaScanner.Scan(path))
                .Where(static entry => entry.Type == GameCatalogEntryType.Chd)
        ];

        return
        [
            .. _discSetResolver
                .Resolve(entries)
                .Select(ToMultiDiscSet)
                .Where(static set => set is not null)
                .Cast<MultiDiscSet>()
        ];
    }

    private static MultiDiscSet? ToMultiDiscSet(GameCatalogDiscSet set)
    {
        if (set.Discs.Count < 2 || string.IsNullOrWhiteSpace(set.DirectoryPath))
        {
            return null;
        }

        MultiDiscItem[] discs =
        [
            .. set.Discs
                .OrderBy(static entry => entry.DiscNumber.GetValueOrDefault())
                .Select(static entry => new MultiDiscItem(
                    entry.Path,
                    Path.GetFileName(entry.Path),
                    entry.DiscNumber.GetValueOrDefault()))
        ];

        return new MultiDiscSet(
            set.Key,
            set.CollectionTitle,
            set.DirectoryPath,
            discs);
    }
}
