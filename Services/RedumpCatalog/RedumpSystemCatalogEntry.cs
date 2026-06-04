using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Services.RedumpCatalog;

internal sealed record RedumpSystemCatalogEntry
{
    public RedumpSystemCatalogEntry(
        string key,
        string labelKey,
        string englishName,
        string mediaTypes,
        bool requiresDumperAccess,
        bool isNonRedump,
        bool isDefunct,
        IReadOnlyList<RedumpArtifactEndpoint> artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(englishName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaTypes);
        ArgumentNullException.ThrowIfNull(artifacts);

        Key = key.Trim();
        LabelKey = labelKey.Trim();
        EnglishName = englishName.Trim();
        MediaTypes = mediaTypes.Trim();
        RequiresDumperAccess = requiresDumperAccess;
        IsNonRedump = isNonRedump;
        IsDefunct = isDefunct;
        Artifacts = ToReadOnlyArtifactList(artifacts);
    }

    public string Key { get; }

    public string LabelKey { get; }

    public string EnglishName { get; }

    public string MediaTypes { get; }

    public bool RequiresDumperAccess { get; }

    public bool IsNonRedump { get; }

    public bool IsDefunct { get; }

    public IReadOnlyList<RedumpArtifactEndpoint> Artifacts { get; }

    private static ReadOnlyCollection<RedumpArtifactEndpoint> ToReadOnlyArtifactList(
        IReadOnlyList<RedumpArtifactEndpoint> source)
    {
        List<RedumpArtifactEndpoint> items = new(source.Count);

        foreach (RedumpArtifactEndpoint item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            items.Add(item);
        }

        return new ReadOnlyCollection<RedumpArtifactEndpoint>(items);
    }
}