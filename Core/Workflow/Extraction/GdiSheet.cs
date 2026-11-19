using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Core.Workflow.Extraction;

/// <summary>One track line of a GDI: number, start LBA, type, sector size, file name and offset.</summary>
internal sealed record GdiTrackLine(int LineIndex, int TrackNumber, long StartLba, int TrackType, int SectorSize, string FileName, long Offset)
{
    /// <summary>Writes the line the way chdman does, quoting a file name that contains spaces.</summary>
    public string Format(string fileName)
    {
        string name = fileName.Contains(' ', StringComparison.Ordinal) ? "\"" + fileName + "\"" : fileName;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{TrackNumber} {StartLba} {TrackType} {SectorSize} {name} {Offset}");
    }
}

/// <summary>
/// Strict reader for the GDI descriptor of a GD-ROM: a track count followed by one line per
/// track. File names are plain names, optionally quoted; anything else is refused.
/// </summary>
internal static class GdiSheet
{
    private const long MaxGdiBytes = 64 * 1024;
    private const int MaxTracks = 99;

    public static bool TryRead(string path, out string[] lines, out List<GdiTrackLine> tracks)
    {
        lines = [];
        tracks = [];
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxGdiBytes)
            {
                return false;
            }

            lines = File.ReadAllLines(path, Encoding.UTF8);
            return TryParse(lines, out tracks);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static bool TryParse(IReadOnlyList<string> lines, out List<GdiTrackLine> tracks)
    {
        tracks = [];
        int index = 0;
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index]))
        {
            index++;
        }

        if (index >= lines.Count
            || !int.TryParse(lines[index].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || count is < 1 or > MaxTracks)
        {
            return false;
        }

        for (index++; index < lines.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            if (!TryParseTrack(index, lines[index], out GdiTrackLine? track)
                || track!.TrackNumber != tracks.Count + 1)
            {
                tracks = [];
                return false;
            }

            tracks.Add(track);
        }

        if (tracks.Count != count)
        {
            tracks = [];
            return false;
        }

        return true;
    }

    private static bool TryParseTrack(int lineIndex, string line, out GdiTrackLine? track)
    {
        track = null;
        List<string>? tokens = Tokenize(line.Trim());
        if (tokens is null || tokens.Count != 6
            || !int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            || !long.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out long lba)
            || !int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out int type)
            || !int.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out int sectorSize)
            || !long.TryParse(tokens[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long offset)
            || type is not (0 or 4)
            || sectorSize is not (2048 or 2352)
            || !IsPlainFileName(tokens[4]))
        {
            return false;
        }

        track = new GdiTrackLine(lineIndex, number, lba, type, sectorSize, tokens[4], offset);
        return true;
    }

    private static List<string>? Tokenize(string line)
    {
        var tokens = new List<string>();
        int position = 0;
        while (position < line.Length)
        {
            if (char.IsWhiteSpace(line[position]))
            {
                position++;
                continue;
            }

            if (line[position] == '"')
            {
                int close = line.IndexOf('"', position + 1);
                if (close < 0)
                {
                    return null;
                }

                tokens.Add(line[(position + 1)..close]);
                position = close + 1;
                continue;
            }

            int end = position;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            tokens.Add(line[position..end]);
            position = end;
        }

        return tokens;
    }

    private static bool IsPlainFileName(string name) =>
        name.Length > 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
        && name is not ("." or "..")
        && name.Trim() == name;
}
