using System;

namespace HakamiqChdTool.App.Services;

public sealed record RedumpLocalLibraryDatEntry(
    string FilePath,
    string FileName,
    string DirectoryPath,
    string Extension,
    string PlatformKey,
    string? Name,
    string? Description,
    string? Version,
    DateTime? DatDateUtc,
    int? PreviewGameCount,
    bool IsSelected,
    string Status,
    string? Reason,
    long FileSizeBytes,
    DateTime LastWriteTimeUtc);
