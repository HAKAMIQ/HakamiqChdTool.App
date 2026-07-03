using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

public sealed record RedumpLocalLibraryIndexResult(
    string RootPath,
    int TotalDatXmlFiles,
    int PlatformCount,
    int SelectedCount,
    int OlderCount,
    int DuplicateCount,
    int VariantCount,
    int ReadErrorCount,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    IReadOnlyList<RedumpLocalLibraryDatEntry> Entries,
    IReadOnlyList<string> Errors)
{
    public bool HasImportableEntries => SelectedCount > 0;

    public static RedumpLocalLibraryIndexResult Empty(
        string rootPath,
        DateTime startedUtc,
        DateTime finishedUtc,
        string error)
    {
        return new RedumpLocalLibraryIndexResult(
            rootPath,
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            startedUtc,
            finishedUtc,
            Array.Empty<RedumpLocalLibraryDatEntry>(),
            new[] { error });
    }
}
