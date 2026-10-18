using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Core.Library;

public sealed record DatHeader(
    string? Name,
    string? Description,
    string? Version,
    string? Date,
    string? Author,
    string? Homepage);

/// <summary>
/// One ROM record. Hashes are lowercase hex; absent means the DAT did not provide it.
/// Name is empty for hash-only records (some libretro DATs list a CRC32 and nothing else).
/// </summary>
public sealed record DatRom(
    string Name,
    long? Size,
    string? Crc32,
    string? Md5,
    string? Sha1)
{
    /// <summary>
    /// True when the name is not a plain file name (separators, traversal, invalid
    /// characters). DAT names are data only and must never be used as paths.
    /// </summary>
    public bool NameIsUnsafe => DatNames.IsUnsafeFileName(Name);
}

public sealed record DatGame(
    string Name,
    string? Description,
    string? Region,
    string? Version,
    string? CloneOf,
    IReadOnlyList<DatRom> Roms);

public sealed class DatFile(
    string sourcePath,
    DatHeader header,
    IReadOnlyList<DatGame> games)
{
    public string SourcePath { get; } = sourcePath;

    public DatHeader Header { get; } = header;

    public IReadOnlyList<DatGame> Games { get; } = games;

    /// <summary>
    /// Catalog platform exactly as the DAT header names it (for example
    /// "Sony - PlayStation"). Null when the header carries no name; never inferred.
    /// </summary>
    public string? Platform =>
        string.IsNullOrWhiteSpace(Header.Name) ? null : Header.Name.Trim();
}

public enum DatParseStatus
{
    Parsed,
    Unsupported,
    Malformed,
    TooLarge,
    Unreadable,
    Cancelled
}

public sealed record DatIssue(string Code, string Message);

public sealed class DatParseResult(
    DatParseStatus status,
    DatFile? dat,
    IReadOnlyList<DatIssue> issues,
    int rejectedRecords)
{
    public DatParseStatus Status { get; } = status;

    /// <summary>Set only when Status is Parsed.</summary>
    public DatFile? Dat { get; } = dat;

    public IReadOnlyList<DatIssue> Issues { get; } = issues;

    /// <summary>Records dropped because they were malformed. Parsed with rejects is not clean.</summary>
    public int RejectedRecords { get; } = rejectedRecords;
}

internal static class DatNames
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public const int MaxComponentLength = 255;

    /// <summary>True when the value is not usable as a single Windows path component.</summary>
    public static bool IsUnsafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > MaxComponentLength
            || name is "." or ".."
            || name.EndsWith('.')
            || name.EndsWith(' ')
            || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/')
            || name.Contains('\\'))
        {
            return true;
        }

        int dot = name.IndexOf('.');
        string stem = dot < 0 ? name : name[..dot];
        return ReservedDeviceNames.Contains(stem.TrimEnd());
    }
}
