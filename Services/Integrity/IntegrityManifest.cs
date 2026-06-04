using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.Integrity;

public sealed class IntegrityManifest
{
    private string _format = string.Empty;

    private IntegrityManifestEntry[] _files = [];

    public string Format
    {
        get => _format;
        set => _format = NormalizeFormat(value);
    }

    public DateTimeOffset GeneratedUtc { get; set; }

    public IntegrityManifestEntry[] Files
    {
        get => CloneEntries(_files);
        set => _files = CloneEntries(value);
    }

    private static string NormalizeFormat(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static IntegrityManifestEntry[] CloneEntries(IntegrityManifestEntry[]? source)
    {
        if (source is null || source.Length == 0)
        {
            return [];
        }

        List<IntegrityManifestEntry> entries = new(source.Length);

        foreach (IntegrityManifestEntry? entry in source)
        {
            if (entry is null)
            {
                continue;
            }

            entries.Add(new IntegrityManifestEntry
            {
                Path = entry.Path,
                Sha256 = entry.Sha256
            });
        }

        return entries.Count == 0
            ? []
            : [.. entries];
    }
}