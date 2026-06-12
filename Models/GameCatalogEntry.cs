namespace HakamiqChdTool.App.Models;

public sealed record GameCatalogEntry
{
    public GameCatalogEntryType Type { get; init; } = GameCatalogEntryType.Unknown;

    public string Platform { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Serial { get; init; } = string.Empty;

    public string Crc { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SortTitle { get; init; } = string.Empty;

    public string RedumpTitle { get; init; } = string.Empty;

    public string CollectionTitle { get; init; } = string.Empty;

    public int? DiscNumber { get; init; }

    public int? TotalDiscs { get; init; }

    public bool IsDisc { get; init; }

    public bool IsValid { get; init; }
}