using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.Core.Workflow.Extraction;

internal sealed record ExtractionOutputBundle(
    ExtractionOutputKind Kind,
    string PrimaryPath,
    IReadOnlyList<string> FilePaths,
    long TotalBytes)
{
    public static ExtractionOutputBundle Create(
        ExtractionOutputKind kind,
        string primaryPath,
        IEnumerable<(string Path, long Length)> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryPath);
        ArgumentNullException.ThrowIfNull(files);

        (string Path, long Length)[] materialized =
        [
            .. files
                .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
                .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
        ];

        return new ExtractionOutputBundle(
            kind,
            primaryPath,
            [.. materialized.Select(static item => item.Path)],
            materialized.Sum(static item => Math.Max(0L, item.Length)));
    }
}
