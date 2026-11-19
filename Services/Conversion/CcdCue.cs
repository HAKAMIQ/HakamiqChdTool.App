using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

public enum CloneCdTrackMode
{
    Audio = 0,
    Mode1 = 1,
    Mode2 = 2
}

public sealed record CloneCdTrack(int Number, CloneCdTrackMode Mode, long? Index0, long Index1);

public sealed record CloneCdLayout(string ImagePath, long SectorCount, IReadOnlyList<CloneCdTrack> Tracks);

public sealed record CloneCdParseResult(bool IsValid, string MessageKey, CloneCdLayout? Layout)
{
    public static CloneCdParseResult Fail(string messageKey) => new(false, messageKey, null);
}

/// <summary>
/// Reads a CloneCD control file (.ccd) and writes an equivalent CUE for the
/// raw 2352-byte .img so chdman createcd can read it. Written from the public
/// CloneCD control-file layout; it fails closed on anything it cannot map
/// exactly (multiple sessions, scrambled data, missing or misaligned image).
/// Subchannel data (.sub) is not carried into the CUE.
/// </summary>
public static class CloneCdCueBuilder
{
    public const int RawSectorBytes = 2352;
    public const string InvalidControlFileMessageKey = "LocWorkflow_CcdInvalid";
    public const string MultiSessionMessageKey = "LocWorkflow_CcdMultiSession";
    public const string ScrambledMessageKey = "LocWorkflow_CcdScrambled";
    public const string ImageMissingMessageKey = "LocWorkflow_CcdImageMissing";
    public const string ImageNotAlignedMessageKey = "LocWorkflow_CcdImageNotAligned";
    public const string StagingFailedMessageKey = "LocWorkflow_CcdStagingFailed";

    private const long MaxControlFileBytes = 256 * 1024;
    private const int MaxTracks = 99;

    public static CloneCdParseResult Parse(string ccdPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(ccdPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxControlFileBytes)
            {
                return CloneCdParseResult.Fail(InvalidControlFileMessageKey);
            }

            Dictionary<string, Dictionary<string, string>> sections = ReadSections(File.ReadAllLines(fullPath, Encoding.UTF8));

            if (!sections.ContainsKey("clonecd") || !sections.TryGetValue("disc", out Dictionary<string, string>? disc))
            {
                return CloneCdParseResult.Fail(InvalidControlFileMessageKey);
            }

            if (!TryReadInt(disc, "sessions", out int sessions) || sessions != 1)
            {
                return CloneCdParseResult.Fail(sessions > 1 ? MultiSessionMessageKey : InvalidControlFileMessageKey);
            }

            if (TryReadInt(disc, "datatracksscrambled", out int scrambled) && scrambled != 0)
            {
                return CloneCdParseResult.Fail(ScrambledMessageKey);
            }

            string imagePath = Path.ChangeExtension(fullPath, ".img");
            var image = new FileInfo(imagePath);
            if (!image.Exists)
            {
                return CloneCdParseResult.Fail(ImageMissingMessageKey);
            }

            if (image.Length <= 0 || image.Length % RawSectorBytes != 0)
            {
                return CloneCdParseResult.Fail(ImageNotAlignedMessageKey);
            }

            long sectorCount = image.Length / RawSectorBytes;
            List<CloneCdTrack> tracks = ReadTracks(sections, sectorCount);
            return tracks.Count == 0
                ? CloneCdParseResult.Fail(InvalidControlFileMessageKey)
                : new CloneCdParseResult(true, string.Empty, new CloneCdLayout(image.FullName, sectorCount, tracks));
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            return CloneCdParseResult.Fail(InvalidControlFileMessageKey);
        }
    }

    /// <summary>
    /// Builds CUE text. chdman resolves FILE relative to the CUE folder, so the
    /// reference must be a plain file name staged next to the generated CUE.
    /// </summary>
    public static string BuildCue(CloneCdLayout layout, string imageFileName)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFileName);

        if (!string.Equals(Path.GetFileName(imageFileName), imageFileName, StringComparison.Ordinal)
            || imageFileName.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("CUE image reference must be a plain file name.", nameof(imageFileName));
        }

        var cue = new StringBuilder();
        cue.Append("FILE \"").Append(imageFileName).Append("\" BINARY\r\n");

        foreach (CloneCdTrack track in layout.Tracks)
        {
            string mode = track.Mode switch
            {
                CloneCdTrackMode.Audio => "AUDIO",
                CloneCdTrackMode.Mode1 => "MODE1/2352",
                _ => "MODE2/2352"
            };

            cue.Append("  TRACK ").Append(track.Number.ToString("00", CultureInfo.InvariantCulture)).Append(' ').Append(mode).Append("\r\n");

            if (track.Index0 is long index0)
            {
                cue.Append("    INDEX 00 ").Append(ToMsf(index0)).Append("\r\n");
            }

            cue.Append("    INDEX 01 ").Append(ToMsf(track.Index1)).Append("\r\n");
        }

        return cue.ToString();
    }

    internal static string ToMsf(long lba)
    {
        long minutes = lba / (60 * 75);
        long seconds = lba / 75 % 60;
        long frames = lba % 75;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{minutes:00}:{seconds:00}:{frames:00}");
    }

    private static List<CloneCdTrack> ReadTracks(
        Dictionary<string, Dictionary<string, string>> sections,
        long sectorCount)
    {
        var tracks = new List<CloneCdTrack>();
        long previousStart = -1;

        for (int number = 1; number <= MaxTracks; number++)
        {
            if (!sections.TryGetValue("track " + number.ToString(CultureInfo.InvariantCulture), out Dictionary<string, string>? section))
            {
                break;
            }

            if (!TryReadInt(section, "mode", out int modeValue) || modeValue is < 0 or > 2
                || !TryReadLong(section, "index 1", out long index1))
            {
                return [];
            }

            long? index0 = TryReadLong(section, "index 0", out long value0) ? value0 : null;
            long trackStart = index0 ?? index1;

            if (index1 < 0
                || index1 >= sectorCount
                || (index0 is long i0 && (i0 < 0 || i0 > index1))
                || trackStart <= previousStart)
            {
                return [];
            }

            tracks.Add(new CloneCdTrack(number, (CloneCdTrackMode)modeValue, index0, index1));
            previousStart = trackStart;
        }

        // Any [TRACK n] beyond the consecutive run means the layout is not one we can map.
        foreach (string name in sections.Keys)
        {
            if (name.StartsWith("track ", StringComparison.Ordinal)
                && (!int.TryParse(name.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n > tracks.Count))
            {
                return [];
            }
        }

        return tracks;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadSections(string[] lines)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                string name = line[1..^1].Trim().ToLowerInvariant();
                if (!sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                    sections[name] = current;
                }

                continue;
            }

            int equals = line.IndexOf('=');
            if (current is null || equals <= 0)
            {
                continue;
            }

            current[line[..equals].Trim().ToLowerInvariant()] = line[(equals + 1)..].Trim();
        }

        return sections;
    }

    private static bool TryReadInt(Dictionary<string, string> section, string key, out int value)
    {
        value = 0;
        return section.TryGetValue(key, out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadLong(Dictionary<string, string> section, string key, out long value)
    {
        value = 0;
        return section.TryGetValue(key, out string? text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
