using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

public enum AlcoholMdsMedium
{
    Cd = 0,
    Dvd = 1
}

/// <summary>How the .mdf stores each sector.</summary>
public enum AlcoholMdsSectorFormat
{
    /// <summary>Raw 2352-byte sectors (data and audio).</summary>
    Raw2352 = 0,

    /// <summary>Raw 2352-byte sectors followed by 96 bytes of subchannel data.</summary>
    Raw2352WithSubchannel = 1,

    /// <summary>2048-byte user data (Mode 1 CD tracks or DVD).</summary>
    Cooked2048 = 2
}

public sealed record AlcoholMdsLayout(
    string ImagePath,
    AlcoholMdsMedium Medium,
    AlcoholMdsSectorFormat SectorFormat,
    long SectorCount,
    IReadOnlyList<CloneCdTrack> Tracks)
{
    public int StoredSectorBytes => SectorFormat switch
    {
        AlcoholMdsSectorFormat.Raw2352WithSubchannel => AlcoholMdsCueBuilder.RawWithSubchannelSectorBytes,
        AlcoholMdsSectorFormat.Cooked2048 => AlcoholMdsCueBuilder.CookedSectorBytes,
        _ => AlcoholMdsCueBuilder.RawSectorBytes
    };
}

public sealed record AlcoholMdsParseResult(bool IsValid, string MessageKey, AlcoholMdsLayout? Layout)
{
    public static AlcoholMdsParseResult Fail(string messageKey) => new(false, messageKey, null);
}

/// <summary>
/// Reads an Alcohol 120% descriptor (.mds, version 1) and maps the .mdf to a
/// CUE that chdman createcd can read, or to a plain 2048-byte image for
/// createdvd. Written from the public MDS layout description (psx-spx,
/// "CDROM Disk Images MDS/MDF"). It fails closed on anything it cannot map
/// exactly: other versions, several sessions, mixed sector sizes, split or
/// missing .mdf, gaps between tracks or a size that does not match.
/// Subchannel data in the .mdf is not carried over.
/// </summary>
public static class AlcoholMdsCueBuilder
{
    public const int RawSectorBytes = 2352;
    public const int RawWithSubchannelSectorBytes = 2448;
    public const int CookedSectorBytes = 2048;

    public const string InvalidMessageKey = "LocWorkflow_MdsInvalid";
    public const string UnsupportedVersionMessageKey = "LocWorkflow_MdsUnsupportedVersion";
    public const string MultiSessionMessageKey = "LocWorkflow_MdsMultiSession";
    public const string ImageMissingMessageKey = "LocWorkflow_MdsImageMissing";
    public const string ImageSizeMismatchMessageKey = "LocWorkflow_MdsImageSizeMismatch";
    public const string StagingFailedMessageKey = "LocWorkflow_MdsStagingFailed";

    private const long MaxDescriptorBytes = 16L * 1024 * 1024;
    private const int HeaderBytes = 0x58;
    private const int SessionBlockBytes = 0x18;
    private const int DataBlockBytes = 0x50;
    private const int IndexBlockBytes = 0x08;
    private const int FilenameBlockBytes = 0x10;
    private const int MaxFilenameChars = 260;
    private const int MaxTracks = 99;
    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");

