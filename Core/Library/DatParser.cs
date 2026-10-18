using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

namespace HakamiqChdTool.App.Core.Library;

/// <summary>
/// Reads user-supplied Logiqx XML DAT files (&lt;datafile&gt; with &lt;game&gt; or &lt;machine&gt;
/// records) and ClrMamePro text DATs (see DatClrMame.cs). RomCenter, MAME XML and other formats are reported as Unsupported,
/// never guessed. DTDs are ignored and never resolved; size and record counts are bounded.
/// Names found in a DAT are data only and are not trusted as paths.
/// </summary>
public static class DatParser
{
    public const long MaxFileBytes = 256L * 1024 * 1024;
    public const int MaxGames = 250_000;
    public const int MaxRoms = 1_000_000;
    public const int MaxTextLength = 1024;
    private const int MaxReportedIssues = 100;
    private const int SniffBytes = 512;

    public static DatParseResult Parse(string path, CancellationToken cancellationToken)
    {
        List<DatIssue> issues = [];

        string fullPath;
        long length;

        try
        {
            fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);

            if (!info.Exists)
            {
                return Fail(DatParseStatus.Unreadable, "dat-missing", "DAT file does not exist.", issues);
            }

            length = info.Length;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Fail(DatParseStatus.Unreadable, "dat-unreadable", ex.Message, issues);
        }

        if (length > MaxFileBytes)
        {
            return Fail(DatParseStatus.TooLarge, "dat-too-large", $"DAT is {length} bytes; the limit is {MaxFileBytes}.", issues);
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            DatFormat format = SniffFormat(stream);
            stream.Position = 0;

            return format switch
            {
                DatFormat.LogiqxXml => ParseXml(stream, fullPath, issues, cancellationToken),
                DatFormat.ClrMameProText => DatClrMamePro.Parse(stream, fullPath, issues, cancellationToken),
                _ => Fail(DatParseStatus.Unsupported, "format-unrecognized", "DAT format is not supported (Logiqx XML and ClrMamePro text are).", issues)
            };
        }
        catch (OperationCanceledException)
        {
            return Fail(DatParseStatus.Cancelled, "cancelled", "Parsing was cancelled.", issues);
        }
        catch (XmlException ex)
        {
            return Fail(DatParseStatus.Malformed, "xml-malformed", ex.Message, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Fail(DatParseStatus.Unreadable, "dat-unreadable", ex.Message, issues);
        }
    }

    private enum DatFormat
    {
        Unknown,
        LogiqxXml,
        ClrMameProText
    }

    private static DatFormat SniffFormat(Stream stream)
    {
        byte[] buffer = new byte[SniffBytes];
        int read = stream.Read(buffer, 0, buffer.Length);
        string text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('﻿', ' ', '\t', '\r', '\n');

        if (text.StartsWith('<'))
        {
            return DatFormat.LogiqxXml;
        }

        // "keyword (" is what both the header block and game blocks of a text DAT start with.
        foreach (string keyword in new[] { "clrmamepro", "game", "machine" })
        {
            if (text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                && text[keyword.Length..].TrimStart().StartsWith('('))
            {
                return DatFormat.ClrMameProText;
            }
        }

        return DatFormat.Unknown;
    }

    private static DatParseResult ParseXml(
        Stream stream,
        string fullPath,
        List<DatIssue> issues,
        CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = MaxFileBytes
        };

        using XmlReader reader = XmlReader.Create(stream, settings);

        if (reader.MoveToContent() != XmlNodeType.Element
            || !reader.LocalName.Equals("datafile", StringComparison.Ordinal))
        {
            return Fail(DatParseStatus.Unsupported, "xml-root-unsupported", "XML root is not <datafile> (MAME XML and software lists are not supported).", issues);
        }

