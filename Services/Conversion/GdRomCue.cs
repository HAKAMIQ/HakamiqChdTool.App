using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

/// <summary>One track of a GD-ROM CUE, placed on the disc as a GDI line describes it.</summary>
internal sealed record GdRomCueTrack(int Number, bool IsAudio, string FilePath, long Lba);

/// <summary>
/// Reads a GD-ROM CUE (Dreamcast, with "REM SINGLE-DENSITY AREA" and "REM HIGH-DENSITY AREA") and writes the matching
/// GDI. chdman 0.289 places a track's PREGAP from such a CUE at the end of the previous track, so the CHD differs from
/// one made from the GDI; giving chdman a GDI keeps every track exactly as dumped.
/// Only the plain layout is taken: one 2352-byte track per file, INDEX 01 at 00:00:00, optional PREGAP. Anything else
/// returns null and the CUE is converted as before.
/// </summary>
internal static class GdRomCueGdi
{
    public const string StagingFailedMessageKey = "LocWorkflow_GdRomCueStagingFailed";

    private const int SectorBytes = 2352;
    private const long HighDensityStartLba = 45000;
    private const long MaxCueBytes = 64 * 1024;

    public static IReadOnlyList<GdRomCueTrack>? TryRead(string cuePath)
    {
        try
        {
            var info = new FileInfo(cuePath);
            if (!info.Exists || info.Length > MaxCueBytes
                || !string.Equals(info.Extension, ".cue", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Parse(File.ReadAllLines(cuePath), info.DirectoryName ?? string.Empty, path => new FileInfo(path) is { Exists: true } file ? file.Length : -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Parses the CUE lines; <paramref name="fileLength"/> returns a track file's size, or -1 when missing.</summary>
    internal static IReadOnlyList<GdRomCueTrack>? Parse(IReadOnlyList<string> lines, string cueDirectory, Func<string, long> fileLength)
    {
        var tracks = new List<GdRomCueTrack>();
        bool sawHighDensity = false;
        bool inHighDensity = false;
        long nextLba = 0;

        string? file = null;
        int? number = null;
        bool isAudio = false;
        long pregap = 0;
        bool sawIndex01 = false;

        bool Flush()
        {
            if (file is null)
            {
                return true;
            }

            if (number is not int trackNumber || !sawIndex01)
            {
                return false;
            }

            long length = fileLength(file);
            if (length <= 0 || length % SectorBytes != 0)
            {
                return false;
            }

            long lba = (inHighDensity && !tracks.Exists(track => track.Lba >= HighDensityStartLba)
                ? HighDensityStartLba
                : nextLba) + pregap;
            tracks.Add(new GdRomCueTrack(trackNumber, isAudio, file, lba));
            nextLba = lba + length / SectorBytes;

            file = null;
            number = null;
            isAudio = false;
            pregap = 0;
            sawIndex01 = false;
            return true;
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Flush())
                    {
                        return null;
                    }

                    sawHighDensity = true;
                    inHighDensity = true;
                }

                continue;
            }

            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                if (!Flush())
                {
                    return null;
                }

                int open = line.IndexOf('"');
                int close = line.LastIndexOf('"');
                if (open < 0 || close <= open || !line.EndsWith(" BINARY", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string name = line[(open + 1)..close];
                if (name.Length == 0 || Path.IsPathRooted(name) || name.Contains("..", StringComparison.Ordinal)
                    || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return null;
                }

                file = Path.Combine(cueDirectory, name);
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0].ToUpperInvariant())
            {
                case "TRACK" when parts.Length == 3 && file is not null && number is null
                                  && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n == tracks.Count + 1:
                    if (string.Equals(parts[2], "AUDIO", StringComparison.OrdinalIgnoreCase))
                    {
                        isAudio = true;
                    }
                    else if (!string.Equals(parts[2], "MODE1/2352", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    number = n;
                    break;

                case "PREGAP" when parts.Length == 2 && number is not null && !sawIndex01 && TryParseMsf(parts[1], out long frames):
                    pregap = frames;
                    break;

                case "INDEX" when parts.Length == 3 && number is not null && parts[1] == "01" && parts[2] == "00:00:00" && !sawIndex01:
                    sawIndex01 = true;
                    break;

                case "FLAGS" or "CATALOG" or "ISRC" or "TITLE" or "PERFORMER" or "SONGWRITER":
                    break;

                default:
                    // INDEX 00, a second TRACK in one FILE, other modes or unknown commands: not the plain layout.
                    return null;
            }
        }

        if (!Flush() || !sawHighDensity || tracks.Count < 3 || tracks[0].Lba != 0 || tracks[0].IsAudio)
        {
            return null;
        }

        return tracks;
    }

    /// <summary>GDI text for the tracks, with each track file named as staged next to it.</summary>
    public static string BuildGdi(IReadOnlyList<GdRomCueTrack> tracks, Func<GdRomCueTrack, string> stagedName)
    {
        var text = new StringBuilder();
        text.Append(tracks.Count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        foreach (GdRomCueTrack track in tracks)
        {
            text.Append(CultureInfo.InvariantCulture, $"{track.Number} {track.Lba} {(track.IsAudio ? 0 : 4)} {SectorBytes} {stagedName(track)} 0\r\n");
        }

        return text.ToString();
    }

    public static string StagedFileName(GdRomCueTrack track) =>
        string.Create(CultureInfo.InvariantCulture, $"track{track.Number:00}.{(track.IsAudio ? "raw" : "bin")}");

    private static bool TryParseMsf(string value, out long frames)
    {
        frames = 0;
        string[] parts = value.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int frameCount)
            || seconds >= 60 || frameCount >= 75)
        {
            return false;
        }

        frames = ((minutes * 60L) + seconds) * 75L + frameCount;
        return true;
    }
}