    public static AlcoholMdsParseResult Parse(string mdsPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(mdsPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length < HeaderBytes || info.Length > MaxDescriptorBytes)
            {
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
            }

            byte[] mds = File.ReadAllBytes(fullPath);
            return ParseDescriptor(fullPath, mds);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }
    }

    /// <summary>
    /// Builds CUE text for a CD layout. chdman resolves FILE relative to the CUE
    /// folder, so the reference must be a plain file name staged next to the CUE.
    /// </summary>
    public static string BuildCue(AlcoholMdsLayout layout, string imageFileName)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFileName);

        if (layout.Medium != AlcoholMdsMedium.Cd)
        {
            throw new ArgumentException("Only CD layouts are written as CUE.", nameof(layout));
        }

        if (!string.Equals(Path.GetFileName(imageFileName), imageFileName, StringComparison.Ordinal)
            || imageFileName.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("CUE image reference must be a plain file name.", nameof(imageFileName));
        }

        bool cooked = layout.SectorFormat == AlcoholMdsSectorFormat.Cooked2048;
        var cue = new StringBuilder();
        cue.Append("FILE \"").Append(imageFileName).Append("\" BINARY\r\n");

        foreach (CloneCdTrack track in layout.Tracks)
        {
            string mode = track.Mode switch
            {
                CloneCdTrackMode.Audio => "AUDIO",
                CloneCdTrackMode.Mode1 => cooked ? "MODE1/2048" : "MODE1/2352",
                _ => "MODE2/2352"
            };

            cue.Append("  TRACK ").Append(track.Number.ToString("00", CultureInfo.InvariantCulture)).Append(' ').Append(mode).Append("\r\n");

            if (track.Index0 is long index0)
            {
                cue.Append("    INDEX 00 ").Append(CloneCdCueBuilder.ToMsf(index0)).Append("\r\n");
            }

            cue.Append("    INDEX 01 ").Append(CloneCdCueBuilder.ToMsf(track.Index1)).Append("\r\n");
        }

        return cue.ToString();
    }

    private sealed record RawTrack(
        int Point,
        byte Mode,
        byte Subchannels,
        int SectorSize,
        int StartLba,
        ulong StartOffset,
        uint FilenameCount,
        string? Filename,
        uint? PregapSectors,
        uint? LengthSectors);

    private static AlcoholMdsParseResult ParseDescriptor(string mdsPath, byte[] mds)
    {
        if (!mds.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        if (mds[0x10] != 1)
        {
            return AlcoholMdsParseResult.Fail(UnsupportedVersionMessageKey);
        }

        ushort mediumType = BinaryPrimitives.ReadUInt16LittleEndian(mds.AsSpan(0x12));
        AlcoholMdsMedium medium;
        switch (mediumType)
        {
            case 0x00 or 0x01 or 0x02:
                medium = AlcoholMdsMedium.Cd;
                break;
            case 0x10 or 0x12:
                medium = AlcoholMdsMedium.Dvd;
                break;
            default:
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        ushort sessions = BinaryPrimitives.ReadUInt16LittleEndian(mds.AsSpan(0x14));
        if (sessions != 1)
        {
            return AlcoholMdsParseResult.Fail(sessions > 1 ? MultiSessionMessageKey : InvalidMessageKey);
        }

        uint sessionOffset = BinaryPrimitives.ReadUInt32LittleEndian(mds.AsSpan(0x50));
        if (!InRange(mds, sessionOffset, SessionBlockBytes))
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        ReadOnlySpan<byte> session = mds.AsSpan((int)sessionOffset, SessionBlockBytes);
        int totalBlocks = session[0x0A];
        ushort firstTrack = BinaryPrimitives.ReadUInt16LittleEndian(session[0x0C..]);
        ushort lastTrack = BinaryPrimitives.ReadUInt16LittleEndian(session[0x0E..]);
        uint dataBlocksOffset = BinaryPrimitives.ReadUInt32LittleEndian(session[0x14..]);

        if (totalBlocks == 0
            || firstTrack != 1
            || lastTrack < firstTrack
            || lastTrack > MaxTracks
            || !InRange(mds, dataBlocksOffset, (long)totalBlocks * DataBlockBytes))
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        var tracks = new List<RawTrack>();
        for (int block = 0; block < totalBlocks; block++)
        {
            ReadOnlySpan<byte> data = mds.AsSpan((int)dataBlocksOffset + block * DataBlockBytes, DataBlockBytes);
            int point = data[0x04];
            if (point >= 0xA0)
            {
                continue;
            }

            if (!TryReadTrack(mds, data, point, out RawTrack? track))
            {
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
            }

            tracks.Add(track!);
        }

        if (tracks.Count != lastTrack - firstTrack + 1)
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Point != firstTrack + i)
            {
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
            }
        }

        RawTrack first = tracks[0];
        foreach (RawTrack track in tracks)
        {
            if (track.FilenameCount != 1
                || track.SectorSize != first.SectorSize
                || track.Subchannels != first.Subchannels
                || !string.Equals(track.Filename, first.Filename, StringComparison.OrdinalIgnoreCase))
            {
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
            }
        }

        string? imagePath = ResolveImagePath(mdsPath, first.Filename);
        if (imagePath is null)
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        var image = new FileInfo(imagePath);
        if (!image.Exists)
        {
            return AlcoholMdsParseResult.Fail(ImageMissingMessageKey);
        }

        return medium == AlcoholMdsMedium.Dvd
            ? BuildDvdLayout(image, tracks)
            : BuildCdLayout(image, tracks);
    }

    private static bool TryReadTrack(byte[] mds, ReadOnlySpan<byte> data, int point, out RawTrack? track)
    {
        track = null;
        if (point is < 1 or > MaxTracks)
        {
            return false;
        }

        uint indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]);
        uint? pregap = null;
        uint? length = null;
        if (indexOffset != 0)
        {
            if (!InRange(mds, indexOffset, IndexBlockBytes))
            {
                return false;
            }

            pregap = BinaryPrimitives.ReadUInt32LittleEndian(mds.AsSpan((int)indexOffset));
            length = BinaryPrimitives.ReadUInt32LittleEndian(mds.AsSpan((int)indexOffset + 4));
        }

        uint filenameBlockOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x34..]);
        string? filename = ReadFilename(mds, filenameBlockOffset);

        track = new RawTrack(
            Point: point,
            Mode: data[0x00],
            Subchannels: data[0x01],
            SectorSize: BinaryPrimitives.ReadUInt16LittleEndian(data[0x10..]),
            StartLba: BinaryPrimitives.ReadInt32LittleEndian(data[0x24..]),
            StartOffset: BinaryPrimitives.ReadUInt64LittleEndian(data[0x28..]),
            FilenameCount: BinaryPrimitives.ReadUInt32LittleEndian(data[0x30..]),
            Filename: filename,
            PregapSectors: pregap,
            LengthSectors: length);
        return filename is not null;
    }

    private static string? ReadFilename(byte[] mds, uint blockOffset)
    {
        if (!InRange(mds, blockOffset, FilenameBlockBytes))
        {
            return null;
        }

        uint nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(mds.AsSpan((int)blockOffset));
        bool wide = mds[blockOffset + 4] switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentException("Unknown MDS filename format.")
        };

        if (nameOffset >= mds.Length)
        {
            return null;
        }

        var name = new StringBuilder();
        int unit = wide ? 2 : 1;
        for (long position = nameOffset; position + unit <= mds.Length; position += unit)
        {
            char c = wide
                ? (char)BinaryPrimitives.ReadUInt16LittleEndian(mds.AsSpan((int)position))
                : (char)mds[position];
            if (c == '\0')
            {
                return name.Length == 0 ? null : name.ToString();
            }

            if (name.Length >= MaxFilenameChars)
            {
                return null;
            }

            name.Append(c);
        }

        return null;
    }

    /// <summary>
    /// "*.mdf" means the .mds name with the .mdf extension. Otherwise only a
    /// plain .mdf file name in the same folder is accepted.
    /// </summary>
    private static string? ResolveImagePath(string mdsPath, string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(mdsPath)!;
        if (string.Equals(filename, "*.mdf", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(mdsPath, ".mdf");
        }

        if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetFileName(filename), filename, StringComparison.Ordinal)
            || filename.Trim() != filename
            || filename.EndsWith('.')
            || !string.Equals(Path.GetExtension(filename), ".mdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(directory, filename);
    }

    private static AlcoholMdsParseResult BuildDvdLayout(FileInfo image, List<RawTrack> tracks)
    {
        RawTrack track = tracks[0];
        if (tracks.Count != 1
            || track.SectorSize != CookedSectorBytes
            || track.Subchannels != 0
            || track.StartOffset != 0)
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        if (image.Length <= 0 || image.Length % CookedSectorBytes != 0)
        {
            return AlcoholMdsParseResult.Fail(ImageSizeMismatchMessageKey);
        }

        long sectors = image.Length / CookedSectorBytes;
        if (track.LengthSectors is uint length && length != 0 && length != sectors)
        {
            return AlcoholMdsParseResult.Fail(ImageSizeMismatchMessageKey);
        }

        return new AlcoholMdsParseResult(
            true,
            string.Empty,
            new AlcoholMdsLayout(image.FullName, AlcoholMdsMedium.Dvd, AlcoholMdsSectorFormat.Cooked2048, sectors, []));
    }

    private static AlcoholMdsParseResult BuildCdLayout(FileInfo image, List<RawTrack> tracks)
    {
        RawTrack first = tracks[0];
        AlcoholMdsSectorFormat format;
        if (first.Subchannels == 8 && first.SectorSize == RawWithSubchannelSectorBytes)
        {
            format = AlcoholMdsSectorFormat.Raw2352WithSubchannel;
        }
        else if (first.Subchannels == 0 && first.SectorSize == RawSectorBytes)
        {
            format = AlcoholMdsSectorFormat.Raw2352;
        }
        else if (first.Subchannels == 0 && first.SectorSize == CookedSectorBytes)
        {
            format = AlcoholMdsSectorFormat.Cooked2048;
        }
        else
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        // The .mdf starts at 00:02:00, so a sector's place in the file is its LBA.
        if (first.StartLba != 0 || first.StartOffset != 0)
        {
            return AlcoholMdsParseResult.Fail(InvalidMessageKey);
        }

        var result = new List<CloneCdTrack>(tracks.Count);
        long sectorSize = first.SectorSize;
        long expectedStart = 0;

        foreach (RawTrack track in tracks)
        {
            CloneCdTrackMode? mode = (track.Mode & 0x0F) switch
            {
                0x09 => CloneCdTrackMode.Audio,
                0x0A => CloneCdTrackMode.Mode1,
                0x0B or 0x0C or 0x0D => CloneCdTrackMode.Mode2,
                _ => null
            };

            if (mode is null
                || (format == AlcoholMdsSectorFormat.Cooked2048 && mode != CloneCdTrackMode.Mode1)
                || track.LengthSectors is not uint length
                || length == 0
                || track.PregapSectors is not uint pregap
                || track.StartLba < 0)
            {
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
            }

            long index1 = track.StartLba;
            long? index0 = null;
            if (track.Point > 1 && pregap > 0)
            {
                index0 = index1 - pregap;
            }

            long trackStart = index0 ?? index1;
            ulong offsetAtIndex1 = (ulong)(index1 * sectorSize);
            ulong offsetAtIndex0 = (ulong)(trackStart * sectorSize);
            if (trackStart != expectedStart
                || (track.StartOffset != offsetAtIndex1 && track.StartOffset != offsetAtIndex0))
            {
                return AlcoholMdsParseResult.Fail(InvalidMessageKey);
            }

            result.Add(new CloneCdTrack(track.Point, mode.Value, index0, index1));
            expectedStart = index1 + length;
        }

        if (image.Length != expectedStart * sectorSize)
        {
            return AlcoholMdsParseResult.Fail(ImageSizeMismatchMessageKey);
        }

        return new AlcoholMdsParseResult(
            true,
            string.Empty,
            new AlcoholMdsLayout(image.FullName, AlcoholMdsMedium.Cd, format, expectedStart, result));
    }

    private static bool InRange(byte[] buffer, long offset, long length) =>
        offset > 0 && length >= 0 && offset <= buffer.Length && length <= buffer.Length - offset;
}
