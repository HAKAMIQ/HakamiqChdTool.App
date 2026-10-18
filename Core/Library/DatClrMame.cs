using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Core.Library;

/// <summary>
/// ClrMamePro text DATs (<c>clrmamepro ( … )</c> header, <c>game ( … )</c> blocks with
/// <c>rom ( name … size … crc … md5 … sha1 … )</c>). This is the format libretro-database
/// ships. Tokens are bare words, quoted strings and parentheses; unknown keys are skipped.
/// A game without a <c>name</c> falls back to its <c>comment</c> (libretro hash-only DATs);
/// a ROM may be nameless when it carries a hash. Nesting depth and token length are bounded.
/// </summary>
internal static class DatClrMamePro
{
    private const int MaxDepth = 4;
    private const int MaxTokenLength = 4096;

    private enum Kind
    {
        Word,
        Open,
        Close,
        End
    }

    private readonly record struct Token(Kind Kind, string Text);

    public static DatParseResult Parse(
        Stream stream,
        string fullPath,
        List<DatIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
            var lexer = new Lexer(reader);
            DatHeader header = new(null, null, null, null, null, null);
            List<DatGame> games = [];
            int romCount = 0;
            int rejected = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Token keyword = lexer.Next();

                if (keyword.Kind == Kind.End)
                {
                    break;
                }

                if (keyword.Kind != Kind.Word || lexer.Next().Kind != Kind.Open)
                {
                    return DatParser.Fail(DatParseStatus.Malformed, "cmpro-malformed", "Expected 'keyword (' at block start.", issues);
                }

                string name = keyword.Text.ToLowerInvariant();
                Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
                List<Dictionary<string, string>> roms = [];
                List<string> releaseRegions = [];
                ReadBlock(lexer, depth: 1, fields, roms, releaseRegions, cancellationToken);

                if (name == "clrmamepro")
                {
                    header = new DatHeader(
                        Field(fields, "name"),
                        Field(fields, "description"),
                        Field(fields, "version"),
                        Field(fields, "date"),
                        Field(fields, "author"),
                        Field(fields, "homepage"));
                    continue;
                }

                if (name is not ("game" or "machine"))
                {
                    continue;
                }

                if (games.Count >= DatParser.MaxGames)
                {
                    return DatParser.Fail(DatParseStatus.TooLarge, "dat-too-many-games", $"More than {DatParser.MaxGames} games.", issues);
                }

                romCount += roms.Count;

                if (romCount > DatParser.MaxRoms)
                {
                    return DatParser.Fail(DatParseStatus.TooLarge, "dat-too-many-roms", $"More than {DatParser.MaxRoms} ROM records.", issues);
                }

                DatGame? game = BuildGame(fields, roms, releaseRegions, ref rejected, issues);

                if (game is null)
                {
                    rejected++;
                    DatParser.AddIssue(issues, "game-unnamed", "A game block without a name or comment was rejected.");
                    continue;
                }

                games.Add(game);
            }

