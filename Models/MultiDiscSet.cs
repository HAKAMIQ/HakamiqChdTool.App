using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Models;

public sealed record MultiDiscSet
{
    public MultiDiscSet(
        string? key,
        string? title,
        string? directoryPath,
        IReadOnlyList<MultiDiscItem>? discs)
    {
        Key = key ?? string.Empty;
        Title = title ?? string.Empty;
        DirectoryPath = directoryPath ?? string.Empty;
        Discs = discs is null || discs.Count == 0
            ? Array.Empty<MultiDiscItem>()
            : discs.Where(static disc => disc is not null).ToArray();
    }

    public string Key { get; init; }

    public string Title { get; init; }

    public string DirectoryPath { get; init; }

    public IReadOnlyList<MultiDiscItem> Discs { get; init; }
}