        DatHeader header = new(null, null, null, null, null, null);
        List<DatGame> games = [];
        int romCount = 0;
        int rejected = 0;
        int depth = reader.Depth;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.Depth != depth + 1)
            {
                continue;
            }

            if (reader.LocalName.Equals("header", StringComparison.OrdinalIgnoreCase))
            {
                header = ReadHeader(reader.ReadSubtree());
                continue;
            }

            if (!reader.LocalName.Equals("game", StringComparison.OrdinalIgnoreCase)
                && !reader.LocalName.Equals("machine", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (games.Count >= MaxGames)
            {
                return Fail(DatParseStatus.TooLarge, "dat-too-many-games", $"More than {MaxGames} games.", issues);
            }

            DatGame? game = ReadGame(reader.ReadSubtree(), ref romCount, ref rejected, issues, cancellationToken);

            if (romCount > MaxRoms)
            {
                return Fail(DatParseStatus.TooLarge, "dat-too-many-roms", $"More than {MaxRoms} ROM records.", issues);
            }

            if (game is null)
            {
                rejected++;
                continue;
            }

            games.Add(game);
        }

        return new DatParseResult(
            DatParseStatus.Parsed,
            new DatFile(fullPath, header, games),
            issues,
            rejected);
    }

    private static DatHeader ReadHeader(XmlReader reader)
    {
        using (reader)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            reader.Read();
            int depth = reader.Depth;
            bool more = reader.Read();

            while (more)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.Depth == depth + 1
                    && reader.LocalName is "name" or "description" or "version" or "date" or "author" or "homepage")
                {
                    string name = reader.LocalName;
                    values.TryAdd(name, Bound(reader.ReadElementContentAsString()));
                    more = !reader.EOF;
                    continue;
                }

                more = reader.Read();
            }

            return new DatHeader(
                Get(values, "name"),
                Get(values, "description"),
                Get(values, "version"),
                Get(values, "date"),
                Get(values, "author"),
                Get(values, "homepage"));
        }
    }

    private static DatGame? ReadGame(
        XmlReader reader,
        ref int romCount,
        ref int rejected,
        List<DatIssue> issues,
        CancellationToken cancellationToken)
    {
        using (reader)
        {
            reader.Read();

            string? name = Bound(reader.GetAttribute("name"));
            string? region = NullIfEmpty(Bound(reader.GetAttribute("region")));
            string? version = NullIfEmpty(Bound(FirstNonEmpty(reader.GetAttribute("version"), reader.GetAttribute("rev"))));
            string? cloneOf = NullIfEmpty(Bound(reader.GetAttribute("cloneof")));
            string? description = null;
            List<DatRom> roms = [];
            int depth = reader.Depth;
            bool more = reader.Read();

            while (more)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element || reader.Depth != depth + 1)
                {
                    more = reader.Read();
                    continue;
                }

                if (reader.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase))
                {
                    description = NullIfEmpty(Bound(reader.ReadElementContentAsString()));
                    more = !reader.EOF;
                    continue;
                }
                else if (reader.LocalName.Equals("release", StringComparison.OrdinalIgnoreCase))
                {
                    region ??= NullIfEmpty(Bound(reader.GetAttribute("region")));
                }
                else if (reader.LocalName.Equals("rom", StringComparison.OrdinalIgnoreCase))
                {
                    romCount++;

                    if (romCount > MaxRoms)
                    {
                        return null;
                    }

                    DatRom? rom = ReadRom(reader, out string? problem);

                    if (rom is null)
                    {
                        rejected++;
                        AddIssue(issues, "rom-rejected", $"Game '{name}': {problem}");
                    }
                    else
                    {
                        roms.Add(rom);
                    }
                }

                more = reader.Read();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                AddIssue(issues, "game-unnamed", "A game record without a name was rejected.");
                return null;
            }

            return new DatGame(name, description, region, version, cloneOf, roms);
        }
    }

    private static DatRom? ReadRom(XmlReader reader, out string? problem)
    {
        problem = null;
        string? name = Bound(reader.GetAttribute("name"));

        if (string.IsNullOrWhiteSpace(name))
        {
            problem = "ROM without a name.";
            return null;
        }

        long? size = null;
        string? sizeText = reader.GetAttribute("size");

        if (!string.IsNullOrWhiteSpace(sizeText))
        {
            if (!long.TryParse(sizeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long parsed))
            {
                problem = $"ROM '{name}' has an invalid size.";
                return null;
            }

            size = parsed;
        }

        string? crc = Hex(reader.GetAttribute("crc"), 8, name, "crc", ref problem);
        string? md5 = Hex(reader.GetAttribute("md5"), 32, name, "md5", ref problem);
        string? sha1 = Hex(reader.GetAttribute("sha1"), 40, name, "sha1", ref problem);

        return problem is null ? new DatRom(name, size, crc, md5, sha1) : null;
    }

    internal static string? Hex(string? value, int length, string rom, string field, ref string? problem)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim().ToLowerInvariant();

        if (trimmed.Length != length || !IsHex(trimmed))
        {
            problem ??= $"ROM '{rom}' has an invalid {field} value.";
            return null;
        }

        return trimmed;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    internal static string Bound(string? value) =>
        value is null
            ? string.Empty
            : value.Length > MaxTextLength ? value[..MaxTextLength] : value.Trim();

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? Get(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? NullIfEmpty(value) : null;

    internal static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    internal static void AddIssue(List<DatIssue> issues, string code, string message)
    {
        if (issues.Count < MaxReportedIssues)
        {
            issues.Add(new DatIssue(code, message));
        }
    }

    internal static DatParseResult Fail(DatParseStatus status, string code, string message, List<DatIssue> issues)
    {
        issues.Add(new DatIssue(code, message));
        return new DatParseResult(status, null, issues, rejectedRecords: 0);
    }
}