            return new DatParseResult(DatParseStatus.Parsed, new DatFile(fullPath, header, games), issues, rejected);
        }
        catch (FormatException ex)
        {
            return DatParser.Fail(DatParseStatus.Malformed, "cmpro-malformed", ex.Message, issues);
        }
        catch (OperationCanceledException)
        {
            return DatParser.Fail(DatParseStatus.Cancelled, "cancelled", "Parsing was cancelled.", issues);
        }
    }

    // Reads key/value pairs until the matching ')'. rom blocks are collected; release blocks
    // contribute their region; other nested blocks are skipped.
    private static void ReadBlock(
        Lexer lexer,
        int depth,
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> roms,
        List<string> releaseRegions,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("Block nesting is too deep.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Token key = lexer.Next();

            if (key.Kind == Kind.Close)
            {
                return;
            }

            if (key.Kind != Kind.Word)
            {
                throw new FormatException("Unexpected token inside a block.");
            }

            Token value = lexer.Next();

            if (value.Kind == Kind.Word)
            {
                fields.TryAdd(key.Text, value.Text);
                continue;
            }

            if (value.Kind != Kind.Open)
            {
                throw new FormatException("A key must be followed by a value or a block.");
            }

            string keyName = key.Text.ToLowerInvariant();

            if (keyName is "rom" or "release")
            {
                Dictionary<string, string> nested = new(StringComparer.OrdinalIgnoreCase);
                ReadBlock(lexer, depth + 1, nested, [], [], cancellationToken);

                if (keyName == "rom")
                {
                    roms.Add(nested);
                }
                else if (nested.TryGetValue("region", out string? region))
                {
                    releaseRegions.Add(region);
                }
            }
            else
            {
                ReadBlock(lexer, depth + 1, new(StringComparer.OrdinalIgnoreCase), [], [], cancellationToken);
            }
        }
    }

    private static DatGame? BuildGame(
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> romBlocks,
        List<string> releaseRegions,
        ref int rejected,
        List<DatIssue> issues)
    {
        string? name = DatParser.NullIfEmpty(DatParser.Bound(Field(fields, "name") ?? Field(fields, "comment")));

        if (name is null)
        {
            return null;
        }

        List<DatRom> roms = [];

        foreach (Dictionary<string, string> block in romBlocks)
        {
            string? problem = null;
            string romName = DatParser.Bound(Field(block, "name"));
            long? size = null;

            if (Field(block, "size") is { } sizeText)
            {
                if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
                {
                    problem = $"ROM '{romName}' has an invalid size.";
                }
                else
                {
                    size = parsed;
                }
            }

            string? crc = DatParser.Hex(Field(block, "crc"), 8, romName, "crc", ref problem);
            string? md5 = DatParser.Hex(Field(block, "md5"), 32, romName, "md5", ref problem);
            string? sha1 = DatParser.Hex(Field(block, "sha1"), 40, romName, "sha1", ref problem);

            if (problem is null && romName.Length == 0 && crc is null && md5 is null && sha1 is null)
            {
                problem = "ROM without a name or any hash.";
            }

            if (problem is not null)
            {
                rejected++;
                DatParser.AddIssue(issues, "rom-rejected", $"Game '{name}': {problem}");
                continue;
            }

            roms.Add(new DatRom(romName, size, crc, md5, sha1));
        }

        return new DatGame(
            name,
            DatParser.NullIfEmpty(DatParser.Bound(Field(fields, "description"))),
            DatParser.NullIfEmpty(DatParser.Bound(Field(fields, "region") ?? (releaseRegions.Count > 0 ? releaseRegions[0] : null))),
            DatParser.NullIfEmpty(DatParser.Bound(Field(fields, "version") ?? Field(fields, "rev"))),
            DatParser.NullIfEmpty(DatParser.Bound(Field(fields, "cloneof"))),
            roms);
    }

    private static string? Field(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) ? DatParser.NullIfEmpty(value) : null;

    private sealed class Lexer(TextReader reader)
    {
        public Token Next()
        {
            int c;

            while ((c = reader.Peek()) >= 0 && char.IsWhiteSpace((char)c))
            {
                reader.Read();
            }

            if (c < 0)
            {
                return new Token(Kind.End, string.Empty);
            }

            if (c == '(')
            {
                reader.Read();
                return new Token(Kind.Open, "(");
            }

            if (c == ')')
            {
                reader.Read();
                return new Token(Kind.Close, ")");
            }

            StringBuilder text = new();

            if (c == '"')
            {
                reader.Read();

                while (true)
                {
                    int next = reader.Read();

                    if (next < 0)
                    {
                        throw new FormatException("Unterminated quoted string.");
                    }

                    if (next == '"')
                    {
                        break;
                    }

                    text.Append((char)next);
                    Guard(text);
                }

                return new Token(Kind.Word, text.ToString());
            }

            while ((c = reader.Peek()) >= 0 && !char.IsWhiteSpace((char)c) && c != '(' && c != ')')
            {
                text.Append((char)reader.Read());
                Guard(text);
            }

            return new Token(Kind.Word, text.ToString());
        }

        private static void Guard(StringBuilder text)
        {
            if (text.Length > MaxTokenLength)
            {
                throw new FormatException("Token is too long.");
            }
        }
    }
}
