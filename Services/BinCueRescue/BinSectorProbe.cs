using System;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal static class BinSectorProbe
{
    private const int RawSectorSize = 2352;
    private const int CookedSectorSize = 2048;
    private const int ModeByteOffset = 15;
    private const int IsoPrimaryVolumeDescriptorSector = 16;
    private const int MinimumUsefulSampleCount = 2;

    private static readonly byte[] SyncPattern =
    [
        0x00,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0x00
    ];

    public static BinSectorProbeResult Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath;

        try
        {
            fullPath = NormalizeFullPath(path);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return new BinSectorProbeResult(
                BinTrackKind.Unknown);
        }

        FileInfo file = new(fullPath);

        if (!TryGetSafeFileLength(file, out long length))
        {
            return new BinSectorProbeResult(
                BinTrackKind.Unknown);
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: RawSectorSize,
                FileOptions.SequentialScan);

            bool canBeRaw2352 =
                length % RawSectorSize == 0;

            bool canBeCooked2048 =
                length % CookedSectorSize == 0;

            if (canBeRaw2352)
            {
                BinSectorProbeResult rawResult =
                    ProbeRaw2352(
                        length,
                        stream);

                if (canBeCooked2048
                    && rawResult.Kind is
                        BinTrackKind.Raw2352AudioCandidate
                        or BinTrackKind.Unknown)
                {
                    BinSectorProbeResult cookedResult =
                        ProbeCooked2048(
                            length,
                            stream);

                    if (cookedResult.HasConfirmedIso9660Pvd)
                    {
                        return cookedResult;
                    }
                }

                return rawResult;
            }

            if (canBeCooked2048)
            {
                return ProbeCooked2048(
                    length,
                    stream);
            }
        }
        catch (Exception ex) when (
            IsIoFailure(ex) || IsPathFailure(ex))
        {
            return new BinSectorProbeResult(
                BinTrackKind.Unknown);
        }

        return new BinSectorProbeResult(
            BinTrackKind.NonStandard);
    }

    private static bool TryGetSafeFileLength(
        FileInfo file,
        out long length)
    {
        length = 0;

        try
        {
            if (!file.Exists)
            {
                return false;
            }

            if (HasReparsePointInExistingPathFromVolumeRoot(
                    file.FullName))
            {
                return false;
            }

            length = file.Length;

            return length > 0;
        }
        catch (Exception ex) when (
            IsIoFailure(ex) || IsPathFailure(ex))
        {
            length = 0;
            return false;
        }
    }

    private static BinSectorProbeResult ProbeRaw2352(
        long length,
        FileStream stream)
    {
        long[] offsets =
            BuildSampleOffsets(
                length,
                RawSectorSize);

        int mode1Count = 0;
        int mode2Count = 0;
        int zeroModeCount = 0;
        int syncCount = 0;

        foreach (long offset in offsets)
        {
            byte[] sector =
                ReadBytes(
                    stream,
                    offset,
                    RawSectorSize);

            if (sector.Length < ModeByteOffset + 1)
            {
                continue;
            }

            if (!HasSyncPattern(sector))
            {
                continue;
            }

            syncCount++;

            switch (sector[ModeByteOffset])
            {
                case 0x01:
                    mode1Count++;
                    break;

                case 0x02:
                    mode2Count++;
                    break;

                case 0x00:
                    zeroModeCount++;
                    break;
            }
        }

        if (mode1Count > 0 && mode2Count > 0)
        {
            return new BinSectorProbeResult(
                BinTrackKind.NonStandard);
        }

        if (mode1Count > 0)
        {
            return new BinSectorProbeResult(
                BinTrackKind.Raw2352Mode1);
        }

        if (mode2Count > 0)
        {
            return new BinSectorProbeResult(
                BinTrackKind.Raw2352Mode2);
        }

        if (zeroModeCount > 0)
        {
            return new BinSectorProbeResult(
                BinTrackKind.Unknown);
        }

        if (LooksLikeAudioCandidate(
                length,
                offsets.Length,
                syncCount))
        {
            return new BinSectorProbeResult(
                BinTrackKind.Raw2352AudioCandidate);
        }

        return new BinSectorProbeResult(
            BinTrackKind.Unknown);
    }

    private static BinSectorProbeResult ProbeCooked2048(
        long length,
        FileStream stream)
    {
        long pvdOffset =
            (long)IsoPrimaryVolumeDescriptorSector
            * CookedSectorSize;

        if (pvdOffset + 6 <= length)
        {
            byte[] pvd =
                ReadBytes(
                    stream,
                    pvdOffset,
                    6);

            if (pvd.Length >= 6
                && pvd[0] == 0x01
                && IsCd001(pvd, 1))
            {
                return new BinSectorProbeResult(
                    BinTrackKind.Cooked2048Data,
                    HasConfirmedIso9660Pvd: true);
            }
        }

        return new BinSectorProbeResult(
            BinTrackKind.Cooked2048Data);
    }

    private static bool LooksLikeAudioCandidate(
        long length,
        int sampleCount,
        int syncCount)
    {
        return length >= RawSectorSize
            && sampleCount >= MinimumUsefulSampleCount
            && syncCount == 0;
    }

    private static long[] BuildSampleOffsets(
        long length,
        int sectorSize)
    {
        long sectorCount =
            length / sectorSize;

        if (sectorCount <= 0)
        {
            return [];
        }

        long middleSector =
            Math.Max(
                0,
                sectorCount / 2);

        long lastSector =
            Math.Max(
                0,
                sectorCount - 1);

        long[] requestedSectors =
        [
            0,
            1,
            4,
            16,
            middleSector,
            lastSector
        ];

        return
        [
            .. requestedSectors
                .Where(
                    sector =>
                        sector >= 0
                        && sector < sectorCount)
                .Distinct()
                .Select(
                    sector =>
                        sector * sectorSize)
                .Order()
        ];
    }

    private static byte[] ReadBytes(
        FileStream stream,
        long offset,
        int count)
    {
        if (offset < 0
            || offset >= stream.Length
            || count <= 0)
        {
            return [];
        }

        int safeCount =
            (int)Math.Min(
                count,
                stream.Length - offset);

        byte[] buffer =
            new byte[safeCount];

        stream.Seek(
            offset,
            SeekOrigin.Begin);

        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = stream.Read(
                buffer,
                totalRead,
                buffer.Length - totalRead);

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(
            ref buffer,
            totalRead);

        return buffer;
    }

    private static bool HasSyncPattern(
        byte[] sector)
    {
        if (sector.Length < SyncPattern.Length)
        {
            return false;
        }

        for (int i = 0;
             i < SyncPattern.Length;
             i++)
        {
            if (sector[i] != SyncPattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCd001(
        byte[] buffer,
        int offset)
    {
        return buffer.Length >= offset + 5
            && buffer[offset] == (byte)'C'
            && buffer[offset + 1] == (byte)'D'
            && buffer[offset + 2] == (byte)'0'
            && buffer[offset + 3] == (byte)'0'
            && buffer[offset + 4] == (byte)'1';
    }

    private static bool
        HasReparsePointInExistingPathFromVolumeRoot(
            string candidatePath)
    {
        try
        {
            string candidate =
                NormalizeFullPath(candidatePath);

            string? root =
                Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(
                candidate,
                root);
        }
        catch (Exception ex) when (
            IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(
        string candidatePath,
        string rootPath)
    {
        try
        {
            string candidate =
                NormalizeFullPath(candidatePath);

            string root =
                NormalizeFullPath(rootPath);

            if (!IsSamePathOrChild(
                    candidate,
                    root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current)
                        || Directory.Exists(current))
                    && IsExistingPathReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(
                        current,
                        root))
                {
                    return false;
                }

                string? parent =
                    Directory.GetParent(current)?.FullName;

                if (string.IsNullOrWhiteSpace(parent)
                    || PathsEqual(
                        parent,
                        current))
                {
                    return true;
                }

                current =
                    NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (
            IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(
        string path)
    {
        try
        {
            if (!File.Exists(path)
                && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path)
                    & FileAttributes.ReparsePoint)
                == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (
            IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(
        string candidatePath,
        string rootPath)
    {
        string candidate =
            NormalizeFullPath(candidatePath);

        string root =
            NormalizeFullPath(rootPath);

        return string.Equals(
                candidate,
                root,
                StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(
                EnsureDirectorySeparatorSuffix(root),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFullPath(
        string path)
    {
        string fullPath =
            Path.GetFullPath(path);

        string? root =
            Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureDirectorySeparatorSuffix(
        string path)
    {
        return path.EndsWith(
                Path.DirectorySeparatorChar)
            || path.EndsWith(
                Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsPathFailure(
        Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsIoFailure(
        Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException;
    }
}