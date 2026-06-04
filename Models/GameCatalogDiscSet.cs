using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Models;

public sealed record GameCatalogDiscSet
{
    public GameCatalogDiscSet(
        string? key,
        string? platform,
        string? region,
        string? collectionTitle,
        string? directoryPath,
        IReadOnlyList<GameCatalogEntry>? discs)
    {
        Key = key ?? string.Empty;
        Platform = platform ?? string.Empty;
        Region = region ?? string.Empty;
        CollectionTitle = collectionTitle ?? string.Empty;
        DirectoryPath = directoryPath ?? string.Empty;
        Discs = discs is null || discs.Count == 0
            ? Array.Empty<GameCatalogEntry>()
            : discs.Where(static entry => entry is not null).ToArray();
    }

    public string Key { get; init; }

    public string Platform { get; init; }

    public string Region { get; init; }

    public string CollectionTitle { get; init; }

    public string DirectoryPath { get; init; }

    public IReadOnlyList<GameCatalogEntry> Discs { get; init; }
}