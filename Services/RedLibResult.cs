using System;

namespace HakamiqChdTool.App.Services;

public sealed class RedumpLocalLibraryScanResult
{
    public string RootPath { get; init; } = string.Empty;

    public int TotalFileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    public int TopLevelFolderCount { get; init; }

    public int DatFileCount { get; init; }

    public int XmlFileCount { get; init; }

    public int CueFileCount { get; init; }

    public int GdiFileCount { get; init; }

    public int SbiFileCount { get; init; }

    public int LsdFileCount { get; init; }

    public int KeyFileCount { get; init; }

    public int DkeyFileCount { get; init; }

    public DateTime? NewestModifiedLocal { get; init; }

    public int DatXmlFileCount => DatFileCount + XmlFileCount;

    public int SubchannelFileCount => SbiFileCount + LsdFileCount;

    public int DiscKeyFileCount => KeyFileCount + DkeyFileCount;

    public bool HasImportableDatFiles => DatXmlFileCount > 0;
}
