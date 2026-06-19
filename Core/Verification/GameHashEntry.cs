namespace HakamiqChdTool.App.Core.Verification;

public sealed record GameHashEntry
{
    public string SystemName { get; init; } = string.Empty;

    public string GameName { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string DumpStatus { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long? SizeBytes { get; init; }

    public string CRC32 { get; init; } = string.Empty;

    public string MD5 { get; init; } = string.Empty;

    public string SHA1 { get; init; } = string.Empty;

    public string SHA256 { get; init; } = string.Empty;

    public string SourceDatabase { get; init; } = string.Empty;

    public string SourceDate { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;
}
